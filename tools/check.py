#!/usr/bin/env python3
"""Перевіряє переклад у translation/ на помилки розмітки, які зламають рядок у грі.

Використання:
    python3 tools/check.py [--warnings]

Помилки (ERROR) мають бути виправлені; попередження (WARN, показуються з --warnings) — підказки редакторові.
Профіль розмітки оригіналу (назви змінних, теги анімації й HTML) — у data/markup.json.
Код виходу 1, якщо є хоч одна помилка.
"""
import collections
import glob
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

ALLOWED_MARKERS = {'MA', 'MI', 'FA', 'FI', 'NA', 'NI', 'PA', 'PI', 'aj', 'nn',
                   'n', 'g', 'd', 'a', 'i', 'l', 'v', 'p', 'gp', 'dp', 'ap', 'ip', 'lp',
                   'c', 'ca', 'N1', 'N2', 'N5', 'Nend', '%', '^', '_', 'link'}
GENDER = {'MA', 'MI', 'FA', 'FI', 'NA', 'NI', 'PA', 'PI'}
CHAR_VARS = {'PLAYER', 'SPEAKER', 'LISTENER', 'CHILD'}
GENDER_FUNCS = {'adj', 'adjs', 'past', 'pron', 'prong', 'prond', 'pronp', 'poss', 'this', 'isfem', 'isadj'}
UK_FUNCS = GENDER_FUNCS | {'PLURAL', 'ABS', 'MAX', 'POW2', 'FIB', 'ADDITION'}

# Свідомі відступи від розмітки оригіналу.
EXCEPTIONS = {
    # Оригінал підставляє {RANK} і {PRONOUN} англійськими словами («lord», «him»); українською рід виражено через
    # стать гравця, тож ці змінні не потрібні.
    '8zFwYZeQ': {'missing': {'PRONOUN', 'RANK'}},
    # В оригіналі змінна записана з пробілом — «{ OTHER_CLAN_NOMINEE.LINK}»; у перекладі — правильно, без пробілу.
    'TjAQbTab': {'extra': {'OTHER_CLAN_NOMINEE.LINK'}},
}

TOKEN_RE = re.compile(r'\{[^{}]*\}')
VAR_RE = re.compile(r'\{([A-Z][A-Z0-9_]*(?:\.[A-Za-z0-9_]+)*)\}')
FUNC_RE = re.compile(r'\{([A-Za-z_][A-Za-z0-9_]*)\(([^{}]*)\)\}')
ANIM_RE = re.compile(r'\[(?:ib|if|rf|rb|ic|lb|bb|sb|cf|ef|mf):[^\]]*\]')
HTML_RE = re.compile(r'<[^>]+>')
MARK_RE = re.compile(r'\{\.([^{}]*)\}')
CYR = re.compile(r'[А-Яа-яІіЇїЄєҐґ]')
LAT = re.compile(r'[A-Za-z]')


def strip_markup(s):
    return TOKEN_RE.sub(' ', ANIM_RE.sub(' ', HTML_RE.sub(' ', s)))


def cond_vars(s):
    out = set()
    for m in re.finditer(r'\{:?\?([^{}]+)\}', s):
        out.update(re.findall(r'[A-Z][A-Z0-9_]*(?:\.[A-Za-z0-9_]+)?', m.group(1)))
    return out


def check_braces(s):
    depth = 0
    for ch in s:
        if ch == '{':
            depth += 1
            if depth > 1:
                return ['вкладені фігурні дужки']
        elif ch == '}':
            depth -= 1
            if depth < 0:
                return ['зайва `}`']
    return ['незакрита `{`'] if depth else []


def check_conditions(s):
    errs, stack = [], []
    for m in re.finditer(r'\{\?\}|\{\\\?\}|\{:\}|\{:\?[^{}]*\}|\{\?[^{}]*\}|\{@[^{}]*\}|\{\\@\}|\{#\}|\{\\#\}|\{#[^{}]*\}', s):
        t = m.group(0)
        top = stack[-1][0] if stack else None
        if t == '{?}':
            if top != '?':
                errs.append('`{?}` поза умовою')
            elif stack[-1][1]:
                errs.append('подвійний `{?}` в одній умові')
            else:
                stack[-1][1] = True
        elif t == '{\\?}':
            if top != '?':
                errs.append('зайвий `{\\?}`')
            else:
                stack.pop()
        elif t == '{:}' or t.startswith('{:?'):
            if top != '?':
                errs.append('`{:…}` поза умовою')
        elif t.startswith('{?'):
            stack.append(['?', False])
        elif t == '{\\@}':
            if top != '@':
                errs.append('зайвий `{\\@}`')
            else:
                stack.pop()
        elif t.startswith('{@'):
            stack.append(['@', False])
        elif t == '{\\#}':
            if top != '#':
                errs.append('зайвий `{\\#}`')
            else:
                stack.pop()
        elif t == '{#}':
            if top != '#':
                errs.append('`{#}` поза вибором')
        elif t.startswith('{#'):
            stack.append(['#', False])
    if stack:
        errs.append('незакриті умови/поля: ' + ''.join(x[0] for x in stack))
    return errs


def check_one(sid, uk, prof):
    errs, warns = [], []
    exc = EXCEPTIONS.get(sid, {})
    if not uk.strip():
        return ['порожній переклад'], []
    errs += check_braces(uk)
    errs += check_conditions(uk)

    en_vars = set(prof.get('vars', []))
    en_roots = set(prof.get('roots', [])) or {v.split('.')[0] for v in en_vars}
    uk_vars = set(VAR_RE.findall(uk))
    uk_args = {x.strip() for _, a in FUNC_RE.findall(uk) for x in a.split(',')}
    missing = {v for v in en_vars - uk_vars if v not in uk_args} - exc.get('missing', set())
    if missing:
        errs.append('бракує змінних: ' + ', '.join(sorted(missing)))
    extra = {v for v in uk_vars - en_vars if v.split('.')[0] not in en_roots} - exc.get('extra', set())
    if extra:
        errs.append('нові змінні, яких немає в оригіналі: ' + ', '.join(sorted(extra)))
    ctx_ok = {'FEMALE', 'IMPERIAL', 'COASTAL', 'NORTHERN'} if prof.get('firstname') else set()
    for v in cond_vars(uk):
        root = v.split('.')[0]
        if root not in en_roots and root not in CHAR_VARS and root not in ctx_ok:
            errs.append(f'умова на невідому змінну {v}')

    for f, a in FUNC_RE.findall(uk):
        if f not in UK_FUNCS:
            errs.append(f'невідома функція {f}()')
        if f in GENDER_FUNCS and ('.' in a or a.strip() not in en_roots):
            errs.append(f'функція {f}({a}): аргумент має бути простою змінною з оригіналу')
    for a in prof.get('plural', []):
        if 'PLURAL(' not in uk and a not in uk:
            errs.append('втрачено PLURAL(…)')

    if collections.Counter(prof.get('anim', [])) != collections.Counter(ANIM_RE.findall(uk)):
        errs.append('теги анімації не збігаються з оригіналом')
    if collections.Counter(prof.get('html', [])) != collections.Counter(HTML_RE.findall(uk)):
        errs.append('HTML-теги не збігаються з оригіналом')

    marks = MARK_RE.findall(uk)
    for mk in marks:
        if mk not in ALLOWED_MARKERS:
            errs.append(f'недозволений маркер {{.{mk}}}')
    for m in re.finditer(r'\{\.(n|g|d|a|i|l|v|p|gp|dp|ap|ip|lp|c|ca)\}', uk):
        before = uk[:m.start()]
        if not (before.endswith('}') or before.endswith(')')):
            errs.append(f'токен відмінка {m.group(0)} не одразу після змінної')
    seq = [mk for mk in marks if mk in ('N1', 'N2', 'N5', 'Nend')]
    for i in range(0, len(seq), 4):
        if seq[i:i + 4] != ['N1', 'N2', 'N5', 'Nend']:
            errs.append('числові форми мають іти як {.N1}…{.N2}…{.N5}…{.Nend}')
            break
    if re.search(r'\{\.(o|s|A)\}', uk):
        errs.append('англійські токени {.o}/{.s}/{.A}')

    plain = strip_markup(uk)
    if re.search(r'[ЁёЪъЫыЭэ]', plain):
        errs.append('російські літери (ы/э/ъ/ё)')
    for w in re.findall(r'[A-Za-zА-Яа-яІіЇїЄєҐґ’\']+', plain):
        if CYR.search(w) and LAT.search(w):
            errs.append(f'змішані латиниця й кирилиця в слові «{w}»')

    if any(mk in GENDER for mk in marks):
        words = [w for w in strip_markup(re.sub(r'\{@.*?\{\\@\}', '', uk)).split() if CYR.search(w)]
        if len(words) >= 2 and not re.search(r'\{\.(nn|aj)\}', uk):
            warns.append('багатослівна назва з маркером роду без {.nn}/{.aj}')
    if '"' in plain:
        warns.append('прямі лапки " (треба «»)')
    if re.search(r"[А-Яа-яІіЇїЄєҐґ]['`][А-Яа-яІіЇїЄєҐґ]", plain):
        warns.append("апостроф ' або ` (треба ’)")
    if re.search(r'(^|\s)-(\s|$)', plain):
        warns.append('дефіс замість тире —')
    return errs, warns


def main():
    show_warn = '--warnings' in sys.argv
    with open(os.path.join(ROOT, 'data', 'markup.json'), encoding='utf-8') as fh:
        profiles = json.load(fh)
    nerr = nwarn = n = 0
    texts = {}
    for path in sorted(glob.glob(os.path.join(ROOT, 'translation', '*', '*.jsonl'))):
        rel = os.path.relpath(path, ROOT)
        seen = set()
        with open(path, encoding='utf-8') as fh:
            for ln, line in enumerate(fh, 1):
                if not line.strip():
                    continue
                try:
                    o = json.loads(line)
                    sid, uk = o['id'], o['uk']
                except (ValueError, KeyError) as e:
                    print(f'{rel}:{ln}: ERROR: не JSON-рядок {{"id", "uk"}}: {e}')
                    nerr += 1
                    continue
                n += 1
                if sid in seen:
                    print(f'{rel}:{ln} [{sid}] ERROR: повтор id у файлі')
                    nerr += 1
                seen.add(sid)
                if sid in texts and texts[sid][0] != uk:
                    print(f'{rel}:{ln} [{sid}] ERROR: інший текст, ніж у {texts[sid][1]}')
                    nerr += 1
                texts.setdefault(sid, (uk, rel))
                errs, warns = check_one(sid, uk, profiles.get(sid, {}))
                for e in errs:
                    print(f'{rel}:{ln} [{sid}] ERROR: {e}')
                if show_warn:
                    for w in warns:
                        print(f'{rel}:{ln} [{sid}] WARN: {w}')
                nerr += len(errs)
                nwarn += len(warns)
    print(f'Перевірено рядків: {n}; помилок: {nerr}; попереджень: {nwarn}')
    sys.exit(1 if nerr or not n else 0)


if __name__ == '__main__':
    main()
