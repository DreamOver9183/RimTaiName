"""Merge Gemini translations with official zh-TW names into a reviewable dictionary draft.

Priority: review_corrections.csv > official_overrides.csv (empty value = use Gemini) > official > Gemini.
Then nicknames that are only a spelling variant of a tier 1-2 given name are aligned to that name.
Writes NameTable/translations/dictionary_draft.csv, review_queue.csv, nick_collisions.csv and
Dictionary/BaseNames_zhTW.tsv, and prints statistics.
"""
import collections
import csv
import os
import sys
import unicodedata

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from name_rules import check, special_form  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TR = os.path.join(ROOT, 'NameTable', 'translations')
DICT_OUT = os.path.join(ROOT, 'Dictionary', 'BaseNames_zhTW.tsv')
# Translation -> source texts for which that translation is fine (e.g. Sadako really is 貞子).
BLACKLIST = {'貞子': {'sadako'}}
# Name-like nicknames whose meaning translation was wrong; always align them to the given-name translation.
FORCE_ALIGN = {'yu', 'hiroshi', 'nobu', 'yoshi', 'ho', 'chang', 'levi', 'boyle', 'waldo', 'chase',
               'lander', 'bender'}


def load_fixes(name):
    path = os.path.join(TR, name)
    return {(o['namespace'], o['text'].lower()): o for o in csv.DictReader(open(path, encoding='utf-8-sig'))}


def align_nicknames(out):
    """Align nicknames that are transliterated variants of a tier 1-2 given name to that name."""
    names = {o['text'].lower(): o for o in out if o['namespace'] == 'name' and o['tier'] in ('1', '2')}
    counts = collections.Counter(c for o in names.values() if o['origin'] != 'Japanese'
                                 and len(o['final_zh']) >= 2 for c in o['final_zh'])
    translit = {c for c, n in counts.items() if n >= 3}
    aligned = 0
    for o in out:
        name = names.get(o['text'].lower())
        if o['namespace'] != 'nick' or not name or o['final_zh'] == name['final_zh'] or o['source'] in ('correction', 'special_form'):
            continue
        if o['text'].lower() in FORCE_ALIGN or all(c in translit for c in o['final_zh']):
            o['final_zh'], o['source'] = name['final_zh'], 'aligned_to_name'
            aligned += 1
    return aligned


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    rows = list(csv.DictReader(open(os.path.join(TR, 'full_results.csv'), encoding='utf-8-sig')))
    overrides = load_fixes('official_overrides.csv')
    corrections = load_fixes('review_corrections.csv')
    out = []
    for r in rows:
        gem = r['gemini_zh'].strip()
        offs = [o for o in r['zh_tw_official'].split(' / ') if o]
        k = (r['namespace'], r['text'].lower())
        ov = overrides.get(k)
        special = special_form(r['text'], r['namespace'])
        if k in corrections:
            final, source = corrections[k]['value'], 'correction'
        elif special:
            final, source = special, 'special_form'
        elif ov:
            final, source = (ov['value'], 'override') if ov['value'] else (gem, 'gemini(override)')
        elif offs:
            final, source = offs[0], 'official'
        else:
            final, source = gem, 'gemini'
        out.append({'id': r['id'], 'namespace': r['namespace'], 'text': r['text'], 'tier': r['tier'],
                    'freq': r['freq'], 'type': r['type'], 'origin': r['origin'], 'example': r['example'],
                    'official': r['zh_tw_official'], 'gemini_zh': gem, 'final_zh': final, 'source': source,
                    'multi_official': len(offs) > 1 and not ov, 'flags': [], 'review': '', 'notes': ''})
    aligned = align_nicknames(out)

    for o in out:
        final = o['final_zh']
        flags = check(o['text'], final, o['namespace']) if final else ['未翻譯']
        if o.pop('multi_official'):
            flags.append('官方多譯')
        if o['namespace'] == 'name' and len(final) == 1 and o['source'] != 'special_form':
            flags.append('單字名')
        if o['namespace'] == 'nick' and final.endswith('者'):
            flags.append('者字尾')
        if final in BLACKLIST and o['text'].lower() not in BLACKLIST[final]:
            flags.append('黑名單')
        o['flags'] = flags

    by_zh = collections.defaultdict(list)
    for o in out:
        if o['namespace'] == 'nick' and o['final_zh']:
            by_zh[o['final_zh']].append(o['text'])
    collisions = {z: t for z, t in by_zh.items() if len(t) > 1}

    fields = list(out[0].keys())
    for name, subset in (('dictionary_draft.csv', out),
                         ('review_queue.csv', [o for o in out if o['flags']])):
        with open(os.path.join(TR, name), 'w', encoding='utf-8-sig', newline='') as f:
            w = csv.DictWriter(f, fieldnames=fields)
            w.writeheader()
            for o in subset:
                w.writerow({**o, 'flags': ';'.join(o['flags'])})
    with open(os.path.join(TR, 'nick_collisions.csv'), 'w', encoding='utf-8-sig', newline='') as f:
        w = csv.writer(f)
        w.writerow(['zh', 'count', 'texts'])
        for z, t in sorted(collisions.items(), key=lambda kv: -len(kv[1])):
            w.writerow([z, len(t), ';'.join(t)])

    os.makedirs(os.path.dirname(DICT_OUT), exist_ok=True)
    with open(DICT_OUT, 'w', encoding='utf-8', newline='\n') as f:
        f.write('namespace\tkey\tzh_tw\n')
        for o in sorted(out, key=lambda o: (o['namespace'], o['text'].lower())):
            if o['final_zh']:
                key = unicodedata.normalize('NFC', o['text'].strip()).lower()
                f.write(f"{o['namespace']}\t{key}\t{o['final_zh']}\n")

    print('rows:', len(out), 'translated:', sum(1 for o in out if o['final_zh']))
    print('source:', dict(collections.Counter(o['source'] for o in out)), 'aligned nicknames:', aligned)
    flag_kinds = collections.Counter(f.split('(')[0].split(':')[0] for o in out for f in o['flags'])
    print('flags:', dict(flag_kinds))
    print('flagged rows:', sum(1 for o in out if o['flags']),
          'by tier:', dict(collections.Counter(o['tier'] for o in out if o['flags'])))
    print('nick collision groups (informational):', len(collisions))
    diff = [o for o in out if o['official'] and o['gemini_zh'] and o['gemini_zh'] not in o['official'].split(' / ')]
    print('official vs gemini differ:', len(diff), 'of', sum(1 for o in out if o['official']))
    print('dictionary ->', DICT_OUT)


if __name__ == '__main__':
    main()
