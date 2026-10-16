# RimWorld 原版人名總表（RimTaiName 內建字典底稿）

*   **資料來源**：本機 RimWorld **1.6.4871 rev590**（`resources.assets`、`Data/*/Defs`、各 DLC 的英文 Strings 與官方繁中語言包 `.tar`）
*   **產生方式**：`python -I Tools/extract_vanilla_names.py [RimWorld 根目錄] [輸出資料夾]`。不會執行遊戲程式碼；遊戲改版後重新執行即可更新。
*   **檔案編碼**：CSV 為 UTF-8（含 BOM），可以直接用 Excel 開啟。

| 檔案 | 內容 | 筆數 |
| :--- | :--- | --: |
| `tokens.csv` | 每個不重複的「名字詞」一列，是翻譯工作的主表 | 24,596 |
| `full_names.csv` | 所有固定的完整姓名（固定角色、稀有角色、玩家貢獻名字） | 16,444 |
| `generator_lists.csv` | 文化／異種族命名器使用的音節、字尾、詞庫（無法窮舉成完整名字） | 1,054 |
| `stats.json` | 各項統計數字 | — |

---

## 1. 原版人名來源分類

小人名字的產生順序（依 `PawnBioAndNameGenerator` 原始碼整理）：

```
產生小人
 ├─ ① 固定角色 Solid Bio（最多 25%；派系首領一定先嘗試；嬰兒除外）
 │     名字＋童年／成年背景整組固定。發生在異種族命名「之前」，所以異種族小人也可能拿到固定角色名。
 │     ├─ 一般固定角色 backstories ............ 299 人
 │     └─ 稀有角色 Rare（Ludeon 團隊成員）...... 9 人（抽中後還有 50% 會重抽）
 │
 └─ ② 一般產生（依序判斷，命中即停止）
       ├─ 異種族命名器（Biotech）：Impid／Yttakin／Pigskin／Dirtmole 100%，Waster 75%
       ├─ PawnKind／背景故事命名器（原版人類目前沒有使用）
       ├─ 文化命名器：Corunan（Ideology）、Sophian（Royalty 帝國）
       └─ 標準規則（含 Astropolitan、Rustican、Kriminul 文化，以及 Neanderthal、Hussar、Genie、Sanguophage、Highmate 等異種族）
             ├─ 50%：玩家貢獻名字 names（16,135 個完整姓名，每局不重複使用）
             └─ 其餘：名字庫隨機組合（名＋姓；暱稱 15% 取自暱稱庫，否則 50/50 等於名或姓）
```

*   **特殊事件人物**：
    *   **黑衣人**（`StrangerInBlack`）**沒有專屬命名器**，名字依上述一般規則產生，所以每次都不同。
    *   **怪人**（Anomaly creepjoiner）使用 `NamerPersonCreepjoiner`：名取自標準名字庫，姓與暱稱取自專屬清單。
*   **新生兒**：玩家派系的嬰兒，第一個名字先設為「Baby」的翻譯字串，暱稱為空。

---

## 2. 名字庫（Tier 1，最常出現）

| 清單 | 筆數 | 子分類（檔案內的 `//` 註解） |
| :--- | --: | :--- |
| First_Male | 352 | 未分類 136、British 42、Tynan's custom 43、Japanese 56、Russian 75 |
| First_Female | 308 | 未分類 121、British 43、Tynan's custom 17、Japanese 58、Russian 69 |
| Last | 988 | 無子分類 |
| Nick_Male | 12 | Misc 5、Titles 2、Body 5 |
| Nick_Female | 9 | Misc 6、Titles 3 |
| Nick_Unisex | 42 | 無子分類 |

> **翻譯注意**：日文名（114 個）需要先決定譯法，是採常見漢字（如 Haruka → 遙）還是音譯；俄文名（144 個）宜採臺灣慣用譯法。

## 3. 固定角色與特殊人物（Tier 2）

### 3.1 稀有角色（Rare，Ludeon 團隊成員）

| 名 | 暱稱 | 姓 | 童年／成年（官方繁中） | 生成類別 |
| :--- | :--- | :--- | :--- | :--- |
| Tynan | Tynan | Sylvester | 龐克少年／遊戲開發者 | Pirate |
| Nathan | Morg | Cruzan | 洞穴兒童／穴居者 | Tribal、TribalMiner |
| Joe | （空白） | Gasparich | 鼓手／遊戲開發者 | Offworld、Scientist |
| Liam | Static | Harrison | 模擬上癮者／模擬軟體開發者 | Offworld、Scientist |
| Máté | （空白） | Mészáros | 好奇的小孩／街頭表演者 | Offworld、Scientist |
| Fey | Pheanox | Nickel | 戰爭遊戲粉絲／遊戲之主 | Offworld |
| Oskar | （空白） | Potocki | 藝術奴隸／模組設計師 | Outlander |
| Matthew | Cynapse | Ritchie | 修補匠／隱士 | Offworld、Outlander、Pirate |
| William | Will | Stacey | 迷宮兒童／迷宮設計師 | Offworld |

### 3.2 一般固定角色（299 人）

*   **性別**：男 240、女 48、不限 11。
*   **可擔任派系首領**（`pirateKing`）：104 人。
*   **背景生成類別**（含稀有角色，一人可有多類）：Offworld 266、Pirate 197、Cult 72、Outlander 65、**Tribal 10**、**ImperialCommon 8**、Farmer 7、Hunter 4、Trader 4、Slave 3、Scientist 3、**ImperialRoyal 2**、Miner 1、Civil 1、TribalMiner 1。
*   部落角色的暱稱多半有含意（如 Huntsman、Darklight、Hakuja），適合意譯。
*   `full_names.csv` 已附上每位角色的背景標題英文與官方繁中，可作為第二階段生成名字的參考。

### 3.3 指定名字（Requested）

只有 1 筆：Christian 'Aise' Foito。沒有背景故事，遊戲把它當成玩家貢獻名字使用。

## 4. 玩家貢獻名字（Tier 3，長尾）

*   **數量**：`names` 有 16,135 個完整姓名，加上 Requested 共 16,136 個。
*   **性別**：男 13,424、女 1,975、不限 737。
*   **暱稱關係**：

| 暱稱關係 | 筆數 | 比例 |
| :--- | --: | --: |
| 獨立外號 | 10,138 | 62.8% |
| **空白（由遊戲雜湊決定稱呼名或姓）** | **4,087** | **25.3%** |
| 等於名 | 1,290 | 8.0% |
| 等於姓 | 621 | 3.8% |

> 四分之一的玩家貢獻名字暱稱是空白的，證實計畫書 v2 第 5.3 節「暱稱為空」的處理是必要的。

*   **長尾分布**：只出現 1 次、且只來自玩家貢獻名字的詞有 18,450 個。很多暱稱是玩家的網路代號（如 Kalesin、Pheanox），難以判斷該音譯還是意譯。

## 5. DLC 專用清單（Tier 4）

| 清單 | DLC | 用途 | 英文 | 官方繁中 | 繁中遊戲內 |
| :--- | :--- | :--- | --: | :--- | :--- |
| Imperial_First_Male | Royalty | 帝國（Sophian）男性名 | 75 | 75（逐行對齊） | **已是中文** |
| Imperial_First_Female | Royalty | 帝國女性名 | 71 | 71（逐行對齊） | **已是中文** |
| Imperial_Last | Royalty | 帝國姓 | 55 | 55（逐行對齊） | **已是中文** |
| Creepjoiner_Last | Anomaly | 怪人姓 | 83 | 81（未對齊） | **已是中文** |
| Creepjoiner_Nick | Anomaly | 怪人暱稱 | 157 | 157（逐行對齊） | **已是中文** |
| Nick_Unisex_Pig | Biotech | 豬人暱稱 | 152 | 無 | 仍是英文 |
| Nick_Unisex_Waster | Biotech | 廢土人暱稱 | 135 | 無 | 仍是英文 |

`tokens.csv` 的 `zh_tw_official` 欄位已自動填入 355 筆官方譯名。其中包含不少常見西方名，可以當作譯名風格基準，例如 Nicholas → 尼古拉斯、Justin → 賈斯丁、Leo → 里歐、Anna → 安娜。

## 6. 程式產生的名字（無法窮舉）

| 命名器 | 使用者 | 組成 | 繁中遊戲內 |
| :--- | :--- | :--- | :--- |
| NamerPersonImpid | Impid 異種族 | 音節＋字尾 | **已是中文** |
| NamerPersonYttakin | Yttakin 異種族 | 音節＋字尾，姓可加 `Kin'`／`Wyn'` 字首 | **已是中文** |
| NamerPersonPigskin | Pigskin 異種族 | 音節＋字尾；暱稱取自 Nick_Unisex_Pig | 名、姓為中文，**暱稱為英文** |
| NamerPersonWaster | Waster 異種族 | 音節＋字尾；暱稱取自 Nick_Unisex_Waster | 名、姓為中文，**暱稱為英文** |
| NamerPersonDirtmole_* | Dirtmole 異種族 | **名取自標準名字庫**＋音節姓（男女字尾不同） | **名為英文**，姓為中文 |
| NamerPersonSophian(Female) | 帝國文化 | 帝國名＋帝國姓，或拜占庭音節詞 | 帝國名、姓為中文，**拜占庭音節詞仍是英文** |
| NamerPersonCorunan | Corunan 文化 | 部落詞、加利西亞音節、顏色、動物 | **已是中文** |
| NamerPersonCreepjoiner | 怪人 | **名取自標準名字庫**＋專屬姓、暱稱 | **名為英文**，姓與暱稱為中文 |

> 結論：繁中玩家實際會看到的英文名字，主要來自四處：名字庫、固定角色、玩家貢獻名字，以及上表標粗體「英文」的部分（Dirtmole 與怪人的名、豬人與廢土人的暱稱、帝國的拜占庭音節詞）。混合中英文的名字（如「Gisele 疤痕」）由計畫書「已含中文字即跳過」的規則處理，只翻英文欄位。

`generator_lists.csv` 收錄上述音節、字尾、詞庫，與官方繁中逐行對齊的會一併附上中文。日後若要在**英文介面**下翻譯異種族名字，可以拆音節後查表，產生與官方一致的譯名。

---

## 7. 字典涵蓋率分析（決定翻譯範圍用）

| 字典範圍 | 詞數 | 玩家貢獻名字「整筆」可離線翻譯 | 名字部件命中率 |
| :--- | --: | --: | --: |
| Tier 1（名字庫） | 1,654 | 3.0% | 22.8% |
| Tier 1＋2（＋固定角色） | 2,278 | 4.9% | 28.7% |
| Tier 1＋2＋出現 ≥3 次 | 3,721 | 14.6% | 47.8% |
| Tier 1＋2＋出現 ≥2 次 | 5,609 | 22.8% | 56.5% |
| 全部 | 24,596 | 100% | 100% |

標準小人約有 37.5% 會拿到玩家貢獻名字。只翻名字庫和固定角色的話，這類小人幾乎都還要呼叫 API。

**建議**：用 Gemini 批次機器翻譯**全部 24,596 詞**（每次 100 詞，約 250 次請求），內建字典直接涵蓋全部原版名字。人工校對則依優先順序進行：
1.  Tier 1（1,654 詞）
2.  Tier 2（624 詞）
3.  出現 ≥3 次的詞（約 1,400 詞）
4.  其餘長尾先接受機器翻譯，`review` 欄位留空，日後由玩家回報修正。

---

## 8. 欄位說明

### `tokens.csv`

| 欄位 | 說明 |
| :--- | :--- |
| `namespace` | `name`（名／姓，音譯）或 `nick`（暱稱，意譯優先），對應計畫書 5.1 的快取命名空間 |
| `text` | 原文，同一詞有多種大小寫時取最常見的寫法 |
| `tier` | 1 名字庫／2 固定角色／3 玩家貢獻名字／4 DLC 專用清單（取最高優先的來源） |
| `freq` | 在所有來源中出現的總次數 |
| `slots` / `genders` | 出現過的欄位（first、last、nick）與性別 |
| `sources` | 來源與次數，例如 `bank:First_Male;solid_name(213)` |
| `bank_category` | 名字庫子分類（British、Japanese、Russian、Tynan's custom、Misc、Titles、Body） |
| `dlc` | 所屬 DLC |
| `form_flags` | `accented`（重音字母 147）、`hyphen`（連字號 136）、`space`（空格 305）、`apostrophe`（撇號 79）、`over_max_len`（超過遊戲長度上限，3 個） |
| `zh_tw_official` | 官方繁中譯名（只在逐行對齊的清單上填入） |
| `zh_tw` / `review` / `notes` | 留給翻譯、校對狀態與備註 |

### `full_names.csv`

`source`（`rare_bio`／`solid_bio`／`solid_name`）、`asset`、`gender`、`first`、`nick`、`last`、`nick_relation`（`explicit`／`empty`／`alias_first`／`alias_last`）、背景故事 ID、背景標題（英文／官方繁中）、`spawn_categories`、`pirate_king`、`rare`。

### `generator_lists.csv`

`list`、`kind`（`strings_file` 或 `rule_literal`）、`dlc`、`used_by`、`index`、`en`、`zh_tw_official`。
