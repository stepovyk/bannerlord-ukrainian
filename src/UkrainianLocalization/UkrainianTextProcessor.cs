using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.Localization.TextProcessor;

namespace UkrainianLocalization
{
    /// <summary>
    /// Ukrainian grammar post-processor for Bannerlord texts.
    ///
    /// Names carry markers: a gender marker ({.MA} {.MI} {.FA} {.FI} {.NA} {.NI} {.PA} {.PI}) at the start and
    /// {.nn} (noun) / {.aj} (adjective) after each declinable word of a multi-word name.
    /// Templates put a case token right after an inserted name: {.n} {.g} {.d} {.a} {.i} {.l} {.v},
    /// plural {.p} {.gp} {.dp} {.ap} {.ip} {.lp}, and {.c} for agreement with a preceding number.
    /// Numeral agreement for plain words: {.N1}one{.N2}few{.N5}many{.Nend}.
    /// </summary>
    public class UkrainianTextProcessor : LanguageSpecificTextProcessor
    {
        private const string LinkEnding = "</b></a>";
        private const int MaxCache = 512;
        private const int MaxInlineGroupLength = 80;

        private sealed class Word
        {
            public int Start;
            public int Length;
            public Gender Gender;
            public bool Adjective;

            public Word Clone(int offset)
            {
                return new Word { Start = Start + offset, Length = Length, Gender = Gender, Adjective = Adjective };
            }
        }

        private sealed class Group
        {
            public int Start;
            public Gender Gender;
            public readonly List<Word> Words = new List<Word>();
            public bool Closed;
            public int LastWordEnd = -1;
        }

        private sealed class CachedGroup
        {
            public string Plain;
            public List<Word> Words;
        }

        private sealed class CallState
        {
            public StringBuilder Output;
            public readonly List<Group> Groups = new List<Group>();
            public Group Current;
        }

        [ThreadStatic]
        private static CallState _call;

        [ThreadStatic]
        private static List<CachedGroup> _cache;

        private static readonly CultureInfo Culture = CreateCulture();

        public override CultureInfo CultureInfoForLanguage => Culture;

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

        private static List<CachedGroup> Cache
        {
            get
            {
                if (_cache == null)
                {
                    _cache = new List<CachedGroup>();
                }
                return _cache;
            }
        }

        public override void ClearTemporaryData()
        {
            _call = null;
            Cache.Clear();
        }

        public override void ProcessToken(string sourceText, ref int cursorPos, string token, StringBuilder outputString)
        {
            try
            {
                CallState state = GetState(outputString);
                switch (token)
                {
                    case ".MA":
                    case ".MI":
                    case ".FA":
                    case ".FI":
                    case ".NA":
                    case ".NI":
                    case ".PA":
                    case ".PI":
                        OnGender(state, GenderInfo.FromToken(token));
                        return;
                    case ".nn":
                        OnWord(state, adjective: false);
                        return;
                    case ".aj":
                        OnWord(state, adjective: true);
                        return;
                    case ".n":
                        Decline(state, Case.Nominative, plural: false, count: false);
                        return;
                    case ".g":
                        Decline(state, Case.Genitive, false, false);
                        return;
                    case ".d":
                        Decline(state, Case.Dative, false, false);
                        return;
                    case ".a":
                        Decline(state, Case.Accusative, false, false);
                        return;
                    case ".i":
                        Decline(state, Case.Instrumental, false, false);
                        return;
                    case ".l":
                        Decline(state, Case.Locative, false, false);
                        return;
                    case ".v":
                        Decline(state, Case.Vocative, false, false);
                        return;
                    case ".p":
                        Decline(state, Case.Nominative, true, false);
                        return;
                    case ".gp":
                        Decline(state, Case.Genitive, true, false);
                        return;
                    case ".dp":
                        Decline(state, Case.Dative, true, false);
                        return;
                    case ".ap":
                        Decline(state, Case.Accusative, true, false);
                        return;
                    case ".ip":
                        Decline(state, Case.Instrumental, true, false);
                        return;
                    case ".lp":
                        Decline(state, Case.Locative, true, false);
                        return;
                    case ".c":
                        Decline(state, Case.Nominative, false, true);
                        return;
                    case ".ca":
                        Decline(state, Case.Accusative, false, true);
                        return;
                    case ".N1":
                        NumeralForms(sourceText, ref cursorPos, outputString);
                        return;
                    default:
                        // ".link", ".N2", ".N5", ".Nend" outside of a form block, and unknown tokens are dropped.
                        return;
                }
            }
            catch
            {
                // Grammar processing must never break a game text.
            }
        }

        private static CallState GetState(StringBuilder output)
        {
            CallState state = _call;
            if (state != null && ReferenceEquals(state.Output, output))
            {
                return state;
            }
            if (state != null)
            {
                FinishCall(state);
            }
            state = new CallState { Output = output };
            _call = state;
            return state;
        }

        /// <summary>Stores the groups of a finished Process() call so that later texts can decline them by their plain text.</summary>
        private static void FinishCall(CallState state)
        {
            string text = state.Output.ToString();
            foreach (Group group in state.Groups)
            {
                if (group.Closed || group.Start < 0 || group.Start >= text.Length)
                {
                    continue;
                }
                string plain = text.Substring(group.Start).TrimEnd();
                if (plain.Length == 0)
                {
                    continue;
                }
                List<Word> words = new List<Word>();
                if (group.Words.Count == 0)
                {
                    int len = FirstWordLength(plain, 0);
                    if (len <= 0)
                    {
                        continue;
                    }
                    words.Add(new Word { Start = 0, Length = len, Gender = group.Gender, Adjective = false });
                }
                else
                {
                    foreach (Word w in group.Words)
                    {
                        if (w.Start >= group.Start && w.Start + w.Length <= group.Start + plain.Length)
                        {
                            words.Add(w.Clone(-group.Start));
                        }
                    }
                    if (words.Count == 0)
                    {
                        continue;
                    }
                }
                AddToCache(plain, words);
            }
        }

        private static void AddToCache(string plain, List<Word> words)
        {
            List<CachedGroup> cache = Cache;
            for (int i = cache.Count - 1; i >= 0; i--)
            {
                if (string.Equals(cache[i].Plain, plain, StringComparison.Ordinal))
                {
                    cache.RemoveAt(i);
                }
            }
            if (cache.Count >= MaxCache)
            {
                cache.RemoveAt(0);
            }
            cache.Add(new CachedGroup { Plain = plain, Words = words });
        }

        private static bool IsWordChar(char c)
        {
            return char.IsLetter(c) || c == '’' || c == '\'' || c == 'ʼ' || c == '-';
        }

        private static int FirstWordLength(string s, int start)
        {
            int i = start;
            while (i < s.Length && IsWordChar(s[i]))
            {
                i++;
            }
            return i - start;
        }

        /// <summary>End of the visible text, i.e. before a trailing hyperlink closing tag.</summary>
        private static int ContentEnd(StringBuilder sb)
        {
            int end = sb.Length;
            if (end >= LinkEnding.Length)
            {
                bool match = true;
                for (int i = 0; i < LinkEnding.Length; i++)
                {
                    if (sb[end - LinkEnding.Length + i] != LinkEnding[i])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    end -= LinkEnding.Length;
                }
            }
            while (end > 0 && (sb[end - 1] == ' ' || sb[end - 1] == '\u00A0'))
            {
                end--;
            }
            return end;
        }

        private static void OnGender(CallState state, Gender gender)
        {
            int pos = state.Output.Length;
            Group current = state.Current;
            if (current != null && !current.Closed && current.Words.Count > 0 && IsContinuation(state.Output, current.LastWordEnd, pos))
            {
                // a gender switch inside a name (apposition / nickname of another gender)
                current.Gender = gender;
                return;
            }
            if (current != null && !current.Closed && current.Words.Count == 0 && current.Start == pos)
            {
                current.Gender = gender;
                return;
            }
            Group group = new Group { Start = pos, Gender = gender };
            state.Groups.Add(group);
            state.Current = group;
        }

        private static bool IsContinuation(StringBuilder sb, int from, int to)
        {
            if (from < 0 || to < from || to - from > 6)
            {
                return false;
            }
            string gap = sb.ToString(from, to - from).Trim();
            return gap.Length == 0 || gap == "і" || gap == "й" || gap == "та" || gap == "/" || gap == "—" || gap == "-" || gap == ",";
        }

        private static void OnWord(CallState state, bool adjective)
        {
            StringBuilder sb = state.Output;
            int end = ContentEnd(sb);
            int start = end;
            while (start > 0 && IsWordChar(sb[start - 1]))
            {
                start--;
            }
            if (start == end)
            {
                return;
            }
            Group group = state.Current;
            if (group == null || group.Closed || (group.Words.Count > 0 && start - group.LastWordEnd > MaxInlineGroupLength))
            {
                group = new Group { Start = start, Gender = Gender.None };
                state.Groups.Add(group);
                state.Current = group;
            }
            if (!adjective)
            {
                // An inserted name that already is a cached group (e.g. "{FIRSTNAME}{.nn}" or "{ITEMNAME}{.nn}") is embedded with its own words.
                CachedGroup cached = FindCachedTail(sb, end, out int cachedStart);
                if (cached != null && (cached.Words.Count > 1 || cached.Plain.IndexOf(' ') >= 0 || cachedStart < start))
                {
                    if (cachedStart < group.Start)
                    {
                        group.Start = cachedStart;
                    }
                    group.Words.RemoveAll(existing => existing.Start + existing.Length > cachedStart);
                    foreach (Word w in cached.Words)
                    {
                        group.Words.Add(w.Clone(cachedStart));
                    }
                    if (group.Gender == Gender.None)
                    {
                        group.Gender = HeadGender(cached.Words);
                    }
                    group.LastWordEnd = end;
                    return;
                }
                if (cached != null && cached.Words.Count == 1 && group.Gender == Gender.None)
                {
                    group.Gender = cached.Words[0].Gender;
                }
            }
            if (start < group.Start)
            {
                group.Start = start;
            }
            group.Words.Add(new Word { Start = start, Length = end - start, Gender = group.Gender, Adjective = adjective });
            group.LastWordEnd = end;
        }

        private static Gender HeadGender(List<Word> words)
        {
            foreach (Word w in words)
            {
                if (!w.Adjective && w.Gender != Gender.None)
                {
                    return w.Gender;
                }
            }
            foreach (Word w in words)
            {
                if (w.Gender != Gender.None)
                {
                    return w.Gender;
                }
            }
            return Gender.None;
        }

        /// <summary>Finds the longest cached name whose plain text ends exactly at <paramref name="end"/>.</summary>
        private static CachedGroup FindCachedTail(StringBuilder sb, int end, out int start)
        {
            start = -1;
            CachedGroup best = null;
            List<CachedGroup> cache = Cache;
            for (int i = cache.Count - 1; i >= 0; i--)
            {
                CachedGroup g = cache[i];
                int len = g.Plain.Length;
                if (len == 0 || len > end)
                {
                    continue;
                }
                if (best != null && len <= best.Plain.Length)
                {
                    continue;
                }
                int s = end - len;
                if (s > 0 && IsWordChar(sb[s - 1]))
                {
                    continue;
                }
                bool match = true;
                for (int k = 0; k < len; k++)
                {
                    char a = sb[s + k];
                    char b = g.Plain[k];
                    if (a != b && (k > 0 || char.ToLowerInvariant(a) != char.ToLowerInvariant(b)))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    best = g;
                    start = s;
                }
            }
            return best;
        }

        private static void Decline(CallState state, Case c, bool plural, bool count)
        {
            StringBuilder sb = state.Output;
            int end = ContentEnd(sb);
            List<Word> words = null;
            int groupStart = -1;

            CachedGroup cached = FindCachedTail(sb, end, out int cachedStart);
            Group inline = state.Current;
            bool inlineUsable = inline != null && !inline.Closed && inline.Words.Count > 0 && inline.Start < end
                                 && end - inline.Start <= MaxInlineGroupLength * 2 && inline.LastWordEnd <= end
                                 && !ContainsSentenceBreak(sb, inline.LastWordEnd, end)
                                 && !ContainsUnmarkedText(sb, inline.LastWordEnd, end);
            if (inlineUsable && (cached == null || cachedStart < inline.LastWordEnd))
            {
                groupStart = inline.Start;
                words = new List<Word>();
                foreach (Word w in inline.Words)
                {
                    words.Add(w.Clone(0));
                }
                inline.Closed = true;
            }
            else if (cached != null)
            {
                groupStart = cachedStart;
                words = new List<Word>();
                foreach (Word w in cached.Words)
                {
                    words.Add(w.Clone(cachedStart));
                }
                if (inline != null && !inline.Closed && inline.Start >= cachedStart)
                {
                    inline.Closed = true;
                }
            }
            if (words == null || words.Count == 0)
            {
                return;
            }

            if (count)
            {
                int category = NumberBefore(sb, groupStart, out bool found);
                if (!found)
                {
                    return;
                }
                if (category == 1)
                {
                    // {.c}: nominative singular (unchanged); {.ca}: accusative singular
                    if (c == Case.Nominative)
                    {
                        return;
                    }
                    plural = false;
                }
                else if (category == 2)
                {
                    c = Case.Nominative;
                    plural = true;
                }
                else
                {
                    c = Case.Genitive;
                    plural = true;
                }
            }
            if (c == Case.Nominative && !plural)
            {
                return;
            }

            Gender head = HeadGender(words);
            // replace from the last word to the first to keep offsets valid
            for (int i = words.Count - 1; i >= 0; i--)
            {
                Word w = words[i];
                if (w.Start < 0 || w.Start + w.Length > sb.Length)
                {
                    continue;
                }
                string original = sb.ToString(w.Start, w.Length);
                string declined;
                if (w.Adjective)
                {
                    Gender g = NextNounGender(words, i);
                    if (g == Gender.None)
                    {
                        g = w.Gender != Gender.None ? w.Gender : head;
                    }
                    declined = Morphology.DeclineAdjective(original, g, c, plural);
                }
                else
                {
                    Gender g = w.Gender != Gender.None ? w.Gender : head;
                    if (g == Gender.None)
                    {
                        continue;
                    }
                    declined = Morphology.DeclineNoun(original, g, c, plural);
                }
                if (!string.Equals(original, declined, StringComparison.Ordinal))
                {
                    sb.Remove(w.Start, w.Length);
                    sb.Insert(w.Start, declined);
                }
            }
            AdjustPreposition(sb, groupStart);
        }

        /// <summary>
        /// Euphony of the preposition written in the template right before an inserted name, which the template cannot
        /// know: «в Вландії» → «у Вландії», «з Сарготу» → «із Сарготу».
        /// </summary>
        private static void AdjustPreposition(StringBuilder sb, int start)
        {
            int p = start;
            while (p < sb.Length && !char.IsLetter(sb[p]))
            {
                p++;
            }
            if (p >= sb.Length || p < 2 || sb[p - 1] != ' ' || (p >= 3 && char.IsLetter(sb[p - 3])))
            {
                return;
            }
            char prep = sb[p - 2];
            char first = char.ToLowerInvariant(sb[p]);
            if ((prep == 'в' || prep == 'В') && (first == 'в' || first == 'ф'))
            {
                sb[p - 2] = prep == 'в' ? 'у' : 'У';
            }
            else if ((prep == 'з' || prep == 'З') && "зсшщжчц".IndexOf(first) >= 0)
            {
                sb[p - 2] = 'з';
                sb.Insert(p - 2, prep == 'з' ? 'і' : 'І');
            }
        }

        /// <summary>
        /// Case tokens always follow an inserted value. Letters or digits between the last marked word of the template
        /// and the token come from an inserted value without markers (a name the player typed, a number), which must not
        /// make the template's own words decline instead.
        /// </summary>
        private static bool ContainsUnmarkedText(StringBuilder sb, int from, int to)
        {
            if (from < 0)
            {
                return false;
            }
            for (int i = from; i < to && i < sb.Length; i++)
            {
                if (char.IsLetterOrDigit(sb[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsSentenceBreak(StringBuilder sb, int from, int to)
        {
            if (from < 0)
            {
                return false;
            }
            for (int i = from; i < to && i < sb.Length; i++)
            {
                char ch = sb[i];
                if (ch == '.' || ch == '!' || ch == '?' || ch == ';' || ch == ':' || ch == '\n')
                {
                    return true;
                }
            }
            return false;
        }

        private static Gender NextNounGender(List<Word> words, int index)
        {
            for (int j = index + 1; j < words.Count; j++)
            {
                if (!words[j].Adjective)
                {
                    return words[j].Gender;
                }
            }
            return Gender.None;
        }

        /// <summary>Category (1/2/5) of the number written right before position <paramref name="pos"/>.</summary>
        private static int NumberBefore(StringBuilder sb, int pos, out bool found)
        {
            int i = pos - 1;
            for (int skippedWords = 0; ; skippedWords++)
            {
                while (i >= 0 && (sb[i] == ' ' || sb[i] == '\u00A0' || sb[i] == '\u202F'))
                {
                    i--;
                }
                if (i < 0 || !IsWordChar(sb[i]) || skippedWords >= 2)
                {
                    break;
                }
                while (i >= 0 && IsWordChar(sb[i]))
                {
                    i--;
                }
            }
            return ParseNumberEndingAt(sb.ToString(0, Math.Max(0, i + 1)), out found);
        }

        private static int ParseNumberEndingAt(string text, out bool found)
        {
            found = false;
            int i = text.Length - 1;
            // allow a closing tag right after the number (e.g. "<b>5</b>")
            while (i >= 0 && text[i] == '>')
            {
                int lt = text.LastIndexOf('<', i);
                if (lt < 0)
                {
                    break;
                }
                i = lt - 1;
            }
            if (i < 0 || !char.IsDigit(text[i]))
            {
                return 5;
            }
            int endDigit = i;
            while (i >= 0 && (char.IsDigit(text[i]) || ((text[i] == ',' || text[i] == '.' || text[i] == ' ' || text[i] == ' ' || text[i] == ' ') && i > 0 && char.IsDigit(text[i - 1]))))
            {
                i--;
            }
            string number = text.Substring(i + 1, endDigit - i);
            found = true;
            return Categorize(number);
        }

        private static int Categorize(string number)
        {
            bool fractional = false;
            int lastSep = number.LastIndexOfAny(new[] { ',', '.' });
            string integral = number;
            if (lastSep >= 0)
            {
                int digitsAfter = number.Length - lastSep - 1;
                if (digitsAfter != 3)
                {
                    fractional = true;
                    integral = number.Substring(0, lastSep);
                }
            }
            var digits = new StringBuilder();
            foreach (char ch in integral)
            {
                if (char.IsDigit(ch))
                {
                    digits.Append(ch);
                }
            }
            string d = digits.ToString();
            if (d.Length == 0)
            {
                return 5;
            }
            if (d.Length > 4)
            {
                d = d.Substring(d.Length - 4);
            }
            long n = long.Parse(d, CultureInfo.InvariantCulture);
            return Morphology.NumberCategory(n, fractional);
        }

        /// <summary>Handles {.N1}one{.N2}few{.N5}many{.Nend}: keeps the form that agrees with the nearest number.</summary>
        private void NumeralForms(string source, ref int cursorPos, StringBuilder output)
        {
            int p2 = source.IndexOf("{.N2}", cursorPos, StringComparison.Ordinal);
            int p5 = p2 >= 0 ? source.IndexOf("{.N5}", p2, StringComparison.Ordinal) : -1;
            int pe = p5 >= 0 ? source.IndexOf("{.Nend}", p5, StringComparison.Ordinal) : -1;
            if (p2 < 0 || p5 < 0 || pe < 0)
            {
                return;
            }
            string f1 = source.Substring(cursorPos, p2 - cursorPos);
            string f2 = source.Substring(p2 + 5, p5 - (p2 + 5));
            string f5 = source.Substring(p5 + 5, pe - (p5 + 5));
            cursorPos = pe + 7;

            int category = NumberNearEnd(output, out bool found);
            if (!found)
            {
                category = NumberInForm(f1, out found);
                if (!found)
                {
                    category = 5;
                }
            }
            string chosen = category == 1 ? f1 : (category == 2 ? f2 : f5);
            AppendProcessed(chosen, output);
        }

        private static int NumberNearEnd(StringBuilder sb, out bool found)
        {
            found = false;
            int limit = Math.Max(0, sb.Length - 48);
            int i = sb.Length - 1;
            while (i >= limit)
            {
                if (char.IsDigit(sb[i]))
                {
                    return ParseNumberEndingAt(sb.ToString(0, i + 1), out found);
                }
                char ch = sb[i];
                if (ch == '.' || ch == '!' || ch == '?' || ch == '\n')
                {
                    break;
                }
                i--;
            }
            return 5;
        }

        private static int NumberInForm(string form, out bool found)
        {
            found = false;
            for (int i = 0; i < form.Length; i++)
            {
                if (char.IsDigit(form[i]))
                {
                    int j = i;
                    while (j < form.Length && (char.IsDigit(form[j]) || ((form[j] == ',' || form[j] == '.' || form[j] == ' ') && j + 1 < form.Length && char.IsDigit(form[j + 1]))))
                    {
                        j++;
                    }
                    found = true;
                    return Categorize(form.Substring(i, j - i));
                }
            }
            return 5;
        }

        /// <summary>Appends a fragment, handling post-processing markers inside it the same way the base class does.</summary>
        private void AppendProcessed(string fragment, StringBuilder output)
        {
            int i = 0;
            while (i < fragment.Length)
            {
                char ch = fragment[i];
                if (ch != '{')
                {
                    output.Append(ch);
                    i++;
                    continue;
                }
                int close = fragment.IndexOf('}', i);
                if (close < 0)
                {
                    output.Append(fragment, i, fragment.Length - i);
                    break;
                }
                string token = fragment.Substring(i + 1, close - i - 1);
                i = close + 1;
                if (token.Length > 1 && token[0] == '.')
                {
                    if (token == ".^" || token == "._")
                    {
                        int next = i;
                        while (next < fragment.Length && !char.IsLetter(fragment[next]))
                        {
                            output.Append(fragment[next]);
                            next++;
                        }
                        if (next < fragment.Length)
                        {
                            output.Append(token == ".^" ? char.ToUpper(fragment[next], Culture) : char.ToLower(fragment[next], Culture));
                            next++;
                        }
                        i = next;
                    }
                    else if (token != ".%")
                    {
                        int pos = i;
                        ProcessToken(fragment, ref pos, token, output);
                        i = pos;
                    }
                }
            }
        }
    }
}
