"""Extract every vanilla RimWorld humanlike name source into CSV tables.

Reads the local game install (no game code is executed) and writes:
    tokens.csv           one row per unique name word (namespace name/nick)
    full_names.csv       every fixed full name (solid bios, rare bios, player names)
    generator_lists.csv  syllables / endings / word lists used by culture and xenotype namers
    stats.json           counts used by README.md

Usage:
    python -I extract_vanilla_names.py [RimWorldRoot] [OutputDir]
"""
import collections
import csv
import json
import os
import re
import sys
import tarfile
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_textassets import read_text_assets  # noqa: E402

DEFAULT_ROOT = r'D:\SteamLibrary\steamapps\common\RimWorld'
ZH_TAR = 'ChineseTraditional (繁體中文).tar'

# Name bank TextAssets inside resources.assets -> (slot, gender)
BANK_FILES = {
    'First_Male': ('first', 'male'),
    'First_Female': ('first', 'female'),
    'Last': ('last', 'unisex'),
    'Nick_Male': ('nick', 'male'),
    'Nick_Female': ('nick', 'female'),
    'Nick_Unisex': ('nick', 'unisex'),
}
# Solid name TextAssets (Resources/Backstories/Solid)
SOLID_FILES = ['backstories', 'Rare', 'Requested', 'names']

# DLC-specific Strings lists: (dlc, relative path, slot, used_by)
LIST_FILES = [
    ('Royalty', 'Core', 'Names/Imperial_First_Male', 'first', 'male', 'Sophian culture (NamerPersonSophian)'),
    ('Royalty', 'Core', 'Names/Imperial_First_Female', 'first', 'female', 'Sophian culture (NamerPersonSophianFemale)'),
    ('Royalty', 'Core', 'Names/Imperial_Last', 'last', 'unisex', 'Sophian culture'),
    ('Biotech', 'Core', 'Names/Nick_Unisex_Pig', 'nick', 'unisex', 'Pigskin xenotype (NamerPersonPigskin)'),
    ('Biotech', 'Core', 'Names/Nick_Unisex_Waster', 'nick', 'unisex', 'Waster xenotype (NamerPersonWaster)'),
    ('Anomaly', 'Anomaly', 'Names/Creepjoiner_Last', 'last', 'unisex', 'Creepjoiner (NamerPersonCreepjoiner)'),
    ('Anomaly', 'Anomaly', 'Names/Creepjoiner_Nick', 'nick', 'unisex', 'Creepjoiner (NamerPersonCreepjoiner)'),
]
# Lists that only feed procedurally generated names (cannot be enumerated as full names)
GENERATOR_FILES = [
    ('Biotech', 'Core', 'WordParts/Syllables_Impid', 'Impid xenotype: first/last syllables'),
    ('Biotech', 'Core', 'WordParts/Syllables_Yttakin', 'Yttakin xenotype: first/last syllables'),
    ('Biotech', 'Core', 'WordParts/Syllables_Pig', 'Pigskin xenotype: first/last syllables'),
    ('Biotech', 'Core', 'WordParts/Syllables_Waster', 'Waster xenotype: first/last syllables'),
    ('Biotech', 'Core', 'WordParts/Syllables_Dirtmole', 'Dirtmole xenotype: last-name syllables'),
    ('Royalty', 'Core', 'WordParts/Syllables_Byzantinian', 'Sophian culture: WordImperial syllables'),
    ('Ideology', 'Core', 'WordParts/Syllables_Galician', 'Corunan culture: WordTribal syllables'),
    ('Ideology', 'Core', 'Words/Foreign/Tribal', 'Corunan culture: WordTribal words'),
    ('Ideology', 'Core', 'Words/Nouns/Colors', 'Corunan culture: [Color]'),
    ('Ideology', 'Core', 'Words/Nouns/Animals', 'Corunan culture: [Animal]'),
    ('Ideology', 'Core', 'Words/Nouns/Animals_Badass', "Corunan culture: '[AnimalBadass]' nickname"),
]
# RulePackDefs whose literal rules (endings, prefixes) we list
PERSON_NAMERS = {
    'NamerPersonImpid': ('Biotech', 'Impid xenotype'),
    'NamerPersonYttakin': ('Biotech', 'Yttakin xenotype'),
    'NamerPersonPigskin': ('Biotech', 'Pigskin xenotype'),
    'NamerPersonWaster': ('Biotech', 'Waster xenotype'),
    'NamerPersonDirtmole_Male': ('Biotech', 'Dirtmole xenotype (male)'),
    'NamerPersonDirtmole_Female': ('Biotech', 'Dirtmole xenotype (female)'),
}
TIER = {'bank': 1, 'solid_bio': 2, 'rare_bio': 2, 'solid_name': 3, 'dlc_list': 4}
CJK = re.compile(r'[\u4e00-\u9fff]')


def read_lines(text):
    """Return (entry, category) pairs from a Strings/name-bank text file."""
    category = ''
    out = []
    for raw in text.replace('\ufeff', '').splitlines():
        line = raw.strip()
        if not line:
            continue
        if line.startswith('//'):
            category = line[2:].strip()
            continue
        out.append((line, category))
    return out


def open_zh_tar(root, dlc_folder):
    path = os.path.join(root, 'Data', dlc_folder, 'Languages', ZH_TAR)
    return tarfile.open(path) if os.path.exists(path) else None


def tar_text(tar, member):
    if tar is None:
        return None
    try:
        f = tar.extractfile(member)
    except KeyError:
        return None
    return f.read().decode('utf-8-sig', 'replace') if f else None


def english_strings(root, dlc_folder, rel):
    path = os.path.join(root, 'Data', dlc_folder, 'Languages', 'English', 'Strings', rel + '.txt')
    with open(path, encoding='utf-8-sig') as f:
        return f.read()


def child_text(elem, *names):
    """Case-insensitive lookup of a direct child's text (accepts 'first' or 'firstInt')."""
    wanted = {n.lower() for n in names}
    for c in elem:
        if c.tag.lower() in wanted:
            return (c.text or '').strip()
    return None


def parse_solid(asset_name, text):
    """Yield dicts for each concrete PawnBio in a solid-bio TextAsset."""
    root = ET.fromstring(text.encode('utf-8'))
    abstract = {}
    for bio in root.iter('PawnBio'):
        if bio.get('Abstract') == 'True':
            abstract[bio.get('Name')] = {
                'rare': (child_text(bio, 'rare') or '').lower() == 'true',
                'pirate_king': (child_text(bio, 'pirateKing') or '').lower() == 'true',
            }
            continue
        parent = abstract.get(bio.get('ParentName'), {})
        name_el = next((c for c in bio if c.tag.lower() == 'name'), None)
        if name_el is None:
            continue
        first = child_text(name_el, 'first', 'firstInt')
        nick = child_text(name_el, 'nick', 'nickInt')
        last = child_text(name_el, 'last', 'lastInt')
        childhood = child_text(bio, 'childhood')
        adulthood = child_text(bio, 'adulthood')
        pk = child_text(bio, 'pirateKing')
        rare = child_text(bio, 'rare')
        is_rare = (rare or '').lower() == 'true' if rare is not None else parent.get('rare', False)
        has_bio = bool(childhood) and bool(adulthood)
        if has_bio:
            source = 'rare_bio' if is_rare else 'solid_bio'
        else:
            source = 'solid_name'  # PawnNameDatabaseSolid.AddPlayerContentName
        yield {
            'source': source,
            'asset': asset_name,
            'gender': (child_text(bio, 'gender') or 'Either').lower(),
            'first': first or '',
            'nick': nick or '',
            'last': last or '',
            'childhood': childhood or '',
            'adulthood': adulthood or '',
            'pirate_king': (pk or '').lower() == 'true' if pk is not None else parent.get('pirate_king', False),
            'rare': is_rare,
        }


def nick_relation(first, nick, last):
    if not nick:
        return 'empty'  # NickSet == false: Nick resolves to First or Last by hash parity
    if nick.lower() == first.lower():
        return 'alias_first'
    if nick.lower() == last.lower():
        return 'alias_last'
    return 'explicit'


def rule_literals(rules):
    """Map symbol -> [literal outputs] for rules whose output has no [tags]."""
    out = collections.OrderedDict()
    for r in rules:
        m = re.match(r'^\s*([A-Za-z_]+)(?:\([^)]*\))?\s*->(.*)$', r)
        if not m:
            continue
        sym, val = m.group(1), m.group(2)
        if '[' in val:
            continue
        out.setdefault(sym, []).append(val)
    return out


def english_rulepacks(root):
    """defName -> rulesStrings (with simple ParentName inheritance) for all RulePackDefs."""
    defs, named = {}, {}
    for dlc in os.listdir(os.path.join(root, 'Data')):
        d = os.path.join(root, 'Data', dlc, 'Defs')
        for dirpath, _, files in os.walk(d):
            for fn in files:
                if not fn.endswith('.xml'):
                    continue
                try:
                    tree = ET.parse(os.path.join(dirpath, fn))
                except ET.ParseError:
                    continue
                for rp in tree.getroot().iter('RulePackDef'):
                    rules = [li.text or '' for li in rp.findall('./rulePack/rulesStrings/li')]
                    entry = {'parent': rp.get('ParentName'), 'rules': rules}
                    if rp.get('Name'):
                        named[rp.get('Name')] = entry
                    dn = rp.findtext('defName')
                    if dn:
                        defs[dn] = entry
    resolved = {}
    for dn, e in defs.items():
        rules = list(e['rules'])
        p = e['parent']
        while p and p in named:
            rules = named[p]['rules'] + rules
            p = named[p]['parent']
        resolved[dn] = rules
    return resolved


def zh_rulepacks(root):
    """defName -> translated rulesStrings from zh-TW DefInjected."""
    out = {}
    for dlc in os.listdir(os.path.join(root, 'Data')):
        tar = open_zh_tar(root, dlc)
        if tar is None:
            continue
        for m in tar.getmembers():
            if not (m.name.startswith('DefInjected/RulePackDef/') and m.name.endswith('.xml')):
                continue
            try:
                tree = ET.fromstring(tar.extractfile(m).read())
            except ET.ParseError:
                continue
            for el in tree:
                if el.tag.endswith('.rulePack.rulesStrings'):
                    out[el.tag.split('.')[0]] = [li.text or '' for li in el.findall('li')]
    return out


def solid_backstories(root):
    """identifier -> {'title', 'categories'} for solid backstories (English Defs)."""
    out = {}
    d = os.path.join(root, 'Data', 'Core', 'Defs', 'BackstoryDefs', 'Solid')
    for fn in os.listdir(d):
        for bs in ET.parse(os.path.join(d, fn)).getroot().iter('BackstoryDef'):
            ident = bs.findtext('identifier') or bs.findtext('defName')
            out[ident] = {
                'title': bs.findtext('title') or '',
                'categories': [li.text for li in bs.findall('./spawnCategories/li')],
            }
    return out


def zh_backstory_titles(root):
    """defName -> official zh-TW title from DefInjected/BackstoryDef."""
    out = {}
    tar = open_zh_tar(root, 'Core')
    for m in tar.getmembers():
        if m.name.startswith('DefInjected/BackstoryDef/') and m.name.endswith('.xml'):
            try:
                tree = ET.fromstring(tar.extractfile(m).read())
            except ET.ParseError:
                continue
            for el in tree:
                if el.tag.endswith('.title'):
                    out[el.tag[:-len('.title')]] = el.text or ''
    return out


def form_flags(text, max_len):
    flags = []
    if any(ord(c) > 127 for c in text):
        flags.append('accented')
    if '-' in text:
        flags.append('hyphen')
    if ' ' in text:
        flags.append('space')
    if "'" in text:
        flags.append('apostrophe')
    if len(text) > max_len:
        flags.append('over_max_len')
    return ';'.join(flags)


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_ROOT
    out_dir = sys.argv[2] if len(sys.argv) > 2 else os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'NameTable')
    os.makedirs(out_dir, exist_ok=True)
    version = open(os.path.join(root, 'Version.txt'), encoding='utf-8').read().strip()
    assets = {n: t for _, n, t in read_text_assets(
        os.path.join(root, 'RimWorldWin64_Data', 'resources.assets'))}

    # token key -> aggregated info
    tokens = {}

    def add_token(namespace, text, slot, gender, source, dlc, category='', official=''):
        text = text.strip()
        if not text:
            return
        key = (namespace, text.lower())
        t = tokens.get(key)
        if t is None:
            t = tokens[key] = {
                'namespace': namespace, 'forms': collections.Counter(), 'slots': set(),
                'genders': set(), 'sources': collections.Counter(), 'categories': set(),
                'dlcs': set(), 'official': set(), 'freq': 0, 'tier': 9,
            }
        t['forms'][text] += 1
        t['slots'].add(slot)
        t['genders'].add(gender)
        t['sources'][source] += 1
        if category:
            t['categories'].add(category)
        t['dlcs'].add(dlc)
        if official:
            t['official'].add(official)
        t['freq'] += 1
        t['tier'] = min(t['tier'], TIER[source.split(':')[0]])

    stats = {'game_version': version, 'bank': {}, 'solid': {}, 'dlc_lists': {}, 'generators': {}}

    # 1. Shuffled name bank (PawnNameDatabaseShuffled, HumanStandard)
    for asset, (slot, gender) in BANK_FILES.items():
        entries = read_lines(assets[asset])
        stats['bank'][asset] = {
            'count': len(entries),
            'categories': dict(collections.Counter(c or '(none)' for _, c in entries)),
        }
        ns = 'nick' if slot == 'nick' else 'name'
        for text, cat in entries:
            add_token(ns, text, slot, gender, 'bank:' + asset, 'Core', cat)

    # 2. Solid bios and player-contributed names
    bs_en, bs_zh = solid_backstories(root), zh_backstory_titles(root)
    full_rows = []
    for asset in SOLID_FILES:
        for bio in parse_solid(asset, assets[asset]):
            rel = nick_relation(bio['first'], bio['nick'], bio['last'])
            bio['nick_relation'] = rel
            cats = []
            for part in ('childhood', 'adulthood'):
                ident = bio[part]
                if ident:
                    info = bs_en.get(ident, {'title': '?', 'categories': []})
                    bio[part + '_title'] = info['title']
                    bio[part + '_title_zh_tw'] = bs_zh.get(ident, '')
                    cats += [c for c in info['categories'] if c not in cats]
            bio['spawn_categories'] = ';'.join(cats)
            full_rows.append(bio)
            src = bio['source']
            add_token('name', bio['first'], 'first', bio['gender'], src, 'Core')
            add_token('name', bio['last'], 'last', bio['gender'], src, 'Core')
            if rel == 'explicit':
                add_token('nick', bio['nick'], 'nick', bio['gender'], src, 'Core')
    sc = collections.Counter((r['source'], r['asset']) for r in full_rows)
    stats['solid'] = {
        'by_source_asset': {f'{s}/{a}': n for (s, a), n in sc.items()},
        'nick_relation': dict(collections.Counter((r['source'], r['nick_relation']) for r in full_rows).most_common()),
        'pirate_king_bios': sum(1 for r in full_rows if r['source'] != 'solid_name' and r['pirate_king']),
        'bio_spawn_categories': dict(collections.Counter(
            c for r in full_rows if r['source'] != 'solid_name'
            for c in r['spawn_categories'].split(';') if c).most_common()),
        'gender': dict(collections.Counter((r['source'], r['gender']) for r in full_rows)),
    }
    stats['solid']['nick_relation'] = {f'{s}/{n}': c for (s, n), c in stats['solid']['nick_relation'].items()}
    stats['solid']['gender'] = {f'{s}/{g}': c for (s, g), c in stats['solid']['gender'].items()}

    # 3. DLC-specific finite lists (+ official zh-TW where line-aligned)
    tars = {}
    for dlc, folder, rel, slot, gender, used_by in LIST_FILES:
        en = read_lines(english_strings(root, folder, rel))
        tars.setdefault(folder, open_zh_tar(root, folder))
        zh_text = tar_text(tars[folder], f'Strings/{rel}.txt')
        zh = read_lines(zh_text) if zh_text else []
        aligned = len(zh) == len(en)
        stats['dlc_lists'][rel] = {'dlc': dlc, 'used_by': used_by, 'en': len(en), 'zh_tw': len(zh),
                                   'zh_tw_aligned': aligned}
        ns = 'nick' if slot == 'nick' else 'name'
        for i, (text, cat) in enumerate(en):
            add_token(ns, text, slot, gender, 'dlc_list:' + rel.split('/')[-1], dlc, cat,
                      zh[i][0] if aligned else '')

    # 4. Generator-only lists (syllables, word lists, literal endings)
    gen_rows = []
    for dlc, folder, rel, used_by in GENERATOR_FILES:
        en = read_lines(english_strings(root, folder, rel))
        tars.setdefault(folder, open_zh_tar(root, folder))
        zh_text = tar_text(tars[folder], f'Strings/{rel}.txt')
        zh = read_lines(zh_text) if zh_text else []
        aligned = len(zh) == len(en)
        translated = bool(zh) and any(CJK.search(z) for z, _ in zh)
        stats['generators'][rel] = {'dlc': dlc, 'used_by': used_by, 'en': len(en), 'zh_tw': len(zh),
                                    'zh_tw_aligned': aligned, 'zh_tw_is_chinese': translated}
        for i, (text, _) in enumerate(en):
            gen_rows.append({'list': rel.split('/')[-1], 'kind': 'strings_file', 'dlc': dlc,
                             'used_by': used_by, 'index': i, 'en': text,
                             'zh_tw_official': zh[i][0] if aligned and translated else ''})
    en_rp, zh_rp = english_rulepacks(root), zh_rulepacks(root)
    for dn, (dlc, used_by) in PERSON_NAMERS.items():
        en_lit = rule_literals(en_rp.get(dn, []))
        zh_lit = rule_literals(zh_rp.get(dn, []))
        for sym, vals in en_lit.items():
            zv = zh_lit.get(sym, [])
            aligned = len(zv) == len(vals)
            stats['generators'][f'{dn}.{sym}'] = {'dlc': dlc, 'used_by': used_by, 'en': len(vals),
                                                 'zh_tw': len(zv), 'zh_tw_aligned': aligned}
            for i, v in enumerate(vals):
                gen_rows.append({'list': f'{dn}.{sym}', 'kind': 'rule_literal', 'dlc': dlc,
                                 'used_by': used_by, 'index': i, 'en': v,
                                 'zh_tw_official': zv[i] if aligned else ''})

    # Write tokens.csv
    def sort_key(t):
        return (t['namespace'], t['tier'], -t['freq'], t['forms'].most_common(1)[0][0].lower())

    with open(os.path.join(out_dir, 'tokens.csv'), 'w', encoding='utf-8-sig', newline='') as f:
        w = csv.writer(f)
        w.writerow(['namespace', 'text', 'tier', 'freq', 'slots', 'genders', 'sources', 'bank_category',
                    'dlc', 'form_flags', 'zh_tw_official', 'zh_tw', 'review', 'notes'])
        for t in sorted(tokens.values(), key=sort_key):
            text = t['forms'].most_common(1)[0][0]
            w.writerow([
                t['namespace'], text, t['tier'], t['freq'],
                ';'.join(sorted(t['slots'])), ';'.join(sorted(t['genders'])),
                ';'.join(f'{s}({n})' if n > 1 else s for s, n in sorted(t['sources'].items())),
                ';'.join(sorted(t['categories'])), ';'.join(sorted(t['dlcs'])),
                form_flags(text, 16 if t['namespace'] == 'nick' else 12),
                ' / '.join(sorted(t['official'])), '', '', '',
            ])

    with open(os.path.join(out_dir, 'full_names.csv'), 'w', encoding='utf-8-sig', newline='') as f:
        cols = ['source', 'asset', 'gender', 'first', 'nick', 'last', 'nick_relation',
                'childhood', 'childhood_title', 'childhood_title_zh_tw',
                'adulthood', 'adulthood_title', 'adulthood_title_zh_tw',
                'spawn_categories', 'pirate_king', 'rare']
        w = csv.DictWriter(f, fieldnames=cols, extrasaction='ignore')
        w.writeheader()
        order = {'rare_bio': 0, 'solid_bio': 1, 'solid_name': 2}
        for r in sorted(full_rows, key=lambda r: (order[r['source']], r['last'].lower(), r['first'].lower())):
            w.writerow(r)

    with open(os.path.join(out_dir, 'generator_lists.csv'), 'w', encoding='utf-8-sig', newline='') as f:
        w = csv.DictWriter(f, fieldnames=['list', 'kind', 'dlc', 'used_by', 'index', 'en', 'zh_tw_official'])
        w.writeheader()
        w.writerows(gen_rows)

    ns_counts = collections.Counter(t['namespace'] for t in tokens.values())
    tier_counts = collections.Counter((t['namespace'], t['tier']) for t in tokens.values())
    stats['tokens'] = {
        'total': len(tokens),
        'by_namespace': dict(ns_counts),
        'by_namespace_tier': {f'{ns}/tier{tr}': n for (ns, tr), n in sorted(tier_counts.items())},
        'with_official_zh_tw': sum(1 for t in tokens.values() if t['official']),
        'freq1_solid_only': sum(1 for t in tokens.values() if t['tier'] == 3 and t['freq'] == 1),
    }
    with open(os.path.join(out_dir, 'stats.json'), 'w', encoding='utf-8') as f:
        json.dump(stats, f, ensure_ascii=False, indent=2)
    print(json.dumps(stats['tokens'], ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
