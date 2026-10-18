using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace UkrainianLocalization
{
    public enum Gender
    {
        None,
        MasculineAnimate,
        MasculineInanimate,
        FeminineAnimate,
        FeminineInanimate,
        NeuterAnimate,
        NeuterInanimate,
        PluralAnimate,
        PluralInanimate
    }

    public enum Case
    {
        Nominative,
        Genitive,
        Dative,
        Accusative,
        Instrumental,
        Locative,
        Vocative
    }

    internal static class GenderInfo
    {
        public static Gender FromToken(string token)
        {
            switch (token)
            {
                case ".MA": return Gender.MasculineAnimate;
                case ".MI": return Gender.MasculineInanimate;
                case ".FA": return Gender.FeminineAnimate;
                case ".FI": return Gender.FeminineInanimate;
                case ".NA": return Gender.NeuterAnimate;
                case ".NI": return Gender.NeuterInanimate;
                case ".PA": return Gender.PluralAnimate;
                case ".PI": return Gender.PluralInanimate;
                default: return Gender.None;
            }
        }

        public static string Code(Gender g)
        {
            switch (g)
            {
                case Gender.MasculineAnimate: return "MA";
                case Gender.MasculineInanimate: return "MI";
                case Gender.FeminineAnimate: return "FA";
                case Gender.FeminineInanimate: return "FI";
                case Gender.NeuterAnimate: return "NA";
                case Gender.NeuterInanimate: return "NI";
                case Gender.PluralAnimate: return "PA";
                case Gender.PluralInanimate: return "PI";
                default: return "";
            }
        }

        public static bool IsAnimate(Gender g)
        {
            return g == Gender.MasculineAnimate || g == Gender.FeminineAnimate || g == Gender.NeuterAnimate || g == Gender.PluralAnimate;
        }

        public static bool IsMasculine(Gender g) => g == Gender.MasculineAnimate || g == Gender.MasculineInanimate;
        public static bool IsFeminine(Gender g) => g == Gender.FeminineAnimate || g == Gender.FeminineInanimate;
        public static bool IsNeuter(Gender g) => g == Gender.NeuterAnimate || g == Gender.NeuterInanimate;
        public static bool IsPluralOnly(Gender g) => g == Gender.PluralAnimate || g == Gender.PluralInanimate;
    }

    /// <summary>
    /// Ukrainian declension: a curated paradigm dictionary for every noun used in the game's names,
    /// plus rule-based fallbacks for nouns that are not in the dictionary and for all adjectives.
    /// </summary>
    internal static class Morphology
    {
        private const string Vowels = "аеєиіїоуюя";
        private const string Sibilants = "жчшщ";

        private static readonly CultureInfo Culture = CreateCulture();

        // key: lowercase nominative -> gender code -> 12 forms
        // forms order: g, d, a, i, l, v, nPl, gPl, dPl, aPl, iPl, lPl
        private static Dictionary<string, Dictionary<string, string[]>> _nouns;
        private static readonly object Sync = new object();

        private static readonly HashSet<string> SoftAdjectiveStems = new HashSet<string>
        {
            "син", "літн", "зимн", "осінн", "весінн", "вечірн", "ранн", "пізн", "давн", "крайн", "справжн",
            "останн", "середн", "верхн", "нижн", "передн", "задн", "внутрішн", "зовнішн", "горішн", "долішн",
            "домашн", "тутешн", "ближн", "дальн", "сусідн", "вишн", "здешн", "тодішн", "колишн", "нинішн", "вчорашн", "сьогоднішн", "торішн", "житн", "безкрайн"
        };

        private static CultureInfo CreateCulture()
        {
            try
            {
                return new CultureInfo("uk-UA");
            }
            catch
            {
                return CultureInfo.InvariantCulture;
            }
        }

        private static Dictionary<string, Dictionary<string, string[]>> Nouns
        {
            get
            {
                if (_nouns == null)
                {
                    lock (Sync)
                    {
                        if (_nouns == null)
                        {
                            _nouns = LoadNouns();
                        }
                    }
                }
                return _nouns;
            }
        }

        private static Dictionary<string, Dictionary<string, string[]>> LoadNouns()
        {
            var result = new Dictionary<string, Dictionary<string, string[]>>(StringComparer.Ordinal);
            try
            {
                Assembly assembly = typeof(Morphology).Assembly;
                using (Stream stream = assembly.GetManifestResourceStream("UkrainianLocalization.nouns.tsv"))
                {
                    if (stream == null)
                    {
                        return result;
                    }
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (line.Length == 0)
                            {
                                continue;
                            }
                            string[] parts = line.Split('\t');
                            if (parts.Length != 14)
                            {
                                continue;
                            }
                            string word = Normalize(parts[0]);
                            if (!result.TryGetValue(word, out var byGender))
                            {
                                byGender = new Dictionary<string, string[]>(StringComparer.Ordinal);
                                result[word] = byGender;
                            }
                            string[] forms = new string[12];
                            for (int i = 0; i < 12; i++)
                            {
                                forms[i] = Normalize(parts[i + 2]);
                            }
                            byGender[parts[1]] = forms;
                        }
                    }
                }
            }
            catch
            {
            }
            return result;
        }

        public static string Normalize(string s)
        {
            return s.Replace('\'', '’').Replace('ʼ', '’').ToLower(Culture);
        }

        public static string ToLower(string s) => s.ToLower(Culture);

        /// <summary>Declines a noun given in nominative singular (or nominative plural for plural-only nouns).</summary>
        public static string DeclineNoun(string word, Gender gender, Case c, bool plural)
        {
            if (string.IsNullOrEmpty(word))
            {
                return word;
            }
            string lower = Normalize(word);
            string result = LookupNoun(lower, gender, c, plural);
            if (result == null)
            {
                int dash = lower.LastIndexOf('-');
                if (dash > 0 && dash < lower.Length - 1)
                {
                    string head = lower.Substring(0, dash);
                    string tail = lower.Substring(dash + 1);
                    string declinedTail = LookupNoun(tail, gender, c, plural) ?? RuleNoun(tail, gender, c, plural);
                    // appositive compounds of two common nouns ("шолом-наносник") decline both parts;
                    // compound proper names ("Пен-Каннок") decline only the last part
                    string declinedHead = head.IndexOf('-') < 0 ? LookupNounAnyGender(head, gender, c, plural) : null;
                    result = (declinedHead ?? head) + "-" + declinedTail;
                }
                else
                {
                    result = RuleNoun(lower, gender, c, plural);
                }
            }
            return ApplyCase(word, result);
        }

        private static string LookupNounAnyGender(string lower, Gender gender, Case c, bool plural)
        {
            if (!Nouns.TryGetValue(lower, out var byGender) || byGender.Count == 0)
            {
                return null;
            }
            bool animate = GenderInfo.IsAnimate(gender);
            foreach (var kv in byGender)
            {
                Gender own = Gender.None;
                switch (kv.Key)
                {
                    case "MA": own = Gender.MasculineAnimate; break;
                    case "MI": own = Gender.MasculineInanimate; break;
                    case "FA": own = Gender.FeminineAnimate; break;
                    case "FI": own = Gender.FeminineInanimate; break;
                    case "NA": own = Gender.NeuterAnimate; break;
                    case "NI": own = Gender.NeuterInanimate; break;
                    case "PA": own = Gender.PluralAnimate; break;
                    case "PI": own = Gender.PluralInanimate; break;
                }
                if (GenderInfo.IsAnimate(own) == animate)
                {
                    return LookupNoun(lower, own, c, plural);
                }
            }
            return null;
        }

        private static string LookupNoun(string lower, Gender gender, Case c, bool plural)
        {
            if (!Nouns.TryGetValue(lower, out var byGender))
            {
                return null;
            }
            string[] forms;
            if (!byGender.TryGetValue(GenderInfo.Code(gender), out forms))
            {
                forms = null;
                foreach (var kv in byGender)
                {
                    forms = kv.Value;
                    break;
                }
                if (forms == null)
                {
                    return null;
                }
            }
            if (!plural)
            {
                switch (c)
                {
                    case Case.Nominative: return lower;
                    case Case.Genitive: return forms[0];
                    case Case.Dative: return forms[1];
                    case Case.Accusative: return forms[2];
                    case Case.Instrumental: return forms[3];
                    case Case.Locative: return forms[4];
                    case Case.Vocative: return forms[5];
                }
            }
            else
            {
                switch (c)
                {
                    case Case.Nominative: return forms[6];
                    case Case.Genitive: return forms[7];
                    case Case.Dative: return forms[8];
                    case Case.Accusative: return forms[9];
                    case Case.Instrumental: return forms[10];
                    case Case.Locative: return forms[11];
                    case Case.Vocative: return forms[6];
                }
            }
            return lower;
        }

        private static bool EndsWithAny(string s, string chars)
        {
            return s.Length > 0 && chars.IndexOf(s[s.Length - 1]) >= 0;
        }

        private static char Last(string s, int fromEnd = 0)
        {
            int i = s.Length - 1 - fromEnd;
            return i >= 0 ? s[i] : '\0';
        }

        private static string Alternate(string stem)
        {
            // к/г/х -> ц/з/с before -і in dative/locative of a-declension
            char c = Last(stem);
            switch (c)
            {
                case 'к': return stem.Substring(0, stem.Length - 1) + "ц";
                case 'г': return stem.Substring(0, stem.Length - 1) + "з";
                case 'х': return stem.Substring(0, stem.Length - 1) + "с";
                default: return stem;
            }
        }

        private static string RuleNoun(string w, Gender g, Case c, bool plural)
        {
            if (c == Case.Nominative && !plural)
            {
                return w;
            }
            bool animate = GenderInfo.IsAnimate(g);
            char last = Last(w);

            if (GenderInfo.IsPluralOnly(g))
            {
                return RulePluralOnly(w, animate, c);
            }

            // indeclinable foreign words
            if (last == 'і' || last == 'у' || last == 'ю' || last == 'ї' || (last == 'е' && !GenderInfo.IsNeuter(g)) || (last == 'о' && GenderInfo.IsNeuter(g) && w.Length > 2 && Vowels.IndexOf(Last(w, 1)) >= 0))
            {
                return w;
            }

            // feminine animate names ending in a consonant are indeclinable
            if (g == Gender.FeminineAnimate && Vowels.IndexOf(last) < 0 && last != 'ь')
            {
                return w;
            }

            if (last == 'а' || last == 'я')
            {
                return RuleADeclension(w, animate, c, plural);
            }

            if (GenderInfo.IsNeuter(g) && (last == 'о' || last == 'е' || last == 'є'))
            {
                return RuleNeuter(w, c, plural);
            }

            if (GenderInfo.IsFeminine(g))
            {
                return RuleFeminineConsonant(w, c, plural);
            }

            return RuleMasculine(w, animate, c, plural);
        }

        private static string RuleADeclension(string w, bool animate, Case c, bool plural)
        {
            string stem = w.Substring(0, w.Length - 1);
            bool soft = Last(w) == 'я';
            bool iaEnding = soft && (Last(stem) == 'і' || Vowels.IndexOf(Last(stem)) >= 0 || Last(stem) == '’');
            bool sibilant = EndsWithAny(stem, Sibilants);
            if (!plural)
            {
                if (soft)
                {
                    if (iaEnding)
                    {
                        string b = stem;
                        switch (c)
                        {
                            case Case.Genitive:
                            case Case.Dative:
                            case Case.Locative: return b + "ї";
                            case Case.Accusative: return b + "ю";
                            case Case.Instrumental: return b + "єю";
                            case Case.Vocative: return b + "є";
                        }
                    }
                    switch (c)
                    {
                        case Case.Genitive:
                        case Case.Dative:
                        case Case.Locative: return stem + "і";
                        case Case.Accusative: return stem + "ю";
                        case Case.Instrumental: return stem + "ею";
                        case Case.Vocative: return stem + "е";
                    }
                }
                switch (c)
                {
                    case Case.Genitive: return stem + (sibilant ? "і" : "и");
                    case Case.Dative:
                    case Case.Locative: return Alternate(stem) + "і";
                    case Case.Accusative: return stem + "у";
                    case Case.Instrumental: return stem + (sibilant ? "ею" : "ою");
                    case Case.Vocative: return stem + (sibilant ? "е" : "о");
                }
                return w;
            }
            string nPl = soft ? (iaEnding ? stem + "ї" : stem + "і") : stem + (sibilant ? "і" : "и");
            string gPl = soft ? (iaEnding ? stem + "й" : stem + "ь") : stem;
            switch (c)
            {
                case Case.Nominative:
                case Case.Vocative: return nPl;
                case Case.Genitive: return gPl;
                case Case.Dative: return stem + (soft ? "ям" : "ам");
                case Case.Accusative: return animate ? gPl : nPl;
                case Case.Instrumental: return stem + (soft ? "ями" : "ами");
                case Case.Locative: return stem + (soft ? "ях" : "ах");
            }
            return w;
        }

        private static string RuleNeuter(string w, Case c, bool plural)
        {
            string stem = w.Substring(0, w.Length - 1);
            bool soft = Last(w) == 'е' || Last(w) == 'є';
            if (!plural)
            {
                switch (c)
                {
                    case Case.Genitive: return stem + (soft ? "я" : "а");
                    case Case.Dative: return stem + (soft ? "ю" : "у");
                    case Case.Accusative:
                    case Case.Vocative: return w;
                    case Case.Instrumental: return stem + (soft ? "ем" : "ом");
                    case Case.Locative: return (soft ? stem : Alternate(stem)) + "і";
                }
                return w;
            }
            switch (c)
            {
                case Case.Nominative:
                case Case.Accusative:
                case Case.Vocative: return stem + (soft ? "я" : "а");
                case Case.Genitive: return stem;
                case Case.Dative: return stem + (soft ? "ям" : "ам");
                case Case.Instrumental: return stem + (soft ? "ями" : "ами");
                case Case.Locative: return stem + (soft ? "ях" : "ах");
            }
            return w;
        }

        private static string RuleFeminineConsonant(string w, Case c, bool plural)
        {
            string stem = Last(w) == 'ь' ? w.Substring(0, w.Length - 1) : w;
            if (!plural)
            {
                switch (c)
                {
                    case Case.Genitive:
                    case Case.Dative:
                    case Case.Locative: return stem + "і";
                    case Case.Accusative: return w;
                    case Case.Instrumental: return stem + Last(stem) + "ю";
                    case Case.Vocative: return stem + "е";
                }
                return w;
            }
            switch (c)
            {
                case Case.Nominative:
                case Case.Accusative:
                case Case.Vocative: return stem + "і";
                case Case.Genitive: return stem + "ей";
                case Case.Dative: return stem + "ям";
                case Case.Instrumental: return stem + "ями";
                case Case.Locative: return stem + "ях";
            }
            return w;
        }

        private static string RuleMasculine(string w, bool animate, Case c, bool plural)
        {
            char last = Last(w);
            string stem;
            bool soft = false;
            bool mixed = false;
            if (last == 'й')
            {
                stem = w.Substring(0, w.Length - 1);
                soft = true;
            }
            else if (last == 'ь')
            {
                stem = w.Substring(0, w.Length - 1);
                soft = true;
            }
            else if (last == 'о')
            {
                stem = w.Substring(0, w.Length - 1);
            }
            else
            {
                stem = w;
                mixed = Sibilants.IndexOf(last) >= 0;
            }
            bool velar = !soft && !mixed && (Last(stem) == 'к' || Last(stem) == 'г' || Last(stem) == 'х');
            bool jStem = last == 'й';
            if (!plural)
            {
                switch (c)
                {
                    case Case.Genitive:
                        return stem + (soft ? "я" : "а");
                    case Case.Dative:
                        if (animate)
                        {
                            return stem + (soft ? (jStem ? "єві" : "еві") : (mixed ? "еві" : "ові"));
                        }
                        return stem + (soft ? "ю" : "у");
                    case Case.Accusative:
                        return animate ? stem + (soft ? "я" : "а") : w;
                    case Case.Instrumental:
                        return stem + (soft ? (jStem ? "єм" : "ем") : (mixed ? "ем" : "ом"));
                    case Case.Locative:
                        if (animate)
                        {
                            return stem + (soft ? (jStem ? "єві" : "еві") : (mixed ? "еві" : "ові"));
                        }
                        if (velar)
                        {
                            return stem + "у";
                        }
                        return stem + (soft ? (jStem ? "ї" : "і") : "і");
                    case Case.Vocative:
                        if (soft)
                        {
                            return stem + "ю";
                        }
                        if (velar || mixed)
                        {
                            return stem + "у";
                        }
                        return stem + "е";
                }
                return w;
            }
            string nPl = stem + (soft ? (jStem ? "ї" : "і") : (mixed ? "і" : "и"));
            string gPl = stem + (soft ? (jStem ? "їв" : "ів") : "ів");
            switch (c)
            {
                case Case.Nominative:
                case Case.Vocative: return nPl;
                case Case.Genitive: return gPl;
                case Case.Dative: return stem + (soft ? "ям" : "ам");
                case Case.Accusative: return animate ? gPl : nPl;
                case Case.Instrumental: return stem + (soft ? "ями" : "ами");
                case Case.Locative: return stem + (soft ? "ях" : "ах");
            }
            return w;
        }

        private static string RulePluralOnly(string w, bool animate, Case c)
        {
            char last = Last(w);
            if (last != 'и' && last != 'і' && last != 'ї')
            {
                return w;
            }
            string stem = w.Substring(0, w.Length - 1);
            bool soft = last == 'ї' || (last == 'і' && !EndsWithAny(stem, Sibilants) && Last(stem) != 'р');
            string gen = stem + (last == 'ї' ? "їв" : "ів");
            switch (c)
            {
                case Case.Nominative:
                case Case.Vocative: return w;
                case Case.Genitive: return gen;
                case Case.Dative: return stem + (soft ? "ям" : "ам");
                case Case.Accusative: return animate ? gen : w;
                case Case.Instrumental: return stem + (soft ? "ями" : "ами");
                case Case.Locative: return stem + (soft ? "ях" : "ах");
            }
            return w;
        }

        /// <summary>Declines an adjective (or participle/ordinal) given in its nominative form.
        /// The nominative ending encodes gender and number; nounGender supplies animacy.</summary>
        public static string DeclineAdjective(string word, Gender nounGender, Case c, bool plural)
        {
            if (string.IsNullOrEmpty(word))
            {
                return word;
            }
            string w = Normalize(word);
            string result = RuleAdjective(w, nounGender, c, plural);
            return ApplyCase(word, result);
        }

        private static string RuleAdjective(string w, Gender g, Case c, bool plural)
        {
            bool animate = GenderInfo.IsAnimate(g);
            string stem;
            char form; // m, f, n, p
            bool soft;
            if (w.EndsWith("ий"))
            {
                stem = w.Substring(0, w.Length - 2); form = 'm'; soft = false;
            }
            else if (w.EndsWith("ій") || w.EndsWith("їй"))
            {
                stem = w.Substring(0, w.Length - 2); form = 'm'; soft = true;
            }
            else if (w.EndsWith("а"))
            {
                stem = w.Substring(0, w.Length - 1); form = 'f'; soft = false;
            }
            else if (w.EndsWith("я"))
            {
                stem = w.Substring(0, w.Length - 1); form = 'f'; soft = true;
            }
            else if (w.EndsWith("е"))
            {
                stem = w.Substring(0, w.Length - 1); form = 'n'; soft = false;
            }
            else if (w.EndsWith("є"))
            {
                stem = w.Substring(0, w.Length - 1); form = 'n'; soft = true;
            }
            else if (w.EndsWith("і") || w.EndsWith("ї"))
            {
                stem = w.Substring(0, w.Length - 1); form = 'p';
                soft = SoftAdjectiveStems.Contains(stem) || w.EndsWith("ї");
            }
            else
            {
                return w;
            }

            if (plural)
            {
                form = 'p';
            }
            else if (GenderInfo.IsPluralOnly(g))
            {
                form = 'p';
            }

            // soft stems: "ь" before o-endings unless the stem ends with a vowel (then "й")
            string sj = soft ? (stem.Length > 0 && Vowels.IndexOf(Last(stem)) >= 0 ? "й" : "ь") : "";
            switch (form)
            {
                case 'm':
                case 'n':
                    switch (c)
                    {
                        case Case.Nominative:
                        case Case.Vocative:
                            return form == 'm' ? stem + (soft ? "ій" : "ий") : stem + (soft ? "є" : "е");
                        case Case.Genitive: return stem + sj + "ого";
                        case Case.Dative: return stem + sj + "ому";
                        case Case.Accusative:
                            if (form == 'm')
                            {
                                return animate ? stem + sj + "ого" : stem + (soft ? "ій" : "ий");
                            }
                            return stem + (soft ? "є" : "е");
                        case Case.Instrumental: return stem + (soft ? "ім" : "им");
                        case Case.Locative: return stem + sj + "ому";
                    }
                    break;
                case 'f':
                    switch (c)
                    {
                        case Case.Nominative:
                        case Case.Vocative: return stem + (soft ? "я" : "а");
                        case Case.Genitive: return stem + sj + (soft ? "ої" : "ої");
                        case Case.Dative:
                        case Case.Locative: return stem + "ій";
                        case Case.Accusative: return stem + (soft ? "ю" : "у");
                        case Case.Instrumental: return stem + sj + "ою";
                    }
                    break;
                case 'p':
                    switch (c)
                    {
                        case Case.Nominative:
                        case Case.Vocative: return stem + "і";
                        case Case.Genitive:
                        case Case.Locative: return stem + (soft ? "іх" : "их");
                        case Case.Dative: return stem + (soft ? "ім" : "им");
                        case Case.Accusative: return animate ? stem + (soft ? "іх" : "их") : stem + "і";
                        case Case.Instrumental: return stem + (soft ? "іми" : "ими");
                    }
                    break;
            }
            return w;
        }

        /// <summary>Copies the letter case of the original word (per hyphen-separated segment) onto the declined form.</summary>
        public static string ApplyCase(string original, string declined)
        {
            if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(declined))
            {
                return declined;
            }
            bool allUpper = original.Length > 1;
            bool anyLetter = false;
            foreach (char ch in original)
            {
                if (char.IsLetter(ch))
                {
                    anyLetter = true;
                    if (!char.IsUpper(ch))
                    {
                        allUpper = false;
                        break;
                    }
                }
            }
            if (allUpper && anyLetter)
            {
                return declined.ToUpper(Culture);
            }
            string[] origParts = original.Split('-');
            string[] declParts = declined.Split('-');
            var sb = new StringBuilder(declined.Length);
            for (int i = 0; i < declParts.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append('-');
                }
                string d = declParts[i];
                string o = i < origParts.Length ? origParts[i] : origParts[origParts.Length - 1];
                if (d.Length > 0 && o.Length > 0 && char.IsUpper(o[0]))
                {
                    sb.Append(char.ToUpper(d[0], Culture));
                    sb.Append(d, 1, d.Length - 1);
                }
                else
                {
                    sb.Append(d);
                }
            }
            return sb.ToString();
        }

        /// <summary>Ukrainian numeral agreement category: 1 = "один день", 2 = "два дні", 5 = "п'ять днів".</summary>
        public static int NumberCategory(long n, bool fractional)
        {
            if (fractional)
            {
                return 2;
            }
            n = Math.Abs(n);
            long mod10 = n % 10;
            long mod100 = n % 100;
            if (mod10 == 1 && mod100 != 11)
            {
                return 1;
            }
            if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14))
            {
                return 2;
            }
            return 5;
        }
    }
}
