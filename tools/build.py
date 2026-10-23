#!/usr/bin/env python3
"""Збирає мод «Українська локалізація» з перекладу в translation/.

Результат – dist/Modules/UkrainianLocalization (одиночна гра) і dist/Modules/UkrainianLocalizationMP (мережева гра),
а з ключем --zip ще й архів dist/UkrainianLocalization-<версія>.zip, у корені якого тека Modules.

Використання:
    python3 tools/build.py [--dll ШЛЯХ] [--zip]

Бібліотеку граматичного процесора спершу збирають: dotnet build src/UkrainianLocalization -c Release
"""
import argparse
import glob
import json
import os
import shutil
import sys
import zipfile
from collections import OrderedDict
from xml.sax.saxutils import escape

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIST = os.path.join(ROOT, 'dist')
MODULES = os.path.join(DIST, 'Modules')
OUT = os.path.join(MODULES, 'UkrainianLocalization')
OUT_MP = os.path.join(MODULES, 'UkrainianLocalizationMP')
DEFAULT_DLL = os.path.join(ROOT, 'src', 'UkrainianLocalization', 'bin', 'Release', 'net472', 'UkrainianLocalization.dll')

LANG_ID = 'Українська'
GAME_VERSION = 'v1.4.8'
MOD_VERSION = 'v1.2.0'
PROCESSOR = ('UkrainianLocalization.UkrainianTextProcessor, UkrainianLocalization, Version=1.0.0.0, '
             'Culture=neutral, PublicKeyToken=null')


def attr(s):
    return escape(s, {'"': '&quot;'})


FUNCTIONS = OrderedDict([
    ('MAX', '{?$0>$1}{$0}{?}{$1}{\\?}'),
    ('ABS', '{?$0>0}{$0}{?}{0 - $0}{\\?}'),
    ('POW2', '{?$0==0}1{?}{2*POW2($0 - 1)}{\\?}'),
    ('FIB', '{?$0 < 2}1{?}{FIB($0 - 1)+FIB($0 - 2)}{\\?}'),
    ('ADDITION', '$0+$1'),
    ('PLURAL', '{?$0.Plural}{$0.Plural}{?}{$0}{\\?}'),
])


def gender_switch(m, f, n, p):
    # Персонажі (SetCharacterProperties) мають GENDER; імена з NameGenerator («{FIRSTNAME} Скеля» тощо) мають FIRSTNAME
    # і FEMALE, тож їхній рід – FEMALE, а не маркер прізвиська. GENDER перевіряється першим: персонаж має FIRSTNAME,
    # але не має FEMALE, і звернення до відсутнього FEMALE там спричиняє виняток. Решту назв (лорди, поселення,
    # фракції) розрізняє маркер роду.
    markers = ('{?$0!.FI}' + f + '{:?$0!.FA}' + f + '{:?$0!.NI}' + n + '{:?$0!.NA}' + n +
               '{:?$0!.PI}' + p + '{:?$0!.PA}' + p + '{:}' + m + '{\\?}')
    return ('{?$0.GENDER}{?$0.GENDER==1}' + f + '{?}' + m + '{\\?}'
            '{:?$0.FIRSTNAME}{?$0.FEMALE==1}' + f + '{?}' + m + '{\\?}'
            '{:}' + markers + '{\\?}')


FUNCTIONS['adj'] = gender_switch('ий', 'а', 'е', 'і')
FUNCTIONS['adjs'] = gender_switch('ій', 'я', 'є', 'і')
FUNCTIONS['past'] = gender_switch('в', 'ла', 'ло', 'ли')
FUNCTIONS['pron'] = gender_switch('він', 'вона', 'воно', 'вони')
FUNCTIONS['prong'] = gender_switch('його', 'її', 'його', 'їх')
FUNCTIONS['prond'] = gender_switch('йому', 'їй', 'йому', 'їм')
FUNCTIONS['pronp'] = gender_switch('нього', 'неї', 'нього', 'них')
FUNCTIONS['poss'] = gender_switch('його', 'її', 'його', 'їхній')
FUNCTIONS['this'] = gender_switch('цей', 'ця', 'це', 'ці')
FUNCTIONS['isfem'] = gender_switch('0', '1', '0', '0')
# 1, коли вставлено прикметник ({.aj} без іменника), напр. str_adjective_for_culture.*; деякі з них мають той самий id,
# що й іменник (Khuzait/Aserai), тож шаблони розгалужуються за цією функцією.
FUNCTIONS['isadj'] = '{?$0!.aj}{?$0!.nn}0{?}1{\\?}{?}0{\\?}'

XML_HEAD = ['<?xml version="1.0" encoding="utf-8"?>',
            '<base xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" '
            'xmlns:xsd="http://www.w3.org/2001/XMLSchema" type="string">',
            '  <tags>', f'    <tag language="{LANG_ID}" />', '  </tags>']


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\r\n') as fh:
        fh.write(text)


def load_translation():
    """Повертає {(модуль, файл.xml): [(id, текст), …]} у порядку гри."""
    files = {}
    texts = {}
    problems = []
    for path in sorted(glob.glob(os.path.join(ROOT, 'translation', '*', '*.jsonl'))):
        mod = os.path.basename(os.path.dirname(path))
        name = os.path.basename(path)[:-len('.jsonl')] + '.xml'
        items = []
        seen = set()
        with open(path, encoding='utf-8') as fh:
            for ln, line in enumerate(fh, 1):
                if not line.strip():
                    continue
                try:
                    o = json.loads(line)
                    sid, text = o['id'], o['uk']
                except (ValueError, KeyError) as e:
                    problems.append(f'{path}:{ln}: {e}')
                    continue
                if sid in seen:
                    problems.append(f'{path}:{ln}: повтор id {sid}')
                    continue
                seen.add(sid)
                if sid in texts and texts[sid] != text:
                    problems.append(f'{path}:{ln}: id {sid} має інший текст, ніж в іншому файлі')
                texts[sid] = text
                items.append((sid, text))
        files[(mod, name)] = items
    if problems:
        print('\n'.join(problems))
        sys.exit(1)
    if not files:
        print('Не знайдено перекладу в translation/')
        sys.exit(1)
    return files


def submodule_xml(mod_id, name, category, deps):
    dep_xml = '\n'.join(f'\t\t<DependedModule Id="{d}" DependentVersion="{GAME_VERSION}" Optional="true"/>' for d in deps)
    return f'''<?xml version="1.0" encoding="utf-8"?>
<Module>
\t<Name value="{name}"/>
\t<Id value="{mod_id}"/>
\t<Version value="{MOD_VERSION}"/>
\t<DefaultModule value="false"/>
\t<ModuleCategory value="{category}"/>
\t<DependedModules>
{dep_xml}
\t</DependedModules>
\t<SubModules>
\t\t<SubModule>
\t\t\t<Name value="UkrainianLocalization"/>
\t\t\t<DLLName value="UkrainianLocalization.dll"/>
\t\t\t<SubModuleClassType value="UkrainianLocalization.UkrainianSubModule"/>
\t\t\t<Tags/>
\t\t</SubModule>
\t</SubModules>
</Module>
'''


def build(dll):
    if not os.path.isfile(dll):
        print(f'Немає бібліотеки {dll}\nСпершу: dotnet build src/UkrainianLocalization -c Release')
        sys.exit(1)
    files = load_translation()
    for d in (OUT, OUT_MP):
        if os.path.isdir(d):
            shutil.rmtree(d)

    lang_files = []
    total = set()
    for (mod, name), items in sorted(files.items()):
        rel = f'UK/{mod}/{name}'
        lines = XML_HEAD + ['  <strings>']
        for sid, text in items:
            lines.append(f'    <string id="{attr(sid)}" text="{attr(text)}" />')
            total.add(sid)
        lines += ['  </strings>', '</base>', '']
        write(os.path.join(OUT, 'ModuleData', 'Languages', *rel.split('/')), '\n'.join(lines))
        lang_files.append(rel)

    func_lines = XML_HEAD + ['  <functions>']
    for name, body in FUNCTIONS.items():
        func_lines.append(f'    <function functionName="{name}" functionBody="{attr(body)}" />')
    func_lines += ['  </functions>', '</base>', '']
    write(os.path.join(OUT, 'ModuleData', 'Languages', 'UK', 'uk_functions.xml'), '\n'.join(func_lines))

    ld = ['<?xml version="1.0" encoding="utf-8"?>',
          f'<LanguageData id="{LANG_ID}" name="{LANG_ID}" subtitle_extension="uk" supported_iso="uk,ukr,uk-ua,uk-UA" '
          f'text_processor="{PROCESSOR}" under_development="false">',
          '  <LanguageFile xml_path="UK/uk_functions.xml" />']
    ld += [f'  <LanguageFile xml_path="{rel}" />' for rel in lang_files]
    ld += ['</LanguageData>', '']
    write(os.path.join(OUT, 'ModuleData', 'Languages', 'UK', 'language_data.xml'), '\n'.join(ld))

    # Galahad (декоративний шрифт гри) не має літер і, ї, й – замінюється на Fira Sans, як і для кирилиці в грі.
    fonts = f'''<?xml version="1.0" encoding="utf-8"?>
<Languages>
  <Language id="{LANG_ID}" DefaultFont="FiraSansExtraCondensed-Regular" LineSeperatorChar="-">
    <Map From="FiraSansExtraCondensed-Light" To="FiraSansExtraCondensed-Light"/>
    <Map From="FiraSansExtraCondensed-Medium" To="FiraSansExtraCondensed-Medium"/>
    <Map From="FiraSansExtraCondensed-Regular" To="FiraSansExtraCondensed-Regular"/>
    <Map From="Galahad" To="FiraSansExtraCondensed-Regular"/>
    <Map From="Galahad_Numbers_Bold" To="Galahad_Numbers_Bold"/>
  </Language>
</Languages>
'''
    write(os.path.join(OUT, 'GUI', 'Fonts', 'UkrainianLanguages.xml'), fonts)

    write(os.path.join(OUT, 'SubModule.xml'), submodule_xml(
        'UkrainianLocalization', 'Українська локалізація', 'Singleplayer',
        ['Native', 'SandBoxCore', 'Sandbox', 'StoryMode', 'CustomBattle', 'BirthAndDeath', 'FastMode']))

    bin_dir = os.path.join(OUT, 'bin', 'Win64_Shipping_Client')
    os.makedirs(bin_dir, exist_ok=True)
    shutil.copyfile(dll, os.path.join(bin_dir, 'UkrainianLocalization.dll'))

    # Субтитри роликів; бібліотека під час запуску гри копіює їх до відео модуля SandBox.
    sub_root = os.path.join(ROOT, 'subtitles')
    for f in sorted(glob.glob(os.path.join(sub_root, '**', '*.srt'), recursive=True)):
        dst = os.path.join(OUT, 'Videos', os.path.relpath(f, sub_root))
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copyfile(f, dst)

    # Копія для вкладки мережевої гри в лаунчері: той самий переклад, без субтитрів.
    shutil.copytree(OUT, OUT_MP, ignore=shutil.ignore_patterns('Videos'))
    write(os.path.join(OUT_MP, 'SubModule.xml'), submodule_xml(
        'UkrainianLocalizationMP', 'Українська локалізація (мережева гра)', 'MultiplayerOptional',
        ['Native', 'Multiplayer']))

    print(f'Зібрано {OUT}: {len(lang_files)} мовних файлів, {len(total)} рядків')


def make_zip():
    path = os.path.join(DIST, f'UkrainianLocalization-{MOD_VERSION}.zip')
    if os.path.exists(path):
        os.remove(path)
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for base in (OUT, OUT_MP):
            for dirpath, _, filenames in sorted(os.walk(base)):
                for fn in sorted(filenames):
                    full = os.path.join(dirpath, fn)
                    z.write(full, os.path.relpath(full, DIST).replace(os.sep, '/'))
    print(f'Архів: {path}')


def main():
    ap = argparse.ArgumentParser(description='Збирає мод «Українська локалізація».')
    ap.add_argument('--dll', default=DEFAULT_DLL, help='шлях до UkrainianLocalization.dll')
    ap.add_argument('--zip', action='store_true', help='також створити архів для публікації')
    args = ap.parse_args()
    build(args.dll)
    if args.zip:
        make_zip()


if __name__ == '__main__':
    main()
