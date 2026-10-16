"""Lint the Keyed language files.

Usage:
    python -I Tools/check_keys.py

Checks that every Languages/*/Keyed/*.xml parses, that ChineseTraditional and English define the same
keys, and that every "RimTaiName_..." string literal in Source/RimTaiName is defined. Keys built at
runtime from an enum (prefix + value) are listed in DYNAMIC and expanded from the C# enum source.
Exit code 0 = clean, 1 = problems found.
"""
import glob
import os
import re
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LANGUAGES = ('ChineseTraditional', 'English')
SOURCE = os.path.join(ROOT, 'Source', 'RimTaiName')
# prefix -> (enum name, values to skip)
DYNAMIC = {
    'RimTaiName_Gemini_': ('GeminiErrorKind', {'None', 'BadRequest', 'UnexpectedStatus', 'QuotaExceeded'}),
    'RimTaiName_Settings_KeySave': ('KeySaveResult', {'Ok'}),
    'RimTaiName_Field_': ('NameField', set()),
    'RimTaiName_Source_': ('FieldSource', set()),
}
LITERAL = re.compile(r'"(RimTaiName_[A-Za-z0-9_]+)"')


def load_keys(language):
    keys = {}
    for path in sorted(glob.glob(os.path.join(ROOT, 'Languages', language, 'Keyed', '*.xml'))):
        for element in ET.parse(path).getroot():
            if element.tag in keys:
                raise ValueError(f'{language}: duplicate key {element.tag}')
            keys[element.tag] = element.text or ''
    return keys


def enum_values(name, sources):
    match = re.search(r'enum\s+' + name + r'\s*\{(.*?)\}', sources, re.S)
    if not match:
        raise ValueError(f'enum {name} not found')
    body = re.sub(r'/\*.*?\*/', '', match.group(1), flags=re.S)
    body = re.sub(r'//[^\n]*', '', body)
    return [v.split('=')[0].strip() for v in body.split(',') if v.strip()]


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    problems = []
    try:
        langs = {lang: load_keys(lang) for lang in LANGUAGES}
    except (ET.ParseError, ValueError) as e:
        print(f'FAIL: {e}')
        return 1
    zh, en = (langs[lang] for lang in LANGUAGES)
    problems += [f'missing in English: {k}' for k in sorted(set(zh) - set(en))]
    problems += [f'missing in ChineseTraditional: {k}' for k in sorted(set(en) - set(zh))]
    for lang, keys in langs.items():
        problems += [f'{lang}: empty value for {k}' for k, v in keys.items() if not v.strip()]
        for k, v in keys.items():
            other = langs['English' if lang == 'ChineseTraditional' else 'ChineseTraditional'].get(k)
            if other is not None and set(re.findall(r'\{\d+\}', v)) != set(re.findall(r'\{\d+\}', other)):
                problems.append(f'placeholder mismatch for {k}')

    sources = ''
    for path in glob.glob(os.path.join(SOURCE, '**', '*.cs'), recursive=True):
        with open(path, encoding='utf-8') as f:
            sources += f.read() + '\n'
    used = set(LITERAL.findall(sources))
    for prefix, (enum, skip) in DYNAMIC.items():
        if prefix not in zh:  # the bare prefix is only a key when it is also used on its own
            used.discard(prefix)
        used |= {prefix + v for v in enum_values(enum, sources) if v not in skip}
    problems += [f'used but not defined: {k}' for k in sorted(used - set(zh))]
    unused = sorted(set(zh) - used)

    for p in sorted(set(problems)):
        print('FAIL:', p)
    if unused:
        print('note: defined but not referenced as a literal:', ', '.join(unused))
    print(f'{len(zh)} keys, {len(used)} referenced, {len(set(problems))} problems')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
