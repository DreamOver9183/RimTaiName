# RimTaiName 開發計畫書 v3

*   **版本**：v3（2026-10-16），取代 v2。v1、v2 保留在專案中作為歷史紀錄。
*   **驗證基準**：
    *   RimWorld 1.6.4871：開發機 rev590，測試機 rev591。
    *   Gemini API 官方文件（2026 年 10 月版）。
    *   Antigravity CLI 1.3.4。

---

## 1. 背景與目標

**RimTaiName** 是一款《RimWorld》角色命名輔助模組。它擴充原版命名視窗（`Dialog_NamePawn`），讓玩家一鍵把小人的英文姓名轉成符合臺灣繁體中文習慣的譯名。

**設計原則：**
*   極低侵入，100% 相容原版機制。
*   零存檔污染：只透過原版邏輯寫入 `Pawn.Name`，不新增任何會寫進存檔的元件。
*   不需 API Key 也能翻譯所有原版名字。
*   玩家的 API Key 只留在玩家自己的電腦，絕不外洩或上傳（第 9 節）。

**API Key 的定位：** 內建字典已經能離線翻譯絕大部分原版名字。只有以下兩種情況才需要玩家在設定頁輸入自己的 Gemini API Key：
*   翻譯字典以外的名字，例如其他 Mod 加入的名字、玩家手打的名字。
*   使用第二階段的「生成中文名」。

**階段目標：**

| 階段 | 內容 |
| :--- | :--- |
| 第一階段（MVP） | 解鎖人類小人「名、姓」的編輯；名、暱稱、姓三列加鎖頭；工具列加「翻譯姓名」與「還原」；三層翻譯管線（個人快取 → 內建字典 → Gemini API）；設定頁的 API Key 欄位與金鑰防護 |
| 第二階段 | 「生成中文名」：依性別、背景故事、文化、異種族與已鎖定欄位，生成音譯／意譯風格的名字，維持原版「名 姓」順序 |
| 未來 | 上架 Steam 創意工坊（Step 7） |
| 未排程 | 開局選人畫面也加入翻譯按鈕 |

**目標版本：**
*   **主要：** RimWorld 1.6（Steam 主線）。
*   **相容性測試：** 1.7。1.7 已改用 Ludeon 自家的 Waylith 引擎，2026-10-12 起在 unstable 分支提供。

---

## 2. 現況總覽（2026-10-16）

| 項目 | 狀態 | 成果 |
| :--- | :--- | :--- |
| 研究與規格校正 | 完成 | v1 → v2 共 16 項修正（附錄 A） |
| 原版人名抽取 | 完成 | `Tools/extract_vanilla_names.py` → `NameTable/`：共 24,596 個名字詞，分四層 |
| 內建字典 v1.0 | 完成 | `Dictionary/BaseNames_zhTW.tsv`。第 1、2、4 層逐筆校對；官方譯名逐筆對照；第 3 層抽查（v1.0 於 Step 5 完成） |
| Step 0 技術驗證 | 完成 | Harmony patch、遊戲內 HTTPS、工具列與鎖頭、焦點寫入皆通過；在測試機 386 個 Mod 環境下正常。新生兒視窗（開發模式＋上帝模式，有／無父母）與成年視窗已實測，鎖頭靠右對齊 |
| 開發環境 | 完成 | .NET 10 SDK 10.0.402；直接引用遊戲 DLL 編譯；`RimWorld\Mods\RimTaiName` 以 junction 連到專案；本機 git（branch `main`） |
| 實作計畫 | 完成並重審 | `RimTaiName 實作計畫.markdown`：Step 0～7 的工作清單與進度勾選。10-16 重審遊戲 API 與 Gemini API，修正已回寫本文件（附錄 A） |
| Step 1 | 完成 | PR #1～#7 已合併：金鑰儲存與遮蔽、Gemini 傳輸層（即時 API 測試通過）、個人快取、打包腳本、設定頁（修正 Listing 自動開新欄造成的塌陷）、金鑰稽核腳本；單元測試 140 項；測試機實測 DPAPI、測試連線、金鑰稽核皆通過；GitHub：DreamOver9183/RimTaiName |
| Step 2 | 完成 | PR #8～#10：名字規則（與 Python 逐筆一致的 fixture）、內建字典、暱稱別名與 `NameProcessorService`；單元測試 252 項 |
| Step 3 | 完成 | PR #11～#13：共用規則檔與 `PromptBuilder`、`GeminiNameTranslator`、`RequestGate`、`UiJob`、設定頁的開發者翻譯測試；單元測試 296 項、即時測試 5 項；測試機實測翻譯（Gemini＋內建字典）、測試連線與金鑰稽核皆通過（`TestLog/4`） |
| Step 4（**MVP**） | 完成 | PR #14、#16：命名視窗正式版——「翻譯姓名」「還原」、等待中保護（暫停點外關閉、只覆寫沒被改過的欄位、晚到結果只進快取）、確定後學習玩家修正；單元測試 327 項；測試機實測通過（`TestLog/5`、`TestLog/5-2`） |
| Step 5 | 完成 | PR #17：字典 v1.0——第 2、4 層逐筆校對（修正 100 筆），新增 `review_accepted.csv` 與校對表工具 `Tools/review_sheet.py`；校對佇列只剩第 3 層；Prompt 範例擴充 |
| Step 6 | 完成 | PR #18、#19：第二階段「生成中文名」——依性別、背景故事、文化、異種族、年齡層取新的中文名；鎖定欄位作為風格參考；生成結果不寫快取、不學習；測試機實測通過（`TestLog/6`） |
| Step 7 | 進行中 | PR #20：正式 `packageId`、預覽圖、創意工坊說明；1.0.0 已上傳（`PublishedFileId` 3820899951）。PR #21（1.0.1）：Gemini 過載時逾時改為 60 秒，提示改為「可能是模型太忙碌」。見第 11 節 |

**驗證用 Mod 的現有程式（Step 4 會改寫成正式版）：**
*   `Source/RimTaiName/Core/RimTaiNameMod.cs`：Mod 進入點；逐一套用 patch，單一失敗不影響其他。
*   `Source/RimTaiName/UI_Patches/Patch_Dialog_NamePawn.cs`：建構子、`MakeRow`、`DoWindowContents` 三個 patch 與 `ToolbarDrawer`。
*   `Source/RimTaiName/UI_Patches/NamePawnAccess.cs`：反射存取委派。
*   `Source/RimTaiName/UI_Patches/DialogState.cs`：每個視窗的狀態與註冊表。
*   `Source/RimTaiName/Spike/ConnectivityProbe.cs`：HTTPS 測試（Step 1 刪除，由 `GeminiInteractionsClient` 的「測試連線」取代）。

---

## 3. 已定案的設計決策

| 決策 | 理由 | 日期 |
| :--- | :--- | :--- |
| 翻譯順序：個人快取 → 內建字典 → Gemini API | 原版名字全部離線可譯，API 只處理其他 Mod 的名字與手打的名字 | 10-14 |
| 字典涵蓋全部 24,596 詞 | 只翻名字庫與固定角色時，玩家貢獻名字僅 4.9% 能整筆離線翻譯 | 10-14 |
| 快取鍵分 `name:`（音譯）與 `nick:`（意譯） | 同一個字當姓與當暱稱的譯法不同（Fox → 福克斯／狐狸） | 10-14 |
| 東亞人名用漢字（日文名用繁體字形） | 日文 Haruka → 遙；中文拼音 Wu → 吳；韓國 Kim → 金 | 10-14 |
| 官方繁中譯名優先，誤譯列覆寫清單 | 繁中玩家在遊戲內已看到官方譯名；官方有 43 處誤譯 | 10-15 |
| 特殊字形保留英文字母 | 縮寫保留原樣（MJ）；單一字母當暱稱寫成「阿B」 | 10-15 |
| 第二階段只生成音譯／意譯風格 | 原版一律以「名 姓」顯示，姓在前的華人姓名會被顛倒 | 10-14 |
| Gemini 用 Interactions API、`store: false`、金鑰放 header | `generateContent` 已列為 legacy；Interactions 預設會保存紀錄 | 10-14 |
| 預設模型 `gemini-3.5-flash-lite`；不傳 thinking 與取樣參數 | 2.5 系列已限制新帳號；3.x 改用 `thinking_level` 且取樣參數已棄用 | 10-14 |
| 網路用 `HttpClient`，禁用 `UnityWebRequest` | 後者只能在主執行緒使用；1.7 已改用非 Unity 引擎 | 10-14 |
| API Key 不寫入 Mod 設定檔，加密另存；只在玩家按下按鈕時送往固定的 Gemini 端點 | 防止設定檔、記錄檔、轉址等管道外洩（第 9 節） | 10-16 |
| 測試連線送一次極小的 Interactions 請求，不用 `models.get` | 一次驗證金鑰、模型、權限與額度，且只需處理一種錯誤格式；代價是用掉 1 次請求額度 | 10-16 |
| 非 Windows 平台的金鑰只在本次遊戲期間記憶 | Mono 在 macOS／Linux 會靜默改用較弱的 ManagedProtection，且無法在開發環境實測 | 10-16 |
| 直接引用遊戲 `Managed/` 的 DLL 編譯 | 只能用到遊戲實際存在的 API，且不需 NuGet 套件 | 10-15 |
| 單元測試用 xUnit | 業界標準；測試專案只連結純 C# 管線檔 | 10-16 |
| 離線批次翻譯用 Antigravity CLI，Python 同時開 8 個行程 | 比 Gemini 子代理少掉主代理抄寫的開銷；全量 15.7 分鐘 | 10-15 |

---

## 4. 遊戲機制規範（已依 1.6.4871 核對）

### 4.1 整合規範

| 項目 | 原版行為 | 本模組規範 |
| :--- | :--- | :--- |
| 適用對象 | 命名視窗同時被人類、動物、機械體使用；後兩者是 `NameSingle` | 只在 `Humanlike && Name is NameTriple` 時啟用 |
| 姓名結構 | `NameTriple(First, Nick, Last)` | 維持原格式；新生兒可從「建議...」選用父母已中文化的姓 |
| 顯示順序 | `ToStringFull` 輸出「`First Last`」 | 譯名一律採外文名的音譯／意譯風格 |
| 隱藏引號 | `Nick == First` 或 `Nick == Last` 時省略引號 | 同名關係的暱稱必須與對應欄位的中文完全相同（6.3） |
| 暱稱空白 | `NickSet == false` 時，稱呼由名、姓雜湊的奇偶決定，視窗暱稱欄空白 | 以開窗時的 `pawn.Name` 判斷稱呼來源（6.3） |
| 字元與長度 | `CharacterCardUtility.ValidNameRegex = ^[\p{L}0-9 '\-.]*$`；名 12、暱稱 16、姓 12 字（以 UTF-16 計）；只在打字時檢查。會擋 `·`、`・` 與 CJK 擴充 B 以後的字 | 程式寫入前自行清理與驗證（第 8 節第 4 點） |
| 零存檔污染 | — | 不新增 `ThingComp`、`WorldComponent`、`GameComponent` |
| UI 字型 | IMGUI 字型不支援 Emoji | 只用純文字、Keyed 語言包、原版 `Widgets` |

### 4.2 原版命名視窗（`Verse.Dialog_NamePawn`）

*   **建構子：** `(Pawn pawn, NameFilter visibleNames, NameFilter editableNames, Dictionary<NameFilter, List<string>> suggestedNames, string initialFirstNameOverride, …NickName…, …LastName…, …Title…)`
*   **權限**（`PawnNamingUtility.NamePawnDialog`）：
    *   成年小人：名、暱稱、姓、頭銜四列可見，只有暱稱與頭銜可編輯。
    *   新生兒命名期限內，**或開發模式且開啟上帝模式**（`DebugSettings.ShowDevGizmos`）：名、暱稱、姓三列皆可編輯，頭銜隱藏；姓欄顯示「建議...」（父母的姓），沒有隨機鈕。開發模式＋上帝模式下任何小人都會開出這個版本（只開開發模式不會），可用來測試新生兒視窗。
    *   父母都沒有姓時，「建議」清單是空的，姓欄**不畫任何按鈕**。
*   **確定流程：** 沒有覆寫 `OnAcceptKeyPressed`。按 Enter 或確定時，`DoWindowContents` 內依序執行 `BuildName()`（每次都 `new NameTriple`）→ `IsValid` → `pawn.Name = name` → `WindowStack.TryRemove(this)`。因此可用 `ReferenceEquals(pawn.Name, 開窗時的 Name)` 偵測「確定」。不要 patch `Pawn.Name` 的 setter（可能被 inline，且開局畫面每次按鍵都會重設名字）。
*   **點視窗外：** `closeOnClickedOutside = true`，點外面會直接取消。
*   **私有成員：**
    *   `Dialog_NamePawn`：`pawn`、`names`、`size`。
    *   巢狀類別 `NameContext`：`current`、`editable`、`nameIndex`（0 名／1 暱稱／2 姓／-1 頭銜）、`maximumNameLength`。
*   **版面：** `MakeRow` 依序畫出標籤、文字框、隨機鈕，欄距 17px。`DoWindowContents` 每幀以 `RectAggregator` 累計內容高度並自動調整視窗大小。
*   **開啟位置：** 角色資訊卡改名鈕、嬰兒出生信件、命名提示，以及動物／機械體改名鈕（不處理）。

---

## 5. 系統架構

### 5.1 目錄結構

```
RimTaiName/                          ✓ = 已存在
├── About/About.xml                  ✓ packageId 暫為 RimTaiName.Dev
├── LoadFolders.xml                  ✓ v1.6 → "/" + "1.6"
├── 1.6/Assemblies/RimTaiName.dll    ✓ 建置輸出，不附帶 0Harmony.dll
├── Languages/{ChineseTraditional,English}/Keyed/RimTaiName_Keys.xml   ✓
├── Dictionary/BaseNames_zhTW.tsv    ✓ namespace \t key \t zh_tw
├── Prompts/NameRules_zhTW.txt       規劃中：翻譯規則，C# 與 Python 共用
└── Source/
    ├── RimTaiName/                  ✓ net472，引用遊戲 DLL
    │   ├── Core/                    RimTaiNameMod ✓、RimTaiNameSettings、ApiKeyStore、KeyRedactor、ModLog
    │   ├── Pipeline/                純 C#，不引用遊戲型別、不寫記錄
    │   │                            NameRequestPayload、NameKey、SpecialFormRule、NickAliasResolver、
    │   │                            NameSanitizer、BuiltInDictionary、PersonalNameCache、NameProcessorService
    │   ├── Llm/                     純 C#：ILlmClient、GeminiInteractionsClient、PromptBuilder、MiniJson、
    │   │                            RequestGate、QuotaResetClock
    │   └── UI_Patches/              ✓ 驗證版，Step 4 改寫
    └── RimTaiName.Tests/            規劃中（Step 1）：xUnit，net10.0，連結 Pipeline/、Llm/ 與 Core/KeyRedactor.cs

開發用（不隨模組發佈）：
Tools/      ✓ 抽取、抽樣、批次翻譯、合併等 Python 工具
NameTable/  ✓ 抽取結果、翻譯紀錄、修正與覆寫清單
TestLog/    ✓ 測試機傳回的 Player.log
```

### 5.2 UI 擴充（已驗證）

| 元件 | 做法 |
| :--- | :--- |
| 解鎖名與姓 | 建構子 Prefix：`editableNames |= visibleNames & (First | Last)` |
| 加寬視窗 | 建構子 Postfix：`size.x += 41`（欄距 17 ＋ 勾選框 24） |
| 鎖頭 | `NameContext.MakeRow` Postfix：在該列剩下的空間以 `divider.NewCol(24f, HorizontalJustification.Right)` 靠右畫勾選框（姓欄沒有按鈕時也能對齊）；只畫 `nameIndex` 0～2 且可編輯的列 |
| 工具列 | `DoWindowContents` Transpiler：在 `rectAggregator.NewRow(17.5f)` 前插入 `ToolbarDrawer.Draw(this, ref rectAggregator)`；視窗高度由原版自動調整 |
| 套用結果 | `DoWindowContents` Prefix，且只在 `EventType.Layout` 時寫入 |
| 失敗保護 | 找不到注入點時只停用工具列；各 patch 包在 try/catch |

```
| 名字：  [ Gisele      ] [ 隨機 ]   [鎖] |
| 暱稱：  [ Fox         ] [ 隨機 ]   [鎖] |
| 姓氏：  [ Miller      ] [ 隨機 ]   [鎖] |
| 頭銜：  [             ]          （不處理）|
| [ 翻譯姓名 ]  [ 生成中文名（第二階段）]  [ 還原 ] |  ← 插入的列
| [ 取消 ]                         [ 確定 ] |
```

### 5.3 使用者流程

1.  **開窗：** 建立 `DialogState`，記錄 `pawn.Name`（含暱稱來源）與三欄初始值；鎖頭預設未鎖定。
2.  **抽籤與鎖定：** 玩家用原版「隨機」或手打，滿意的欄位就勾鎖頭。
3.  **翻譯：**
    *   能在本機解析的欄位立即寫入。
    *   其餘欄位打包成一次 API 請求：按鈕改為「處理中...」，並暫停「點視窗外即關閉」。
4.  **套用：** 只覆寫未鎖定、且等待期間沒被玩家改過的欄位。
5.  **確定：** 原版寫入 `Pawn.Name`。`DoWindowContents` Postfix 以 `ReferenceEquals` 偵測到名字物件已換掉時，若玩家修改了模組給的譯名，把修正寫入個人快取。
6.  **還原：** 未鎖定欄位恢復為開窗時的值。

### 5.4 資料契約（`NameRequestPayload`）

*   `Mode`：`Translate` 或 `Generate`。
*   每欄 `Text`、`Locked`、`OriginalText`。
*   `NickSource`：`Explicit`、`AliasOfFirst`、`AliasOfLast`。
*   第二階段的 `PawnContext`：性別、童年與成年背景標題、文化、異種族、年齡層。
*   回應 `NameResult`：三欄結果，以及每欄來源（Unchanged／SpecialForm／Alias／PersonalCache／Dictionary／Api／Failed）。

---

## 6. 翻譯管線

### 6.1 單一欄位解析順序（命中即停）

1.  空白 → 跳過。
2.  已含中文字 → 視為已中文化，不處理（例：官方已翻譯的帝國人名、混合名「Gisele 疤痕」中的中文欄）。
3.  **特殊字形規則**（先做 NFKC 正規化）：
    *   全大寫縮寫（2～4 字母）→ 保留原樣。
    *   單一字母 → 當名或姓時保留字母；當暱稱時寫成「阿」加字母。
4.  暱稱同名規則（6.3）。
5.  個人快取。
6.  內建字典。
7.  Gemini API：剩餘欄位集中成一次請求。

**本機命中也要清理：** 快取與字典以小寫鍵查詢，例如「Ad」會查到「AD」，但原文只有大寫 A。每次命中都以實際原文跑 `NameSanitizer`，不通過就當作沒命中，往下一層查。

**命名空間：** 名與姓共用 `name:`；暱稱用 `nick:`，查不到時不回退到 `name:`。

**鍵正規化：** `Trim` → NFC → 小寫，保留重音字母。

### 6.2 內建字典

**四層來源：**

| Tier | 來源 | 詞數 | 校對 |
| :-- | :--- | --: | :--- |
| 1 | 名字庫 | 1,654 | 已逐筆校對 |
| 2 | 固定角色 299 人＋稀有角色 9 人 | 624 | 已逐筆校對（Step 5） |
| 3 | 玩家貢獻名字 16,136 筆 | 21,769 | 抽查 150 筆，估計錯誤率 1～2% |
| 4 | DLC 清單（帝國、怪人、豬人、廢土人） | 549 | 已逐筆校對（Step 5；官方譯名原則上保留） |

**最終譯名的採用順序**（`Tools/merge_dictionary.py`）：
1.  `review_corrections.csv`：人工修正，170 筆（含 Step 5 校對的 100 筆）。已檢查、保留原譯的條目記在 `review_accepted.csv`。
2.  特殊字形規則：112 筆。
3.  `official_overrides.csv`：官方誤譯覆寫，45 筆。
4.  官方繁中譯名：309 筆。
5.  Gemini 譯名：23,895 筆（另有 30 筆是官方譯名被覆寫後改用 Gemini）。
6.  之後再做一次自動統一：暱稱若只是人名的拼法變體，就改用人名的譯法，共 66 筆。

**維護方式：**
*   **改譯名：** 編輯 `NameTable/translations/` 下的修正或覆寫清單，執行 `python -I Tools/merge_dictionary.py` 重新產生字典。
*   **遊戲改版：** 依序執行 `extract_vanilla_names.py`、`build_full_input.py`、`agy_translate.py`（可接續未完成的部分）、`merge_dictionary.py`。

**繁中遊戲內仍是英文的部分**（模組要處理的範圍）：
*   名字庫、固定角色、玩家貢獻名字。
*   Dirtmole 與怪人的「名」。
*   豬人與廢土人的暱稱。
*   帝國的拜占庭音節詞。

### 6.3 暱稱同名規則（C# 執行）

*   **開窗時判斷暱稱來源：**
    *   `NickSet == false`（暱稱欄空白）：依 `pawn.Name.Nick` 等於名或姓，判定為對應欄位的別名。
    *   暱稱等於名或姓（不分大小寫）：判定為別名。
    *   其他：獨立外號。
*   **翻譯時：**
    *   暱稱欄空白且為別名：填入對應欄位的中文，保持原本稱呼。設定「保留原版稱呼」關閉時維持空白。
    *   暱稱等於目前或開窗時的名／姓英文：輸出等於該欄的最終值，即使該欄已鎖定為中文。
    *   獨立外號：依 7.3 規則翻譯。

### 6.4 個人快取與學習

*   **檔案：** `GenFilePaths.ConfigFolderPath/RimTaiName_Cache.xml`，不放在 Mod 資料夾。
*   **檔名與 Mod 資料夾：** RimWorld 的 Mod 設定檔名是 `Mod_<資料夾名>_RimTaiNameMod.xml`，所以開發版（junction）與 Workshop 版的設定不互通。快取檔與金鑰檔用固定檔名，兩版共用。
*   **欄位：** namespace、key、value、origin（`api` 或 `user`）。
*   **存檔時機：** 背景執行緒只更新記憶體（加 lock）；存檔一律在主執行緒進行。
*   **學習：**
    *   API 結果收到即寫入，`origin = api`。
    *   玩家按「確定」時修改了模組給的譯名：寫入 `origin = user`，之後優先於字典與 API。
*   **設定頁：** 清除快取、開啟快取資料夾。

---

## 7. Gemini API 與 Prompt

### 7.1 模型與額度

*   **預設模型：** `gemini-3.5-flash-lite`；備選 `gemini-3.1-flash-lite`（預計 2027-05-07 停用）。設定頁可手填模型代號。
*   **免費額度：** 官方不公布數字；以專案計，太平洋時間午夜重置（臺灣時間 15:00 或 16:00）。設計上不依賴任何額度數字。
*   **用量控制：** 每次點擊最多 1 次請求；全域只允許 1 個請求在途；最短間隔預設 3 秒。
*   **隱私告知：** 名字會送往 Google；免費層的內容會被用於改善產品。Gemini API 條款要求提供給 EEA、瑞士、英國使用者的應用只能使用付費服務，需在說明中告知。API Key 的保存與防護見第 9 節。

### 7.2 請求規格

*   **端點：** `POST https://generativelanguage.googleapis.com/v1beta/interactions`
*   **Header：** `x-goog-api-key`。金鑰不放在 URL，也不寫入任何記錄檔。
*   **請求內容：** `model`；`system_instruction`（純字串，放翻譯規則）；`input`（三欄資料，JSON 文字）。
*   **固定參數：** `store: false`；`response_format` 為 `{"type":"text","mime_type":"application/json","schema":{…}}`，schema 中 `first`／`nick`／`last` 皆為必填字串。**注意：** schema 要包在 `response_format.schema` 裡，不是直接放在 `response_format`。
*   **不傳：** `thinking_budget`、`thinking_level`、`temperature`、`top_p`、`top_k`。flash-lite 預設 `thinking_level = minimal`；取樣參數已於 2026-07-21 棄用，未來的模型收到會回 400。
*   **讀取結果：** 從 `steps[]` 中找 `type == "model_output"` 的 step（前面可能有 `thought` step），串接其 `content[]` 中 `type == "text"` 的 `text`，再解析成 JSON。`status` 不是 `completed` 就視為失敗。
*   **錯誤格式：** `{"error":{"code":"<snake_case 字串>","message":…}}`，`code` 是字串，不可當數字解析；舊式 API 的錯誤則是數字 code 加上 `status`。兩種都要能讀。
*   **逾時與重試：** 逾時 60 秒；5xx 等 2 秒後重試 1 次；429 不重試，直接提示。原本是 15 秒，但免費層模型過載時會變慢而不是直接失敗（2026-10-21 實測 flash-lite 回一個字要 17 秒，所有請求都逾時），因此放寬。逾時與 5xx 的提示會說明「可能是模型太忙碌」並建議換模型，不寫成「連線逾時」，以免玩家誤以為是網路或金鑰的問題。
*   **測試連線：** 送出一次極小的 Interactions 請求，一次驗證金鑰、模型、權限與額度。不使用 `GET models/{model}`：它的錯誤格式不同，而且回 200 也不代表能生成或還有額度。代價是每次用掉 1 次請求額度。
*   **欄位細節：** Step 3 開工前再以官方 API reference 確認一次。

### 7.3 翻譯規則

*   **單一來源：** 規則文字放在 `Prompts/NameRules_zhTW.txt`。C# 的 `PromptBuilder` 與 `Tools/name_rules.py` 都讀這個檔案，兩邊各自補上自己的輸入輸出格式。
*   **規則重點：**
    *   名與姓採臺灣慣用音譯，避免中國大陸譯法；俄文名不用「季」對應 di。
    *   單字譯名不得是易誤讀的一般詞；避免知名鬼怪或角色聯想。
    *   東亞人名用漢字。
    *   暱稱意譯優先，要像「別人會這樣叫他」的名詞，不用形容詞或「XX者」直譯；人名簡稱用音譯並與名字一致。
    *   輸出只能是繁體中文；原文中的大寫字母可保留；不得有標點、空白、數字；名、姓 ≤12 字，暱稱 ≤16 字。

### 7.4 第二階段生成規則

*   **風格：** 只生成外文名的音譯／意譯風格。模型先依性別、文化、異種族構想外文名，再依 7.3 轉成中文。
*   **暱稱：** 依背景故事與特質生成意譯外號，或設為名／姓的別名。
*   **鎖定欄位：** 不變，並作為風格參考。
*   **上下文：** 只傳 `PawnContext` 列出的欄位。

---

## 8. 安全與邊界條件

1.  **執行緒：** 背景執行緒只產生純資料。所有遊戲 API（`Messages`、`Find.*`、`Translate()`、寫入 `current`、存檔）只在主執行緒呼叫。視窗關閉後才到達的結果只寫入快取。
2.  **視窗狀態：**
    *   用 `ConditionalWeakTable` 綁定每個視窗的狀態。
    *   每個請求帶遞增的 `RequestId`，套用前確認視窗仍開著、且是最新請求。
3.  **雙重鎖定：** Prompt 告知已鎖定欄位；C# 寫入前也一律檢查鎖頭。
4.  **輸出清理（`NameSanitizer`）：**
    *   NFKC 正規化 → `Trim` → 移除非法字元。
    *   只允許 U+3400–4DBF、U+4E00–9FFF 的漢字，以及原文中出現的大寫字母。這也排除了原版會拒絕的 `·`、`・` 與 CJK 擴充 B 以後的字。
    *   結果為空或超長則該欄失敗，不截斷。
    *   最後再用 `ValidNameRegex` 驗證一次。
5.  **反射：** 啟動時以 `AccessTools` 建立存取委派；不使用 Publicizer。
6.  **錯誤提示：**

| 情況 | 提示內容 |
| :--- | :--- |
| 未設定 API Key | 原版名字仍可用內建字典翻譯 |
| 400 | 請求錯誤，附上 `message` 前 200 字（先遮蔽金鑰） |
| 401，或 400 且訊息提到 API key | 金鑰無效 |
| 402 | 需要付費額度 |
| 403 | 無權使用此模型，或金鑰受限 |
| 404 | 模型代號不存在 |
| 429 `quota_exceeded` | 今日額度已用完，附上重置時間（太平洋時間午夜換算成本機時間） |
| 其他 429（如 `rate_limit_exceeded`） | 請求太頻繁，稍後再試 |
| 5xx、逾時 | 模型可能太忙碌，稍後再試或在設定換一個模型 |
| 斷線（DNS、TLS、連線失敗） | 檢查網路 |

7.  **焦點：** 實測證明保留焦點時寫入也能正確顯示；仍保留先 `UnfocusCurrentTextField()` 作為保險。

---

## 9. API Key 防護

**原則：** 金鑰只存在玩家自己的電腦。只有在玩家按下「翻譯姓名」或「測試連線」時，才會以 HTTPS 送往 Google 的 Gemini 端點。除此之外，金鑰不會出現在任何檔案、記錄、畫面或網路請求中。

| 外洩管道 | 防護 |
| :--- | :--- |
| 設定檔被分享 | 金鑰**不寫入** RimWorld 的 Mod 設定 XML，另存為 `Config/RimTaiName_ApiKey.dat`，並以 Windows DPAPI（`ProtectedData`，目前使用者範圍）加密。**非 Windows 平台一律**改為「只在本次遊戲期間記憶」，並在設定頁說明：Mono 在 macOS／Linux 會靜默改用防護較弱的 ManagedProtection，不會丟例外，所以不能靠「加解密是否成功」來判斷。解密失敗（`CryptographicException`，例如設定檔是從別台電腦複製來的）時視為沒有金鑰，並提示重新輸入 |
| 記錄檔外流（回報問題時附上 `Player.log`、HugsLib 的記錄上傳） | 任何記錄都不輸出金鑰、請求標頭或完整請求內容。所有記錄先經 `KeyRedactor` 遮蔽：以已知金鑰的完整值為主，樣式比對為備援（`AIza[0-9A-Za-z_-]{35}`，以及 2026-05 起 AI Studio 新發的 `AQ.` 開頭金鑰 `AQ\.[0-9A-Za-z_.-]{20,}`），一律換成「前綴…末 4 碼」 |
| 畫面被看到（直播、截圖） | 輸入欄用 `GUI.PasswordField` 遮罩；已儲存的金鑰只顯示「前綴…末 4 碼」；提供「貼上」按鈕，從剪貼簿直接存入、不經過畫面；按「顯示」才露出完整金鑰，關閉設定頁即恢復遮罩 |
| 金鑰被送到別處 | 端點寫死為 `https://generativelanguage.googleapis.com`。模型代號只允許 `^[a-z0-9]+(?:[.-][a-z0-9]+)*$`（不允許 `/`、`?`、`..`），防止藉模型名稱改寫網址。關閉自動轉址（`AllowAutoRedirect = false`），避免金鑰跟著轉址送到其他網域。不覆寫 TLS 憑證驗證 |
| 金鑰出現在網址 | 只放在 `x-goog-api-key` 標頭，絕不放進網址 |
| 金鑰進入 Prompt | 送給模型的內容只有姓名與背景，模型無從回傳金鑰 |
| 其他 Mod 讀取 | 金鑰不放在公開的靜態欄位或屬性。能讀取明文的只有三處：`ApiKeyStore` 內部（含供記錄遮蔽用的 `ApiKeyStore.Redact`）、`GeminiInteractionsClient`（透過注入的 `Func<string>`，送出請求時才取用，用完即丟）、設定頁按「顯示」時。**限制：** 同一個遊戲行程中的惡意 Mod 理論上仍能讀取記憶體，這無法完全防止，會在說明中如實告知 |
| 存檔 | 不寫入存檔（本模組本來就不碰存檔） |
| 開發者誤上傳 | 金鑰存在 Config 資料夾，不在 Mod 資料夾。打包腳本會掃描 `AIza[0-9A-Za-z_-]{35}` 與 `AQ\.[0-9A-Za-z_.-]{20,}`，發現就中止打包 |
| 背景連線 | 移除驗證版「啟動時自動連線」的測試。只有玩家按下按鈕才會連線，不做任何統計或回報 |

**給玩家的建議**（寫在設定頁與 Workshop 說明）：
*   在 Google AI Studio 為本模組建立一把專用金鑰。2026-05 起新發的金鑰（`AQ.` 開頭）預設就只能呼叫 Gemini API；舊式 `AIza` 金鑰請在 Google Cloud 主控台限制為只能呼叫 Generative Language API。
*   建在沒有啟用帳單的專案上。這樣只能用免費額度，即使金鑰外洩也不會產生費用，只是額度可能被別人用掉。
*   懷疑外洩時，到 AI Studio 刪除金鑰並重建，再在設定頁按「清除金鑰」。
*   免費層送出的內容會用於改善 Google 產品；EEA、瑞士、英國地區依條款只能使用付費服務。
*   「測試連線」會用掉 1 次請求額度。

**驗證項目：**
*   `KeyRedactor` 的單元測試，涵蓋 `AIza` 與 `AQ.` 兩種格式。
*   實機測試：輸入真實金鑰並翻譯，再故意製造錯誤（錯誤金鑰、斷網、不存在的模型），之後搜尋 `Player.log` 與 Config 資料夾內所有檔案，都不得出現金鑰明文。
*   確認 Mod 設定 XML 不含金鑰，且 `RimTaiName_ApiKey.dat` 不是明文。
*   用 `AIza` 與 `AQ.` 兩種假金鑰測試打包腳本，都必須中止。
*   程式碼審查：確認只有上表「其他 Mod 讀取」列出的三處會讀取金鑰明文。

---

## 10. 建置、測試環境與發佈

**建置：**
*   .NET 10 SDK，`TargetFramework net472`。
*   設定 `NoStdLib`、`DisableImplicitFrameworkReferences`、`AutomaticallyUseReferenceAssemblyPackages=false`、`FrameworkPathOverride=<遊戲 Managed 資料夾>`。
*   引用遊戲 `Managed/` 下的 mscorlib、System、System.Net.Http、Assembly-CSharp、UnityEngine 各模組，以及 Harmony 模組的 `0Harmony.dll`，全部設 `Private=false`。
*   輸出到 `1.6/Assemblies/`。

**測試環境：**
*   **開發機：** `D:\SteamLibrary`，Mod 以 junction 連到專案資料夾。
*   **測試機：** 另一台電腦，RimWorld 位於 `E:\`，載入 386 個 Mod，Harmony 版本為 2.4.2。開發機編譯時引用的 Harmony 是 2.4.3，因此只使用 2.4.2 已有的 API。
*   **回傳記錄檔：** 測試後把 `Player.log` 放到專案的 `TestLog/`。

**打包：**
*   規劃中的 `Tools/package_mod.ps1` 只複製 `About`、`LoadFolders.xml`、`1.6`、`Languages`、`Dictionary`、`Prompts` 到 `dist/RimTaiName/`。
*   用途是傳到測試機，以及日後上傳 Workshop。

**1.7 對策：**
*   只用 .NET 標準函式庫。
*   UI Patch 層與管線層分離。
*   MVP 完成後在 unstable 分支實測，通過後才加進 `supportedVersions`。

---

## 11. 實作路線圖（剩餘步驟）

### Step 0 收尾
*   **新生兒命名視窗：** 確認鎖頭與工具列的位置，以及姓欄的「建議...」不受影響。
*   **程式修改：** 鎖頭改用 `NewCol(24f, HorizontalJustification.Right)` 靠右對齊。
*   **驗證：** 開發模式＋上帝模式下開啟改名視窗即為新生兒版（4.2），不必等嬰兒出生。測三種情況：
    *   有具名父母的小人：姓欄有「建議...」。
    *   沒有具名父母的小人：姓欄沒有按鈕。
    *   關閉上帝模式後的成年殖民者。

    與 Step 1 一起送測試機，並回傳 `Player.log`。

### Step 1：基礎建設、設定頁、API Key 與 Gemini 傳輸層
*   **檔案：**
    *   `Core/RimTaiNameSettings.cs`：模型代號、最短間隔、「保留原版稱呼」。**不含金鑰。**
    *   `Core/ApiKeyStore.cs`：金鑰的加密儲存、讀取、清除。Windows 用 DPAPI；其他平台一律只在本次遊戲期間記憶（第 9 節）。
    *   `Core/KeyRedactor.cs`、`Core/ModLog.cs`：寫入記錄前遮蔽金鑰。
    *   `RimTaiNameMod.DoSettingsWindowContents`：設定頁，包含
        *   API Key 欄位：`PasswordField` 遮罩、顯示切換、貼上、清除。
        *   「測試連線」按鈕。
        *   模型代號（`Widgets.TextField`，上限 64 字，以正規表示式驗證）。
        *   隱私與金鑰安全說明（第 9 節的建議）。
    *   `Pipeline/PersonalNameCache.cs`。
    *   `Llm/MiniJson.cs`、`Llm/GeminiInteractionsClient.cs`（傳輸層、錯誤分類、測試連線）、`Llm/QuotaResetClock.cs`。
    *   `Source/RimTaiName.Tests`：xUnit 測試專案（建立前先徵求同意下載 NuGet 套件）。
    *   `Tools/package_mod.ps1`：含金鑰掃描；不存在的資料夾直接跳過。
*   **不重用驗證版：** 直接把「測試連線」寫在正式的 `GeminiInteractionsClient`（7.2），刪除 `Spike/`，也**移除啟動時自動連線**。
*   **驗證：**
    *   設定可存可讀；本機與測試機的設定頁都顯示 DPAPI。
    *   「測試連線」能分辨：金鑰正確、金鑰錯誤、模型不存在。
    *   快取檔出現在 Config 資料夾。
    *   `dotnet test` 全數通過。
    *   打包出的資料夾在測試機能直接載入。
    *   第 9 節的金鑰驗證項目全部通過。

### Step 2：翻譯管線（純 C#）＋ 單元測試
*   **檔案：** `Pipeline/` 下的 `NameKey`、`SpecialFormRule`、`NickAliasResolver`、`NameSanitizer`、`BuiltInDictionary`、`NameProcessorService`。
*   **重用：** `SpecialFormRule` 與 `NameSanitizer` 的規則要與 `Tools/name_rules.py` 的 `special_form()`、`latin_ok()`、`check()` 完全一致。
*   **測試專案 `Source/RimTaiName.Tests`**（Step 1 建立）：xUnit，`net10.0`，以連結方式編譯 `Pipeline/`、`Llm/` 與 `Core/KeyRedactor.cs`。
*   **測試內容：**
    *   暱稱的四種來源與空白暱稱。
    *   各種鎖定組合。
    *   非法字元、超長、拉丁字母規則。
    *   `name:` 與 `nick:` 命名空間隔離。
    *   本機命中值與原文大小寫不符時（「Ad」查到「AD」）視為沒命中。
    *   **與 Python 一致性：** 從字典匯出測試資料，確認 C# 的特殊字形與檢查結果和 Python 相同。
*   **驗證：** `dotnet test` 全數通過；`dotnet build` 主專案仍 0 警告、0 錯誤。

### Step 3：Gemini 翻譯請求
*   **範圍：** 傳輸層、錯誤分類與金鑰防護已在 Step 1 完成，這一步只做翻譯本身。
*   **檔案：**
    *   `Prompts/NameRules_zhTW.txt`：從 `name_rules.py` 移出規則文字；`name_rules.py` 改為讀取它，組出的字串須與移出前逐字相同。
    *   `Llm/PromptBuilder.cs`：`system_instruction`、`input` 與 `response_format`（7.2 的格式）。
    *   `GeminiInteractionsClient.TranslateAsync`（實作 `ILlmClient`）。
    *   `Llm/RequestGate.cs`：全域 1 個請求在途、最短間隔。
*   **驗證：**
    *   單元測試：Prompt 組裝、回應解析（非 JSON、缺欄位）、`RequestGate`。
    *   遊戲內用你的金鑰實際翻譯一次。
    *   故意製造錯誤後，全文搜尋 `Player.log` 與 Config 資料夾，確認沒有金鑰明文。

### Step 4：命名視窗正式版
*   **檔案：** 改寫 `UI_Patches/`：
    *   移除測試按鈕，改成「翻譯姓名」與「還原」。
    *   加入非同步請求、`RequestId`、暫停點外關閉、偵測「確定」並學習。
*   **驗證（手動測試）：**
    *   成年殖民者、新生兒、開發模式＋上帝模式。
    *   動物與機械體的視窗沒有模組 UI。
    *   等待中按 ESC、點視窗外、按 Enter。
    *   全鎖定、部分鎖定、還原。
    *   沒有 API Key 時只靠字典。
    *   全形字母（ＭＪ）與帶重音的名字（André），確認遊戲內的 Unicode 正規化正常。
    *   在測試機的 386 個 Mod 環境下正常運作。

### Step 5：字典 v1.0
*   逐筆校對 Tier 2（624 詞）與 Tier 4（549 詞）。
*   從字典挑選 Prompt 的範例詞。
*   以 `merge_dictionary.py` 重新產生字典。
*   **驗證：** `review_queue.csv` 中的第 1、2、4 層都已處理。

### Step 6：第二階段（生成中文名）
*   **檔案：** `PawnContext` 擷取、生成用 Prompt、`ProcessMode.Generate`、工具列的「生成中文名」按鈕。
*   **驗證：** 不同性別、文化、異種族各生成數次，確認風格、鎖定欄位與長度限制。

### Step 7：上架 Steam 創意工坊
*   決定正式的 `packageId` 與作者名稱，取代 `RimTaiName.Dev`。
*   製作 `Preview.png`。
*   撰寫 Workshop 說明：
    *   功能介紹，並說明「不需金鑰即可翻譯原版名字」。
    *   API Key 申請教學（新金鑰以 `AQ.` 開頭），以及第 9 節的金鑰安全建議。
    *   隱私與額度說明，包括 EEA、瑞士、英國的條款限制。
    *   開發版與 Workshop 版的設定不互通，但金鑰與快取共用。
*   以 `Tools/package_mod.ps1` 打包，金鑰掃描必須通過後才上傳。
*   1.7 unstable 實測通過後更新 `supportedVersions` 與 `LoadFolders.xml`。

---

## 附錄 A：修訂歷史

**v1 → v2（2026-10-14）：**
*   **模型與 API：** 預設模型改為 3.5 Flash-Lite；不寫死免費額度；改用 thinking_level；改用 Interactions API。
*   **遊戲機制：** 欄位名稱是 `current`；成年視窗有四列；處理暱稱空白；寫入前必須清理；限定人類＋`NameTriple`；改變版面注入方式。
*   **架構：** 新增內建字典與命名空間；改用 `HttpClient`；快取改用 XML；確定第二階段的風格；補齊目錄結構。

**v2 → v3（2026-10-16）：**
*   以「現況 → 剩餘步驟」重新組織。
*   新增第 9 節「API Key 防護」與設定頁的金鑰欄位；階段目標加入「上架 Steam 創意工坊」。
*   補上字典 v0.1、特殊字形規則、官方優先政策，以及 Step 0 驗證結果。
*   建置改為直接引用遊戲 DLL；測試採 xUnit；翻譯規則改為 C# 與 Python 共用一份檔案。
*   新增打包腳本與測試機流程。

**實作計畫重審（2026-10-16）：** 以本機 1.6.4871 的反射與 IL、以及 Gemini 官方文件（10-16 版）重新查證後修正：
*   **遊戲：**
    *   開發模式＋上帝模式會開出新生兒版視窗，Step 0 收尾改用它測試。
    *   姓欄「建議」清單為空時不畫按鈕，鎖頭改為靠右對齊。
    *   `BuildName` 每次建立新物件，以 `ReferenceEquals` 偵測「確定」。
    *   `ValidNameRegex` 會擋 `·` 與 CJK 擴充 B 以後的字。
    *   設定檔名與 Mod 資料夾名稱相關。
*   **Gemini：**
    *   `response_format` 改為 `{type, mime_type, schema}` 結構。
    *   規則放 `system_instruction`。
    *   Interactions 的錯誤 code 是字串；429 區分 `quota_exceeded` 與 `rate_limit_exceeded`。
    *   測試連線改用一次極小的 Interactions 請求。
*   **API Key：**
    *   新金鑰為 `AQ.` 開頭，遮蔽與打包掃描都要涵蓋。
    *   模型代號正規表示式改為 `^[a-z0-9]+(?:[.-][a-z0-9]+)*$`。
    *   非 Windows 一律只在本次遊戲期間記憶；解密失敗時視為沒有金鑰。
    *   可讀取金鑰明文的地方限定為三處。
    *   給玩家的建議加入條款與資料使用說明。
*   **管線：** 本機命中值也要以實際原文跑 `NameSanitizer`。
*   **路線圖：** Gemini 傳輸層與測試專案移到 Step 1，Step 3 只做翻譯請求。

## 附錄 B：相關文件

*   `NameTable/README.md`：原版人名來源與分類。
*   `NameTable/translations/README.md`：字典翻譯紀錄與品質。
*   `NameTable/samples/REVIEW.md`：樣本檢查與平行化實測。

## 附錄 C：參考資料

*   **Gemini：**
    *   停用時程：https://ai.google.dev/gemini-api/docs/deprecations
    *   更新紀錄：https://ai.google.dev/gemini-api/docs/changelog
    *   速率限制：https://ai.google.dev/gemini-api/docs/rate-limits
    *   定價：https://ai.google.dev/gemini-api/docs/pricing
    *   Interactions API：https://ai.google.dev/gemini-api/docs/interactions
    *   API reference：https://ai.google.dev/api/interactions-api
*   **Antigravity 子代理：** https://antigravity.google/docs/subagents/
*   **RimWorld 1.7 公告：** https://ludeon.com/blog/2026/10/announcing-the-waylith-engine-and-update-1-7/
*   **RimWorld 1.6 反編譯原始碼：** https://github.com/Dyyrlysh/RimworldDecompile
