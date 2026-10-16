"""Export the C# rule-consistency fixture from the Python rules.

Usage:
    python -I Tools/export_rule_fixtures.py

Writes Source/RimTaiName.Tests/Fixtures/rules.tsv with one row per dictionary entry
(NameTable/translations/dictionary_draft.csv) plus hand-written edge cases. Columns:
text, namespace, key (NameKey), special (special_form() or empty), zh (final translation),
valid (1 when check() reports none of 空白／含非中文字元／超過長度上限), origin (dict or edge).
The C# tests assert that NameKey, SpecialFormRule and NameSanitizer.Check agree on every row.
Rerun after editing name_rules.py or regenerating the dictionary.
"""
import csv
import os
import sys
import unicodedata

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from name_rules import check, special_form  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DRAFT = os.path.join(ROOT, 'NameTable', 'translations', 'dictionary_draft.csv')
OUT = os.path.join(ROOT, 'Source', 'RimTaiName.Tests', 'Fixtures', 'rules.tsv')
FAILING = {'空白', '含非中文字元', '超過長度上限'}

# (text, namespace, zh): cases the vanilla data does not cover.
EDGE_CASES = [
    ('ＭＪ', 'nick', 'ＭＪ'),            # full-width capitals: NFKC folds them for special forms
    ('ＭＪ', 'name', 'MJ'),
    ('B', 'name', 'B'),
    ('B', 'nick', '阿B'),
    ('b', 'nick', '阿b'),                # lower case is not a special form, and b is not a source capital
    ('ABCD', 'name', 'ABCD'),
    ('ABCDE', 'name', 'ABCDE'),          # five capitals: not a special form
    ('  Fox ', 'name', '福克斯'),
    ('Fox', 'nick', '狐狸'),
    ('André', 'name', '安德烈'),
    ('ANDRÉ', 'name', '安德烈'),
    ('Ad', 'name', 'AD'),                # D does not appear capitalised in the source
    ('Big D', 'nick', '大D'),
    ('Big D', 'nick', '大d'),
    ('Jean-Luc', 'name', '尚·呂克'),     # middle dot is rejected by the check
    ('Jean-Luc', 'name', '尚呂克'),
    ("O'Brien", 'name', '歐布萊恩'),
    ('Lee', 'name', '李' * 12),
    ('Lee', 'name', '李' * 13),          # over the 12-character limit for names
    ('Doc', 'nick', '老' * 16),
    ('Doc', 'nick', '老' * 17),          # over the 16-character limit for nicknames
    ('X', 'nick', ''),                   # blank translation
    ('Kim', 'name', '金1'),               # digits are not allowed
    ('Kim', 'name', '金 '),               # spaces are not allowed
    ('Haruka', 'name', 'はるか'),         # kana are not allowed
    ('Kim', 'name', '\U0002000b'),        # CJK Extension B (surrogate pair in C#)
]


def row(text, namespace, zh, origin):
    for value in (text, zh):
        if any(c in value for c in '\t\r\n'):
            raise ValueError(f'tab or newline in {value!r}')
    key = unicodedata.normalize('NFC', text.strip()).lower()
    special = special_form(text, namespace) or ''
    flags = set(check(text, zh, namespace)) & FAILING
    return [text, namespace, key, special, zh, '0' if flags else '1', origin]


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    rows = [row(r['text'], r['namespace'], r['final_zh'], 'dict')
            for r in csv.DictReader(open(DRAFT, encoding='utf-8-sig'))]
    rows += [row(text, ns, zh, 'edge') for text, ns, zh in EDGE_CASES]
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        f.write('text\tnamespace\tkey\tspecial\tzh\tvalid\torigin\n')
        for r in rows:
            f.write('\t'.join(r) + '\n')
    invalid = sum(1 for r in rows if r[5] == '0')
    print(f'{len(rows)} rows ({len(EDGE_CASES)} edge cases, {invalid} invalid) -> {OUT}')


if __name__ == '__main__':
    main()
