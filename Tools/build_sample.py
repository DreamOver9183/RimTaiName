"""Build a stratified, numbered sample of name tokens for a Gemini translation spot-check.

Reads NameTable/tokens.csv and full_names.csv, writes NameTable/samples/sample_input.csv.
The sample is reproducible (fixed seed). Few-shot example words are excluded.

Usage:
    python -I build_sample.py [seed]
"""
import csv
import os
import random
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TABLE = os.path.join(ROOT, 'NameTable')
OUT = os.path.join(TABLE, 'samples', 'sample_input.csv')

# Words used as few-shot examples in the prompt; never sampled.
FEW_SHOT_WORDS = {'gisele', 'miller', 'nicholas', 'anna', 'fox', 'doc', 'lena', 'bob', 'alex',
                  'haruka', 'takeshi', 'wu', 'greywind', 'red', 'shadow', 'nguyen', 'jean-luc',
                  'van dyck', "o'brien"}
# Deliberate probes for East Asian surname handling.
PROBES = {('name', 'li'), ('name', 'huang'), ('name', 'liu'), ('name', 'kim')}

LIST_CONTEXT = {
    'Imperial_First_Male': 'Shattered Empire noble (Byzantine-style) given name',
    'Imperial_First_Female': 'Shattered Empire noble (Byzantine-style) given name',
    'Imperial_Last': 'Shattered Empire noble (Byzantine-style) surname',
    'Creepjoiner_Nick': 'creepjoiner (eerie horror stranger) nickname',
    'Creepjoiner_Last': 'creepjoiner (eerie horror stranger) surname',
    'Nick_Unisex_Pig': 'pigskin (pig-human xenotype) nickname',
    'Nick_Unisex_Waster': 'waster (toxic wasteland raider xenotype) nickname',
}


def load():
    tokens = list(csv.DictReader(open(os.path.join(TABLE, 'tokens.csv'), encoding='utf-8-sig')))
    examples = {}
    for r in csv.DictReader(open(os.path.join(TABLE, 'full_names.csv'), encoding='utf-8-sig')):
        full = f"{r['first']} '{r['nick']}' {r['last']}" if r['nick'] else f"{r['first']} {r['last']}"
        rank = 0 if r['source'] != 'solid_name' else 1
        keys = [('name', r['first'].lower()), ('name', r['last'].lower())]
        if r['nick_relation'] == 'explicit':
            keys.append(('nick', r['nick'].lower()))
        for k in keys:
            if k not in examples or rank < examples[k][0]:
                examples[k] = (rank, full)
    return tokens, {k: v[1] for k, v in examples.items()}


def token_type(t):
    if t['namespace'] == 'nick':
        return 'nickname'
    slots = set(t['slots'].split(';'))
    if slots == {'last'}:
        return 'surname'
    if slots == {'first'}:
        return 'given_name'
    return 'given_name_or_surname'


def main():
    seed = int(sys.argv[1]) if len(sys.argv) > 1 else 20261014
    rng = random.Random(seed)
    tokens, examples = load()
    pool = [t for t in tokens if t['text'].lower() not in FEW_SHOT_WORDS]
    picked, used = [], set()

    def key(t):
        return (t['namespace'], t['text'].lower())

    def take(stratum, cands, n):
        cands = [t for t in cands if key(t) not in used]
        for t in rng.sample(cands, min(n, len(cands))):
            used.add(key(t))
            picked.append((stratum, t))

    def tier(t):
        return int(t['tier'])

    def has_src(t, s):
        return s in t['sources']

    take('A 日文名（名字庫）', [t for t in pool if t['bank_category'] == 'Japanese names'], 14)
    take('B 俄文名（名字庫）', [t for t in pool if t['bank_category'] in ('Russian', 'Russian names')], 10)
    take('C 英國名（名字庫）', [t for t in pool if t['bank_category'] == 'British names'], 8)
    take('D 作者自訂名（名字庫）', [t for t in pool if t['bank_category'] == "Tynan's custom names"], 10)
    take('E 未分類名（名字庫）', [t for t in pool if tier(t) == 1 and t['namespace'] == 'name'
                                and not t['bank_category'] and 'first' in t['slots']], 8)
    take('F 姓（名字庫，東亞探針）', [t for t in pool if key(t) in PROBES], 4)
    take('F 姓（名字庫）', [t for t in pool if has_src(t, 'bank:Last')], 12)
    for cat, n in (('Body', 2), ('Misc', 3), ('Titles', 2)):
        take('G 暱稱（名字庫）', [t for t in pool if has_src(t, 'bank:Nick_') and t['bank_category'] == cat], n)
    take('G 暱稱（名字庫）', [t for t in pool if has_src(t, 'bank:Nick_Unisex')], 5)
    take('H 固定角色 名／姓', [t for t in pool if tier(t) == 2 and t['namespace'] == 'name'], 8)
    take('H 固定角色 暱稱', [t for t in pool if tier(t) == 2 and t['namespace'] == 'nick'], 10)
    take('I 玩家名字 名／姓（常見）', [t for t in pool if tier(t) == 3 and t['namespace'] == 'name'
                                  and int(t['freq']) >= 3], 5)
    take('I 玩家名字 名／姓（長尾）', [t for t in pool if tier(t) == 3 and t['namespace'] == 'name'
                                  and int(t['freq']) == 1 and not t['form_flags']], 5)
    take('I 玩家名字 暱稱（常見）', [t for t in pool if tier(t) == 3 and t['namespace'] == 'nick'
                                 and int(t['freq']) >= 3], 6)
    take('I 玩家名字 暱稱（長尾）', [t for t in pool if tier(t) == 3 and t['namespace'] == 'nick'
                                 and int(t['freq']) == 1 and not t['form_flags']], 8)
    for lst, n in (('Imperial_First_Male', 4), ('Imperial_First_Female', 4), ('Imperial_Last', 4)):
        take('J 帝國名（官方盲測）', [t for t in pool if has_src(t, 'dlc_list:' + lst) and t['zh_tw_official']], n)
    take('K 怪人暱稱（官方盲測）', [t for t in pool if has_src(t, 'dlc_list:Creepjoiner_Nick')
                               and t['zh_tw_official']], 8)
    take('L 豬人／廢土人暱稱', [t for t in pool if has_src(t, 'dlc_list:Nick_Unisex_Pig')
                            or has_src(t, 'dlc_list:Nick_Unisex_Waster')], 8)
    for flag in ('hyphen', 'space', 'apostrophe', 'accented'):
        take('M 特殊字形', [t for t in pool if flag in t['form_flags'].split(';')
                        and 'over_max_len' not in t['form_flags']], 3)

    write_input(OUT, [(f'S{i:03d}', stratum, t) for i, (stratum, t) in enumerate(picked, 1)], examples)
    print(f'{len(picked)} samples -> {OUT}')


INPUT_FIELDS = ['id', 'stratum', 'namespace', 'text', 'type', 'gender', 'origin', 'used_by',
                'example', 'tier', 'freq', 'form_flags', 'zh_tw_official']
ORIGIN = {'Japanese names': 'Japanese', 'Russian': 'Russian', 'Russian names': 'Russian',
          'British names': 'British', "Tynan's custom names": 'invented/sci-fi'}


def write_input(path, entries, examples):
    """Write (id, stratum, token) entries as a translation input CSV with model context columns."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8-sig', newline='') as f:
        w = csv.writer(f)
        w.writerow(INPUT_FIELDS)
        for tid, stratum, t in entries:
            lists = [s.split(':')[1].split('(')[0] for s in t['sources'].split(';') if s.startswith('dlc_list:')]
            used_by = '; '.join(LIST_CONTEXT[l] for l in lists if l in LIST_CONTEXT)
            genders = sorted(set(t['genders'].split(';')) - {'either'})
            gender = genders[0] if len(genders) == 1 else 'any'
            k = (t['namespace'], t['text'].lower())
            w.writerow([tid, stratum, t['namespace'], t['text'], token_type(t), gender,
                        ORIGIN.get(t['bank_category'], ''), used_by, examples.get(k, ''), t['tier'],
                        t['freq'], t['form_flags'], t['zh_tw_official']])


if __name__ == '__main__':
    main()
