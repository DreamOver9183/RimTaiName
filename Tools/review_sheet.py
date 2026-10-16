"""Dictionary review sheet for tiers 1, 2 and 4 (Step 5).

Usage:
    python -I Tools/review_sheet.py export [--force]  # dictionary_draft.csv -> NameTable/translations/review_sheet.csv
    python -I Tools/review_sheet.py apply             # review_sheet.csv -> review_corrections.csv / review_accepted.csv

export lists every tier 2 and tier 4 row, plus the tier 1 rows that still carry a flag, unless the row is
already settled (a correction exists, or review_accepted.csv holds its current translation). Columns
suggestion / reason / decision are for the review:
    decision = fix   use `suggestion` (written to review_corrections.csv with `reason`)
    decision = keep  keep `final_zh` (written to review_accepted.csv)
    decision empty   undecided; apply leaves the row alone
apply never overwrites an existing correction. Run merge_dictionary.py afterwards. export does not
overwrite an existing sheet (it may hold decisions not applied yet) unless --force is given, and writes
nothing when every row is settled.
"""
import csv
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from name_rules import check, entry_key, read_rows  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TR = os.path.join(ROOT, 'NameTable', 'translations')
SHEET = os.path.join(TR, 'review_sheet.csv')
ACCEPTED = os.path.join(TR, 'review_accepted.csv')
CORRECTIONS = os.path.join(TR, 'review_corrections.csv')
SHEET_FIELDS = ['id', 'namespace', 'text', 'tier', 'type', 'used_by', 'example', 'official', 'gemini_zh',
                'final_zh', 'source', 'flags', 'suggestion', 'reason', 'decision']
ACCEPTED_FIELDS = ['namespace', 'text', 'value', 'note']
CORRECTION_FIELDS = ['namespace', 'text', 'value', 'reason']


def key(row):
    return entry_key(row['namespace'], row['text'])


def read_csv(path):
    return read_rows(path) if os.path.exists(path) else []


def write_csv(path, fields, rows):
    with open(path, 'w', encoding='utf-8-sig', newline='') as f:
        w = csv.DictWriter(f, fieldnames=fields, extrasaction='ignore')
        w.writeheader()
        w.writerows(rows)


def export(force):
    if os.path.exists(SHEET) and not force:
        print(f'{SHEET} already exists; run apply first, or export --force to replace it')
        return 1
    draft = read_csv(os.path.join(TR, 'dictionary_draft.csv'))
    used_by = {r['id']: r['used_by'] for r in read_csv(os.path.join(TR, 'full_input.csv'))}
    accepted = {key(r): r['value'] for r in read_csv(ACCEPTED)}
    corrected = {key(r) for r in read_csv(CORRECTIONS)}
    rows = []
    for o in draft:
        if not (o['tier'] in ('2', '4') or (o['tier'] == '1' and o['flags'])):
            continue
        if key(o) in corrected or accepted.get(key(o)) == o['final_zh']:
            continue
        rows.append({**o, 'used_by': used_by.get(o['id'], ''), 'suggestion': '', 'reason': '', 'decision': ''})
    if not rows:
        if os.path.exists(SHEET):
            os.remove(SHEET)  # only reached with --force: the old sheet is stale
        print('nothing to review: every tier 1 (flagged), 2 and 4 row is settled')
        return 0
    write_csv(SHEET, SHEET_FIELDS, rows)
    print(f'{len(rows)} rows -> {SHEET}')
    return 0


def apply():
    if not os.path.exists(SHEET):
        print(f'{SHEET} does not exist; run export first')
        return 1
    sheet = read_csv(SHEET)
    accepted = {key(r): r for r in read_csv(ACCEPTED)}
    corrections = read_csv(CORRECTIONS)
    corrected = {key(r) for r in corrections}
    problems, fixed, kept = [], 0, 0
    for r in sheet:
        decision = (r.get('decision') or '').strip().lower()
        if not decision:
            continue
        k = key(r)
        if decision == 'fix':
            value = (r.get('suggestion') or '').strip()
            warnings = check(r['text'], value, r['namespace'])
            hard = [w for w in warnings if w != '名姓偏長']
            if not value or hard:
                problems.append(f"{r['namespace']}:{r['text']}: suggestion {value!r} rejected ({';'.join(hard) or 'empty'})")
                continue
            if k in corrected:
                problems.append(f"{r['namespace']}:{r['text']}: already in review_corrections.csv, left as it is")
                continue
            corrections.append({'namespace': r['namespace'], 'text': r['text'], 'value': value,
                                'reason': (r.get('reason') or '').strip() or 'Step 5 校對'})
            corrected.add(k)
            accepted.pop(k, None)
            fixed += 1
        elif decision == 'keep':
            accepted[k] = {'namespace': r['namespace'], 'text': r['text'], 'value': r['final_zh'],
                           'note': (r.get('reason') or '').strip()}
            kept += 1
        else:
            problems.append(f"{r['namespace']}:{r['text']}: unknown decision {decision!r}")
    if fixed or kept:
        write_csv(CORRECTIONS, CORRECTION_FIELDS, corrections)
        write_csv(ACCEPTED, ACCEPTED_FIELDS, sorted(accepted.values(), key=lambda a: key(a)))
    for p in problems:
        print('SKIP', p)
    print(f'fix {fixed}, keep {kept}, skipped {len(problems)}; now run: python -I Tools/merge_dictionary.py')
    return 1 if problems else 0


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    args = sys.argv[1:]
    if args == ['apply']:
        return apply()
    if args in (['export'], ['export', '--force']):
        return export(force='--force' in args)
    print(__doc__)
    return 2


if __name__ == '__main__':
    sys.exit(main())
