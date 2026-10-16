"""Shared translation prompt rules and output checks for RimTaiName dictionary building."""
import re
import unicodedata

TRANSLATION_RULES = """你是《RimWorld》（邊緣世界）的臺灣繁體中文在地化譯者。請把輸入的每個「角色姓名詞」翻成中文。不要使用任何工具，不要讀寫檔案，直接輸出結果。

【輸入欄位】
id、text（原文）、type（given_name 名／surname 姓／given_name_or_surname 名或姓／nickname 暱稱）、gender、origin（Japanese、Russian、British、invented/sci-fi 等來源提示，可能為空）、used_by（特殊族群說明，可能為空）、example（原版中含此詞的完整姓名，格式為「名 '暱稱' 姓」，可能為空）。

【規則】
1. 名與姓：採自然、符合臺灣習慣的音譯，避免中國大陸譯法（例：Bob → 鮑伯，不是鮑勃）。
   - 俄文名採臺灣常見寫法，不要用「季」對應 di 這類中國大陸俄語譯音慣例。
   - 單字譯名不可是容易被誤讀成一般詞語的字；有歧義時改用較長的音譯或較少歧義的字。
   - 避免與知名角色、鬼怪或負面人物產生聯想的寫法。
2. 東亞人名用漢字：
   - 日文名（羅馬拼音）以最常見的日文漢字表記，並使用繁體字形（例：Haruka → 遙、Takeshi → 武；桜 → 櫻、沢 → 澤）。只有一個漢字的名字要選最常見的寫法。
   - 中文拼音姓名用對應漢字（例：Wu → 吳）；越南姓用慣用漢字（例：Nguyen → 阮）；韓國姓名用韓國常用漢字（例：Kim → 金），多音節的韓國名每個音節都要譯出。
   - 同一個詞也可能是西方名，請依 origin 與 example 判斷。
3. 部落或複合字名可依字根意譯成自然的複合名（例：Greywind → 灰風），否則音譯。
4. 暱稱：
   - 具象名詞、動物、自然現象、職業、外貌或個性外號 → 意譯，簡短有力，符合 RimWorld 外號風格（例：Fox → 狐狸、Doc → 老醫、Red → 紅髮、Shadow → 暗影）。
   - 暱稱要像別人會這樣叫他的「名詞或稱呼」，不要用形容詞或「XX者」直譯（例：Sleepy → 瞌睡蟲，不是想睡；Speedy → 飛毛腿，不是快速者）。
   - 軍階、職稱等用臺灣常用說法（例：Corporal → 下士）。
   - 常見人名或其簡稱 → 臺灣慣用音譯（例：Lena → 蓮娜、Alex → 艾力克斯）。若 example 顯示暱稱是名或姓的簡稱，譯名要與其音譯一致。
   - 沒有明確含意的網路代號 → 音譯。
   - used_by 有說明時（例如帝國貴族、恐怖怪人、豬人、廢土掠奪者），譯名要符合該族群的氣質。
5. 輸出限制：只能用繁體中文字；原文中的大寫縮寫字母要保留成半形大寫（例：Big D → 大D、Lil E → 小E），除此之外不得出現拉丁字母；不得包含任何標點符號（包括「·」「-」「'」「.」）、空白或數字；名、姓不超過 12 字，暱稱不超過 16 字；名與姓建議 2 到 5 字。原文含連字號、空格或撇號時，合併成一個自然的譯名（例：Jean-Luc → 尚呂克、Van Dyck → 范戴克、O'Brien → 歐布萊恩）。
6. 每個 id 都必須回傳，且只回傳一個譯名。

【範例】
Gisele（given_name）→ 吉賽兒；Miller（surname）→ 米勒；Nicholas → 尼古拉斯；Anna → 安娜；Fox（nickname）→ 狐狸；Lena（nickname）→ 蓮娜；Haruka（given_name, Japanese）→ 遙；Wu（surname）→ 吳；Greywind（surname）→ 灰風
"""

OUTPUT_SCHEMA = {
    'type': 'object',
    'properties': {
        'items': {
            'type': 'array',
            'items': {
                'type': 'object',
                'properties': {
                    'id': {'type': 'string'},
                    'zh': {'type': 'string'},
                },
                'required': ['id', 'zh'],
            },
        },
    },
    'required': ['items'],
}

CJK_ONLY = re.compile(r'^[㐀-䶿一-鿿]+$')
# Characters that only exist in Simplified Chinese (common in transliterations / CJK names).
SIMPLIFIED = set('们尔兰罗丽维华亚达莱纳顿伦赛劳乔汉马鲁费贝玛娅丝东韦苏迈鲍约汤奥万历门卢齐杨刘黄吴张陈赵孙郑冯许邓韩'
                 '泽树优纪绘叶凉铃爱乐岛荣实胜辉龙凤时广庆刚诚宫桥边织泪阳飞岚卫闻兴')
# Japanese shinjitai forms that Taiwan writes in traditional form.
SHINJITAI = set('桜沢恵竜広浜斉栄来気楽弥亜絵稲渉実剣団図国県蔵豊単伝徳将巌険')


ABBREVIATION = re.compile(r'^[A-Z]{2,4}$')
SINGLE_LETTER = re.compile(r'^[A-Z]$')


def special_form(text, namespace):
    """Deterministic translation for initials, or None. Mirrors the in-game SpecialFormRule."""
    text = unicodedata.normalize('NFKC', text.strip())
    if ABBREVIATION.match(text):
        return text  # MJ, DJ, ET: keep as written
    if SINGLE_LETTER.match(text):
        return text if namespace == 'name' else '阿' + text  # B / 阿B
    return None


def latin_ok(text, zh):
    """Latin letters are allowed only as half-width capitals that also appear capitalised in the source."""
    source_caps = set(unicodedata.normalize('NFKC', text)) & set('ABCDEFGHIJKLMNOPQRSTUVWXYZ')
    return all(('㐀' <= c <= '鿿') or c in source_caps for c in zh)


def check(text, zh, namespace):
    """Return a list of automatic warnings for one translation."""
    warns = []
    if not zh:
        return ['空白']
    if not CJK_ONLY.match(zh) and not latin_ok(text, zh):
        warns.append('含非中文字元')
    if len(zh) > (16 if namespace == 'nick' else 12):
        warns.append('超過長度上限')
    if namespace != 'nick' and len(zh) > 6:
        warns.append('名姓偏長')
    simp = sorted({c for c in zh if c in SIMPLIFIED})
    if simp:
        warns.append('簡體字:' + ''.join(simp))
    shin = sorted({c for c in zh if c in SHINJITAI})
    if shin:
        warns.append('日文新字體:' + ''.join(shin))
    return warns
