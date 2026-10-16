# RimTaiName - RimWorld 智慧姓名翻譯與生成模組開發計畫書 v2

*   **文件版本**：v2（2026-10-14）
*   **遊戲機制驗證基準**：RimWorld **1.6.4871 rev590**（本機安裝，以反射核對 `Assembly-CSharp.dll` 成員簽章，並對照 1.6 反編譯原始碼）
*   **API 驗證基準**：Google Gemini API 官方文件（2026 年 10 月版）

---

## 0. v2 修訂摘要

| # | v1 內容 | v2 修正 | 原因 |
| :-- | :--- | :--- | :--- |
| 1 | 預設模型 `gemini-2.5-flash-lite` | 預設改為 **`gemini-3.5-flash-lite`** | 2.5 系列自 2026-09-18 起僅開放給曾使用過的帳號，新玩家無法使用 |
| 2 | 免費額度「1,500 次/日、15~30 次/分」 | 不寫死數字，改為容錯設計 | 官方已不公布免費層數字，第三方資料從 500 到 1,500 次/日不等 |
| 3 | `thinkingBudget: 0` | 不傳 thinking 參數（Flash-Lite 預設 `minimal`）；也不傳 `temperature` | 3.x 改用 `thinking_level`，且官方說明無法完全關閉思考；`temperature` 等取樣參數已於 2026-07-21 列為 deprecated |
| 4 | 未指定 API 介面 | 採用 **Interactions API** 並加 `store: false`；金鑰放 header | `generateContent` 已被官方標為 legacy；Interactions 預設會在伺服器保存請求紀錄 |
| 5 | 反射存取 `NameContext.curName` | 欄位實際名稱為 **`current`** | 依 1.6.4871 反射結果 |
| 6 | 成年小人視窗為三列 | 實際為**四列**（多一列頭銜 `BackstoryTitle`） | 頭銜列不加鎖頭、不送翻譯 |
| 7 | 同名規則以文字框內容判斷 | 改以開窗當下的 `pawn.Name` 判斷，並處理**暱稱空白**的情形 | `NickSet == false` 時暱稱欄是空的，稱呼由名/姓的雜湊值決定，翻譯後可能改變 |
| 8 | 直接寫入 AI 結果 | 寫入前必須依原版規則**清理與驗證** | 原版只在玩家打字時檢查字元與長度，程式寫入會繞過檢查，非法字元會直接寫進存檔 |
| 9 | 攔截 `NamePawnDialog` | 改為修改 `Dialog_NamePawn` 建構子的參數，並限定人類＋`NameTriple` | 動物與機械體也共用此視窗（`NameSingle`） |
| 10 | 以 `ref Rect` 預留右側 36px、底部 42px | 鎖頭改為在 `MakeRow` 之後繪製（加寬 41px）；工具列以 Transpiler 插入 | 原版每幀依內容自動調整視窗高度，照 v1 做法工具列會出現在「確定」鈕**下方** |
| 11 | 只有 API ＋快取 | 新增**內建離線字典**，管線改為三層 | 原版人名庫是固定的一組名字，可預先翻譯；多數名字不需 API Key |
| 12 | 快取以英文單字為鍵 | 鍵加上命名空間（`name:` 音譯 / `nick:` 意譯） | 同一個字當姓與當暱稱的譯法不同（Fox → 福克斯 / 狐狸） |
| 13 | `Task.Run` ＋未指定 HTTP 函式庫 | 指定用 `HttpClient`，禁用 `UnityWebRequest`；所有遊戲 API 只在主執行緒呼叫 | `UnityWebRequest` 只能在主執行緒使用；RimWorld 1.7 正改用非 Unity 的 Waylith 引擎 |
| 14 | JSON 快取 | 個人快取改存 XML；回應解析用內嵌的精簡 JSON parser | 遊戲未附 Newtonsoft.Json，`JsonUtility` 不支援 Dictionary |
| 15 | Phase 2 生成方式未定 | 確定為**音譯與意譯風格**的名字，不生成「姓在前」的華人姓名 | 原版一律以「名 姓」顯示 |
| 16 | 目錄結構 | 補上 `LoadFolders.xml`、版本資料夾、英文後備語言包、字典與開發工具 | 發佈與版本相容需求 |

---

## 1. 專案概述與核心目標

本專案（代號：**RimTaiName**）旨在為《RimWorld》（邊緣世界）開發一款**極低侵入性、100% 相容原版機制**的角色命名輔助模組。模組擴充原版命名視窗（`Dialog_NamePawn`），透過**三層翻譯管線**（玩家個人快取 → 內建離線字典 → Google Gemini API），讓玩家一鍵將小人的英文姓名轉換為符合臺灣繁體中文習慣的譯名。第二階段再支援依角色背景與鎖定欄位，自動生成**音譯與意譯風格**的中文名字。

### 1.1 階段開發里程碑
*   **第一階段（MVP - 最小可行性產品）**：
    *   對**人類且姓名為三欄格式（`NameTriple`）**的小人，解鎖角色資訊欄右上角「重新命名」視窗中「名、姓」兩欄的編輯權限（原版成年小人只能改暱稱與頭銜）。
    *   保留原版「先點隨機抽英文、再決定是否翻譯」的操作習慣。
    *   實作名、暱稱、姓三列的鎖頭開關（頭銜列不處理），以及底部「翻譯姓名」與「還原」按鈕。
    *   三層翻譯管線：個人快取（含從玩家手動修正中學習）、內建離線字典、Gemini API（姓名音譯，暱稱意譯優先）。
*   **第二階段（Phase 2 - 擴充功能）**：
    *   啟用預留的「生成中文名」接口，讀取角色性別、背景故事、文化（Ideology）與異種族（Biotech）資訊。
    *   生成的名字一律為**外文名的中文音譯／意譯風格**（例：「艾蓮娜 哈特曼」、暱稱「鷹眼」），維持原版「名 姓」順序，不生成「陳大文」這類姓在前的華人姓名。
    *   支援「固定已鎖定欄位，由 AI 搭配生成其餘未鎖定欄位」的進階玩法。
*   **後續可能擴充（未排程）**：開局選人畫面（`CharacterCardUtility` 內可直接編輯三欄）也加入翻譯按鈕。

### 1.2 目標遊戲版本
*   **主要目標**：RimWorld **1.6**（目前 Steam 主線，本機版本 1.6.4871）。
*   **相容性測試目標**：RimWorld **1.7**。1.7 於 2026-10-12 進入 Steam unstable 分支，引擎由 Unity 換成 Ludeon 自家的 **Waylith**。官方表示大多數 Mod 不需修改，但未說明 Harmony、IMGUI 與網路 API 的細節。詳見第 8 節。

---

## 2. 遊戲底層機制整合規範（已依 1.6.4871 核對）

為確保不破壞原版存檔結構、社交文本與《Biotech》生育繼承邏輯，**RimTaiName** 嚴格遵循以下原生規範：

| 項目 | 原版實際行為（已核對） | RimTaiName 設計規範 |
| :--- | :--- | :--- |
| **適用對象** | `PawnNamingUtility.NamePawnDialog` 同時被人類、動物、機械體使用；動物與機械體的名字是 `NameSingle`（單一欄位） | 僅在 `pawn.RaceProps.Humanlike && pawn.Name is NameTriple` 時啟用模組功能，其他情況完全不介入 |
| **姓名資料結構** | `NameTriple(First, Nick, Last)` 三欄獨立 | 維持原格式，不在任何欄位加入特殊符號。新生兒可直接從「建議...」選單選用父母已中文化的姓；婚姻冠姓也照常運作 |
| **顯示順序** | `ToStringFull` 一律輸出「`First Last`」（中間為半形空白） | 所有譯名與生成名都採外文名的音譯／意譯風格，與「名 姓」順序一致 |
| **隱藏引號規則** | `ToStringFull` 在 `Nick == First` **或** `Nick == Last` 時省略 `'暱稱'` | 暱稱屬於「同名」關係時，輸出必須與對應欄位的中文**完全相同**（見 5.3） |
| **暱稱為空的情形** | `NickSet == false` 時，`Nick` 依 `First`、`Last` 雜湊值的奇偶決定回傳名或姓；視窗中的暱稱欄顯示為**空白** | 不能看文字框內容，要從開窗時的 `pawn.Name` 判斷稱呼來源；預設把對應欄位的中文填入暱稱，避免翻譯後稱呼從「米勒」變成「吉賽兒」（見 5.3） |
| **字元與長度驗證** | `CharacterCardUtility.ValidNameRegex = ^[\p{L}0-9 '\-.]*$`；長度上限：名 12、暱稱 16、姓 12、頭銜 16。**只在玩家打字時檢查**。按「確定」時只做 `Trim()` 與 `IsValid`（名、姓不可空白） | 程式寫入前必須自行清理與驗證（見 7.4）。中文字屬於 `\p{L}`，可通過驗證；間隔號「·」不屬於，**禁止出現** |
| **零存檔污染** | — | 不新增 `ThingComp`、`WorldComponent`、`GameComponent`（這些都會寫入存檔），只在按下「確定」時由原版邏輯更新 `Pawn.Name`。中途加入或移除本模組都不影響存檔 |
| **UI 字型安全** | Unity IMGUI 舊版字型不支援 Emoji | 所有按鈕與提示只用純文字（語言包 Keyed XML）、原版 `Widgets` 元件或模組自帶的 PNG 圖示，**禁用 Emoji** |

### 2.1 原版命名視窗實際結構（`Verse.Dialog_NamePawn`）

*   **建構子**：`Dialog_NamePawn(Pawn pawn, NameFilter visibleNames, NameFilter editableNames, Dictionary<NameFilter, List<string>> suggestedNames, string initialFirstNameOverride = null, string initialNickNameOverride = null, string initialLastNameOverride = null, string initialTitleOverride = null)`
*   **`NameFilter`**（`[Flags]`）：`None = 0, First = 1, Nick = 2, Last = 4, Title = 8`
*   **`PawnNamingUtility.NamePawnDialog` 的權限設定**：

| 情境 | 可見欄位 | 可編輯欄位 | 備註 |
| :--- | :--- | :--- | :--- |
| 新生兒命名期限內（`babyNamingDeadline`）或開發者模式 | 名、暱稱、姓 | 名、暱稱、姓 | 姓欄有 `suggestedNames`（父母的姓），顯示「建議...」下拉選單，**沒有隨機鈕** |
| 一般成年小人 | 名、暱稱、姓、頭銜 | **僅暱稱、頭銜** | 本模組要解鎖名與姓 |

*   **開啟視窗的位置**：角色資訊卡的改名鈕（`CharacterCardUtility`）、嬰兒出生信件（`ChoiceLetter_BabyBirth`）、`Pawn` 的命名提示 tick，以及動物／機械體改名鈕（`RenameUIUtility`，本模組不處理）。
*   **私有成員**（反射存取）：
    *   `Dialog_NamePawn`：`pawn`（`Pawn`）、`names`（`List<NameContext>`）、`size`（`Vector2`，即 `InitialSize`）。
    *   `NameContext`（**private 巢狀類別**）：`current`（`string`，文字框內容）、`editable`、`nameIndex`（名 0／暱稱 1／姓 2／頭銜 -1）、`maximumNameLength`、`suggestedNames`、`textboxName`。
    *   `NameContext.MakeRow(Pawn pawn, float randomizeButtonWidth, TaggedString randomizeText, TaggedString suggestedText, ref RectDivider divider, ref string focusControlOverride)`：依序畫出「標籤 → 文字框 → 隨機／建議鈕」三欄，欄距 17px。
*   **隨機鈕**：呼叫 `PawnBioAndNameGenerator.GeneratePawnName(pawn, NameStyle.Full, ...)` 產生完整新名字，再取該列 `nameIndex` 的部分。名字來源依序為：Anomaly 怪人、Biotech 異種族命名器（以音節隨機組合，無法窮舉）、PawnKind／背景故事命名器、Ideology 文化命名器，最後才是原版名字庫。
*   **版面與尺寸**：建構子依欄寬計算 `size.x`。`DoWindowContents` **每一幀**都用 `RectAggregator`（struct）累計內容高度，重設 `size.y` 後呼叫 `SetInitialSizeAndPosition()`，所以視窗高度會自動貼合內容。
*   **確定按鈕**：`BuildName()` 對三欄 `Trim()` 後建立 `NameTriple`，檢查 `IsValid`，再寫入 `pawn.Name` 與 `pawn.story.Title`，接著關閉視窗。

---

## 3. 使用者介面（UI）與互動流程設計

### 3.1 視窗版面配置藍圖（擴充 `Dialog_NamePawn`）

成年小人（四列）：

```
+------------------------------------------------------------------------+
| [肖像]  重新命名此人                                             [性別] |
|         母親：…   父親：…                                                |
|------------------------------------------------------------------------|
| 名字：  [ Gisele              ]  [ 隨機 ]                      [ 鎖 ]   |
| 暱稱：  [ Fox                 ]  [ 隨機 ]                      [ 鎖 ]   |
| 姓氏：  [ Miller              ]  [ 隨機 ]                      [ 鎖 ]   |
| 頭銜：  [                     ]                         （不處理）     |
|------------------------------------------------------------------------|
| [ 翻譯姓名 ]    [ 生成中文名 (Phase 2) ]                      [ 還原 ]  |   ← Transpiler 插入的一列
|                                                                        |
| [ 取消 ]                                                     [ 確定 ]  |
+------------------------------------------------------------------------+
```

*   新生兒視窗沒有頭銜列，姓氏列的「隨機」換成原版「建議...」（父母的姓）。
*   MVP 階段不顯示「生成中文名」按鈕，只保留程式接口。

### 3.2 版面實作方式

| 元件 | 做法 | 說明 |
| :--- | :--- | :--- |
| 加寬視窗 | `Dialog_NamePawn` 建構子 **Postfix**：`size.x += 41f` | 欄距 17px ＋ 鎖頭 24px |
| 鎖頭 | `NameContext.MakeRow` **Postfix**（以 `AccessTools.Inner` 取得 private 巢狀類別）：在 `ref RectDivider divider` 剩下的空間 `divider.NewCol(24f)` 畫鎖頭 | 自動對齊每一列，不必計算 Y 座標。只對 `nameIndex` 為 0~2 且可編輯的列繪製。目前繪製中的視窗實例由 `DoWindowContents` 的 Prefix 設定、Finalizer 清除 |
| 工具列 | `DoWindowContents` **Transpiler**：以 `CodeMatcher` 找出 `ldloca <rectAggregator>` ＋ `ldc.r4 17.5` ＋ `call RectAggregator::NewRow`，在其前插入 `ldarg.0; ldloca <rectAggregator>; call Toolbar.Draw(Dialog_NamePawn, ref RectAggregator)` | 工具列以 `NewRow(35f)` 加入，原版的自動尺寸邏輯會讓視窗自己長高 |
| 失敗保護 | 找不到注入點時，記錄一次警告並停用本模組 UI | 原版視窗功能不受影響 |
| 鎖頭圖示 | 原版 `Widgets.Checkbox`，或模組自帶的 PNG（`Textures/`），附 Tooltip「鎖定：翻譯時不變更此欄」 | 不使用 Emoji |

### 3.3 標準使用者操作流程（User Flow）

```
[ 1. 開啟命名視窗 ]（角色面板右上角改名圖示／嬰兒出生信件）
        │
        ├─► 建構子 Prefix：若為人類＋NameTriple，editableNames |= First | Last
        ├─► 建構子 Postfix：建立 DialogState
        │     ├─ 原始姓名快照 OriginalName = pawn.Name（含 NickSet 與稱呼來源）
        │     ├─ 文字框初始值快照 InitialSnapshot（名、暱稱、姓）
        │     └─ 三個鎖頭預設為「未鎖定」
        │
[ 2. 抽籤與鎖定階段（可重複）]
        ├─► 點擊原版 [ 隨機 ] 抽取新的英文名字（新生兒的姓氏列為 [ 建議... ]）
        ├─► 直接在文字框打字修改
        └─► 對滿意的欄位點右側 [ 鎖 ]
        │
[ 3. 觸發處理 ]
        ├─► 點擊 [ 翻譯姓名 ]（MVP）或 [ 生成中文名 ]（Phase 2）
        ├─► 前置檢查：三欄全鎖定，或未鎖定欄位皆為空白／已是中文
        │     └─ 是：直接提示，不消耗 API 額度
        ├─► 本機解析（見 5.1）：暱稱同名規則 → 個人快取 → 內建字典
        │     └─ 全部解析完成：立即寫入未鎖定欄位，不消耗額度
        └─► 還有未知名字：
              ├─ 按鈕改為「處理中...」並停用（防連點）；暫時關閉「點視窗外即關閉」
              ├─ 記錄送出當下各欄文字（用來判斷等待期間玩家是否手動改過）
              ├─ 背景執行緒以 HttpClient 送出一次打包請求
              └─ 回應先清理與驗證，再交回主執行緒
        │
[ 4. 套用結果 ]（下一次 DoWindowContents Prefix，且 EventType.Layout 時）
        ├─► 先呼叫 UI.UnfocusCurrentTextField()，避免文字框保留舊內容
        └─► 只覆寫「未鎖定」且「等待期間沒被玩家改過」的欄位
        │
[ 5. 確認或還原 ]
        ├─► [ 還原 ]：未鎖定欄位恢復為 InitialSnapshot
        ├─► 原版 [ 確定 ]：原版驗證後寫入 Pawn.Name 並關閉視窗
        │     └─ DoWindowContents Postfix 偵測到視窗已關閉且 pawn.Name 已改變
        │          → 玩家若手動修正了模組給的譯名，把修正寫入個人快取（見 5.4）
        └─► [ 取消 ]／ESC：不做任何變更
```

---

## 4. 系統核心架構與模組職責劃分

### 4.1 專案目錄結構（命名空間：`RimTaiName`）

```
RimTaiName/
├── About/
│   ├── About.xml                         # packageId、supportedVersions(1.6)、modDependencies(brrainz.harmony)、loadAfter
│   └── Preview.png
├── LoadFolders.xml                       # <v1.6>: "/" + "1.6"；1.7 驗證後再加入
├── 1.6/
│   └── Assemblies/
│       └── RimTaiName.dll                # 不附帶 0Harmony.dll（依賴 Harmony 模組）
├── Languages/
│   ├── ChineseTraditional/Keyed/RimTaiName_Keys.xml
│   └── English/Keyed/RimTaiName_Keys.xml # 後備語系，避免其他語系顯示 key 名稱
├── Dictionary/
│   └── BaseNames_zhTW.tsv                # 內建離線字典（namespace \t key \t 譯名）
├── Textures/RimTaiName/                  # （選用）鎖頭圖示 PNG
└── Source/
    ├── RimTaiName/
    │   ├── RimTaiName.csproj             # net472、LangVersion latest、Krafs.Rimworld.Ref、Lib.Harmony(ExcludeAssets=runtime)
    │   ├── Core/
    │   │   ├── RimTaiNameMod.cs          # Mod 進入點、Harmony 初始化、反射委派建立
    │   │   ├── RimTaiNameSettings.cs     # API Key、模型、節流、選項
    │   │   ├── NameSlot.cs               # First / Nick / Last
    │   │   ├── NameRequestPayload.cs     # 請求 DTO（見 4.2）
    │   │   └── INameProcessorService.cs  # 統一處理介面（解耦 MVP 翻譯與 Phase 2 生成）
    │   ├── Pipeline/                     # 純 C#，不依賴遊戲，可單元測試
    │   │   ├── NameProcessorService.cs   # 管線協調：別名 → 快取 → 字典 → API → 清理
    │   │   ├── SpecialFormRule.cs        # 縮寫與單一字母（MJ、阿B），與 Tools/name_rules.py 的 special_form 一致
    │   │   ├── NickAliasResolver.cs      # 暱稱同名規則
    │   │   ├── NameSanitizer.cs          # 字元／長度／簡體字檢查
    │   │   ├── NameKey.cs                # 鍵正規化（Trim、NFC、小寫）
    │   │   ├── BuiltInDictionary.cs      # 讀取 Dictionary/BaseNames_zhTW.tsv
    │   │   └── PersonalNameCache.cs      # 個人快取（XML，執行緒安全）
    │   ├── Llm/
    │   │   ├── ILlmClient.cs             # 供應商抽象介面
    │   │   ├── GeminiInteractionsClient.cs # HttpClient、錯誤碼對應、逾時與重試
    │   │   ├── PromptBuilder.cs          # MVP 翻譯 Prompt 與 Phase 2 生成 Prompt
    │   │   └── MiniJson.cs               # 內嵌精簡 JSON parser（原始碼，置於自有命名空間）
    │   ├── UI_Patches/
    │   │   ├── DialogState.cs            # 快照、鎖頭、請求狀態、已套用譯名紀錄
    │   │   ├── DialogStateRegistry.cs    # ConditionalWeakTable<Dialog_NamePawn, DialogState>
    │   │   ├── Patch_Dialog_NamePawn_Ctor.cs           # Prefix 解鎖權限；Postfix 加寬、建立狀態
    │   │   ├── Patch_NameContext_MakeRow.cs            # Postfix 繪製鎖頭
    │   │   ├── Patch_Dialog_NamePawn_DoWindowContents.cs # Transpiler 工具列；Prefix 套用結果；Postfix 偵測確定
    │   │   └── ToolbarDrawer.cs
    └── RimTaiName.Tests/                 # 只引用 Pipeline/ 與 Llm/ 的純 C# 檔案，不需啟動遊戲

（以下為開發用資料，不隨模組發佈）
Tools/
├── unity_textassets.py                   # 讀取 Unity .assets 內的 TextAsset
└── extract_vanilla_names.py              # 離線抽取原版全部人名來源
NameTable/                                # 抽取結果：tokens.csv、full_names.csv、generator_lists.csv、README.md
```

### 4.2 統一資料傳輸契約（`NameRequestPayload`）

*   `ProcessMode Mode`：`Translate`（MVP 英翻中）或 `Generate`（Phase 2 生成）。
*   `SlotInput First / Nick / Last`：每欄包含
    *   `string Text`：目前文字框內容。
    *   `bool Locked`：鎖頭狀態。
    *   `string OriginalText`：開窗時的值（判斷同名規則用）。
*   `NickSource NickSource`：`Explicit`（獨立外號）、`AliasOfFirst`、`AliasOfLast`。開窗時由 `pawn.Name` 決定（見 5.3）。
*   `PawnContext Metadata`（Phase 2 專用）：
    *   `Gender`（性別）。
    *   `ChildhoodTitle` / `AdulthoodTitle`（背景故事標題）。
    *   `CultureLabel`（`pawn.Ideo?.culture`，Ideology 未啟用時為 null）。
    *   `XenotypeLabel`（`pawn.genes?.Xenotype`，Biotech）。
    *   `AgeStage`（兒童／成人）。
*   **回應 `NameResult`**：三欄最終字串，以及每欄來源（`Unchanged` / `Alias` / `PersonalCache` / `Dictionary` / `Api` / `Failed`），供 UI 提示與學習機制使用。

---

## 5. 翻譯管線：字典、快取與暱稱規則

### 5.1 單一欄位的解析順序

對每個**未鎖定**欄位依序判斷，命中即停止：

1.  **空白**：跳過。
2.  **已含中文字（CJK）**：視為已中文化，不處理。例：官方繁中語言包已翻譯的帝國人名。
3.  **特殊字形規則**（`SpecialFormRule`，不需 API；2026-10-15 決定）：先以 NFKC 正規化，把全形英數轉成半形，再判斷：
    *   全大寫縮寫（2～4 個字母，如 MJ、DJ、ET）：保留原樣。
    *   單一字母：當名或姓時保留字母本身（B）；當暱稱時寫成「阿」＋字母（阿B）。
    *   原版字典中共 112 個詞適用這條規則。另有約 20 個「字詞＋字母」的組合（如 Big D → 大D、Justin J → 賈斯汀J），已在字典中人工決定；字典以外的同類名字交給 API，並受 7.4 的字母規則約束。
4.  **暱稱同名規則**：只適用暱稱欄。等名或姓解析完成後，直接複製結果（見 5.3）。
5.  **個人快取**：玩家修正過的譯名，優先順序最高。
6.  **內建離線字典**。
7.  **Gemini API**：剩下的未知名字集中成一次請求。

**命名空間**：名與姓共用 `name:`（音譯，一個名字不論當名或姓譯法相同），暱稱用 `nick:`（意譯優先）。暱稱查不到時**不**回退到 `name:`，避免 `Fox` 被音譯成「福克斯」，而是交給 API 依規則 B／C 判斷。

**鍵正規化**：`Trim()` → Unicode NFC → `ToLowerInvariant()`。保留重音字母（如 `José`）。

### 5.2 內建離線字典

*   **涵蓋範圍**（已實際抽取，詳見 `NameTable/README.md`）：原版共有 **24,596 個不重複名字詞**（名／姓 17,762、暱稱 6,834），分為四層：

| Tier | 來源 | 詞數 | 說明 |
| :-- | :--- | --: | :--- |
| 1 | 名字庫（First_Male／First_Female／Last／Nick_*） | 1,654 | 最常出現；名有 British、Japanese、Russian、Tynan's custom 子分類 |
| 2 | 固定角色（299 人）與稀有角色（9 位 Ludeon 團隊成員） | 624 | 名字與背景故事整組固定 |
| 3 | 玩家貢獻名字（16,136 個完整姓名） | 21,769 | 長尾分布，18,450 詞只出現 1 次；標準小人約 37.5% 會用到 |
| 4 | DLC 專用清單（帝國名、怪人姓／暱稱、豬人／廢土人暱稱） | 549 | 其中 355 詞有官方繁中譯名可直接採用 |

*   **繁中遊戲已是中文的部分**：帝國名與姓、怪人的姓與暱稱、Impid／Yttakin 等異種族音節名、Corunan 文化名，在官方繁中介面下已經是中文，由 5.1 第 2 條自動跳過。
*   **繁中遊戲仍是英文的部分**：名字庫、固定角色、玩家貢獻名字、Dirtmole 與怪人的「名」、豬人與廢土人的暱稱、帝國的拜占庭音節詞。
*   **建置流程**（開發階段一次性作業）：
    1.  執行 `Tools/extract_vanilla_names.py`，從本機遊戲檔離線抽取，產出 `NameTable/tokens.csv` 等表格。不需啟動遊戲，遊戲改版後重新執行即可。
    2.  以 Gemini 批次翻譯**全部** 24,596 詞（每次 100 詞，約 250 次請求，使用 6.3 的規則），官方譯名優先採用。
    3.  **人工校對**依優先順序：Tier 1 → Tier 2 → 出現 ≥3 次的詞 → 其餘長尾（先接受機器翻譯，依玩家回報修正）。日文名需先決定採漢字或音譯。
    4.  輸出 `Dictionary/BaseNames_zhTW.tsv`，隨模組發佈。
*   **為什麼要翻全部**：只翻 Tier 1＋2 的話，玩家貢獻名字只有 4.9% 能整筆離線翻譯；翻全部後，原版名字都**不需 API Key**，API 額度只花在其他 Mod 的名字與玩家手打的名字。
*   **參考先例**：簡體中文社群的 PawnNameZH 模組即採用名字對照表（CSV）做法。

### 5.3 暱稱同名規則（由 C# 執行，不依賴模型）

**開窗時**從 `pawn.Name`（原始 `NameTriple`）決定 `NickSource`：

| 原始狀態 | 文字框顯示 | NickSource |
| :--- | :--- | :--- |
| `NickSet == false` | 暱稱欄**空白** | `pawn.Name.Nick` 等於名 → `AliasOfFirst`；等於姓 → `AliasOfLast` |
| `NickSet == true` 且暱稱與名相同（不分大小寫） | 與名相同 | `AliasOfFirst` |
| `NickSet == true` 且暱稱與姓相同（不分大小寫） | 與姓相同 | `AliasOfLast` |
| 其他 | 獨立外號 | `Explicit` |

**翻譯時**（暱稱欄未鎖定）：

*   暱稱欄**空白**，且 `NickSource` 為別名：預設把對應欄位的最終中文**填入暱稱**，保持原本稱呼不變。可在設定「保留原版稱呼」關閉，關閉則維持空白，由原版雜湊決定。
*   暱稱文字與**目前**或**開窗時**的名／姓英文相同：視為別名，輸出等於該欄的最終值。該欄可能已被玩家鎖定為中文，例如名已鎖成「吉賽兒」、暱稱仍是 `Gisele`，此時暱稱也輸出「吉賽兒」。
*   其他情況：視為獨立外號，依 6.3 規則 B／C 翻譯。

寫入後暱稱與名或姓完全相同，原版 `ToStringFull` 會自動顯示為「吉賽兒 米勒」，不會出現多餘的引號。

### 5.4 個人快取與自動學習

*   **位置**：`GenFilePaths.ConfigFolderPath` 下的 `RimTaiName_Cache.xml`，即 `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config`。**不放在 Mod 資料夾**，以免 Workshop 更新時被覆蓋。
*   **格式**：XML（`namespace`、`key`、`value`、`origin = api | user`）。
*   **執行緒安全**：記憶體中的字典以 `lock` 保護；背景執行緒只更新記憶體，**存檔一律在主執行緒進行**（視窗關閉時或遊戲結束時）。
*   **學習規則**：按下「確定」後，比對每個由模組寫入的欄位：
    *   最終值與模組給的譯名不同、是合法中文，且知道原文：寫入 `origin = user`，之後優先於字典與 API 結果。
    *   API 的結果在收到時即以 `origin = api` 寫入，讓視窗被關閉也不浪費額度。
*   **設定頁**：「清除個人快取」、「開啟快取資料夾」。

---

## 6. Gemini API 規格與 Prompt 設計

### 6.1 模型與額度策略

*   **預設模型**：**`gemini-3.5-flash-lite`**（2026-07-21 GA，尚未公布停用日，官方推薦新專案使用）。
    *   備選：`gemini-3.1-flash-lite`（預計 2027-05-07 停用）。
    *   **不使用** 2.5 系列（自 2026-09-18 起僅限曾使用過的帳號）。
    *   設定頁開放手填模型代號，並提供「測試連線」按鈕：呼叫模型清單 API，一次驗證金鑰與模型代號。
*   **免費額度**：
    *   官方不再公布免費層的 RPM／RPD 數字，只說明可在 Google AI Studio 查看。額度以**專案**計（同專案的多把金鑰共用），每日額度於**太平洋時間午夜**重置，即臺灣時間 15:00（夏令時間）或 16:00。
    *   因此設計上**不依賴任何特定額度數字**。
*   **用量控制**：
    *   內建字典與個人快取先過濾，只把未知名字送出。
    *   每次點擊最多送出 **1 次**請求（未知欄位打包在一起）。
    *   全域同時只允許 1 個請求在途，並設最短請求間隔（預設 3 秒，可調）。
*   **隱私告知**：設定頁與 Workshop 說明需註明：
    *   名字（Phase 2 還有背景故事）會傳送給 Google；免費層的請求內容會被 Google 用於改善產品。
    *   API Key 以明文存在 RimWorld 的 Config 資料夾。

### 6.2 請求規格（Interactions API）

*   **Endpoint**：`POST https://generativelanguage.googleapis.com/v1beta/interactions`
*   **驗證**：Header `x-goog-api-key: <金鑰>`。**不要**把金鑰放在 URL query，以免例外訊息或記錄檔洩漏金鑰；任何記錄檔都不得輸出金鑰或完整 header。
*   **`store: false`**：Interactions API 預設會在伺服器保存請求紀錄（免費層保存 1 天），本模組不需要。
*   **不傳的參數**：
    *   `thinking_budget`：3.x 已改用 `thinking_level`。
    *   `temperature`、`top_p`、`top_k`：已列為 deprecated。
    *   `thinking_level`：Flash-Lite 預設為 `minimal`，不必傳。官方說明 3.x Flash／Flash-Lite **無法完全關閉思考**，而且部分模型（如 `gemini-3.8-flash`）傳入 `minimal` 會報錯。玩家改用其他模型時，依模型設定或乾脆不傳。
*   **結構化輸出**：以 `response_format` 附上 JSON Schema。不能只設 mime type，C# 端仍要做 7.4 的驗證。
*   **讀取結果**：取 `steps` 中 `type == "model_output"` 那一步，再取其 `content` 裡 `type == "text"` 的 `text`，最後解析成 JSON。
*   **供應商抽象**：`GeminiInteractionsClient` 實作 `ILlmClient`。日後要改回 `generateContent`（legacy，但官方表示仍完整支援）或接其他供應商，都只需替換一個類別。
*   **逾時與重試**：
    *   逾時 15 秒。
    *   `500`、`503` 等 2 秒後重試 1 次。
    *   `429` 不自動重試，直接提示玩家。
*   **延遲預期**：含網路往返約需數秒，UI 以「處理中...」狀態設計，不承諾秒回。

請求範例（MVP 翻譯，姓已鎖定）：

```json
{
  "model": "gemini-3.5-flash-lite",
  "store": false,
  "system_instruction": "（6.3 的翻譯規則與範例）",
  "input": "{\"mode\":\"translate\",\"first\":{\"text\":\"Gisele\",\"locked\":false},\"nick\":{\"text\":\"Fox\",\"locked\":false},\"last\":{\"text\":\"米勒\",\"locked\":true}}",
  "response_format": {
    "type": "text",
    "mime_type": "application/json",
    "schema": {
      "type": "object",
      "properties": {
        "first": { "type": "string" },
        "nick":  { "type": "string" },
        "last":  { "type": "string" }
      },
      "required": ["first", "nick", "last"]
    }
  }
}
```

預期回應內容（`text` 欄位）：

```json
{ "first": "吉賽兒", "nick": "狐狸", "last": "米勒" }
```

> 註：`response_format` 的欄位細節以實作當下的官方 API reference 為準；Google 在 2026 年調整過數次 API 介面。

### 6.3 MVP 翻譯規則（System Prompt）

1.  **`first`（名）與 `last`（姓）**：
    *   採用自然、符合**臺灣**繁體中文習慣的音譯，避免中國大陸譯法。例：`Gisele` → 吉賽兒、`Miller` → 米勒、`Bob` → 鮑伯（不是「鮑勃」）。
    *   部落複合字姓氏（如 `Greywind`）可依字根意譯成自然的複合姓氏（灰風），或採音譯。
    *   非英語來源的名字（Biotech 異種族音節名、Ideology 文化名等）一律音譯。
    *   **東亞人名用漢字表記**（2026-10-14 決定）：
        *   日文名（羅馬拼音）以常見漢字表記，例：Haruka → 遙、Takeshi → 武。有多種寫法時選最常見者，並一律使用繁體字形（桜 → 櫻、沢 → 澤）。
        *   中文拼音姓名用對應漢字，例：Wu → 吳、Huang → 黃。
        *   越南姓與韓國姓名用慣用漢字，例：Nguyen → 阮、Kim → 金（作為西方名時則依上下文音譯）。
2.  **`nick`（暱稱，意譯優先）**：
    *   **規則 A（同名同步）**：由 C# 處理（見 5.3），Prompt 只需告知「若 nick 與 first／last 相同，輸出相同譯名」作為保險。
    *   **規則 B（外號意譯優先）**：具象名詞、動物、自然現象、職業或特徵外號，採用符合《RimWorld》外號風格的意譯。例：`Fox` → 狐狸、`Snake` → 毒蛇、`Bear` → 大熊、`Doc` → 老醫、`Sparky` → 火花、`Red` → 紅髮、`Shadow` → 暗影。
    *   **規則 C（簡稱音譯保底）**：常見人名簡稱採臺灣慣用音譯。例：`Lena` → 蓮娜、`Bob` → 鮑伯、`Alex` → 艾力克斯。
3.  **鎖定與中文保留**：已鎖定或已是中文的欄位只作為上下文參考（協助搭配音韻），回傳時原字不變。
4.  **輸出限制**（同時由 7.4 程式端強制）：
    *   只能使用繁體中文字；唯一的例外是原文中的大寫縮寫字母，要保留成半形大寫（例：Big D → 大D、Lil E → 小E）。
    *   **不得包含**任何標點（包含「·」「•」「-」「'」「.」）、空白、數字，以及上述例外以外的拉丁字母。
    *   長度：名不超過 12 字、暱稱不超過 16 字、姓不超過 12 字；建議名與姓 2~5 字。
5.  **Few-shot 範例**：Prompt 內附 8~12 組臺灣慣用譯名範例，從內建字典中挑選有代表性的項目。

### 6.4 Phase 2 生成規則（音譯與意譯風格）

1.  **風格**：生成的名字一律是「外文名字的中文音譯／意譯」，不生成姓在前的華人姓名，與原版「名 姓」的顯示順序一致。
2.  **名與姓**：模型先依角色的性別、文化、異種族，在內部構想合適的外文名字，再依 6.3 規則音譯。部落、異種族等文化可採意譯複合名（例：灰風、石牙）。
3.  **暱稱**：依背景故事與角色特質產生意譯外號（例：背景為獵人 → 「鷹眼」），或依 5.3 設為名／姓的別名。
4.  **鎖定欄位**：保持不變，作為風格參考。例：姓已鎖定為「米勒」，就搭配西式名字的音譯。
5.  **上下文**：只傳送 4.2 `PawnContext` 列出的欄位，不傳送其他存檔資訊。
6.  **輸出**：沿用 6.2 的 Schema 與 6.3 第 4 點的限制。

---

## 7. 安全防呆與邊界條件防護機制

1.  **執行緒模型**：
    *   網路請求使用 `System.Net.Http.HttpClient`（RimWorld 1.6 的 `Managed/` 已內建 `System.Net.Http.dll`），在背景執行緒以 async 方式執行。整個模組共用一個靜態 `HttpClient` 實例。
    *   **禁止使用 `UnityWebRequest`**：它只能在主執行緒建立與操作，而且 1.7 正改用非 Unity 引擎。
    *   **所有遊戲 API**（`Messages.Message`、`Find.*`、`Translate()`、寫入 `NameContext.current`、存檔）都**只在主執行緒呼叫**。背景執行緒只產生純資料結果，放入執行緒安全佇列，由 `DoWindowContents` 的 Prefix 取出處理。
    *   視窗關閉後才到達的結果只寫入快取，錯誤直接捨棄（不再顯示訊息）。這樣就不必新增會寫入存檔的 `GameComponent`。
2.  **視窗狀態與存活檢查**：
    *   以 `ConditionalWeakTable<Dialog_NamePawn, DialogState>` 綁定狀態。視窗被回收時狀態自動釋放，不需要靜態的「目前視窗」欄位。
    *   每個請求帶遞增的 `RequestId`。套用前確認 `Find.WindowStack.IsOpen(dialog)`，且 `RequestId` 仍是最新的一筆；玩家在等待期間按了「還原」或再次觸發時，舊結果作廢。
    *   只覆寫「未鎖定」且「送出後沒被玩家手動改過」的欄位。
    *   請求進行中暫時把 `closeOnClickedOutside` 設為 `false`，結束後還原。
3.  **雙重鎖定防覆寫**：除了在 Prompt 告知已鎖定欄位，C# 端寫回前一律檢查 `!Locked[slot]`，杜絕模型幻覺竄改玩家鎖定的文字。
4.  **輸出清理與驗證（`NameSanitizer`）**：
    *   Unicode NFKC 正規化（全形英數轉成半形）→ `Trim()` → 移除「·」等不符合 `ValidNameRegex` 字元類別的字元，以及空白與 `'`、`-`、`.`。
    *   **拉丁字母規則**：只允許「原文中本來就是大寫的字母」以半形大寫出現（例：原文 Big D 可輸出「大D」）。出現原文沒有的字母，或任何小寫字母，代表模型留下了未翻譯的英文，該欄視為失敗。
    *   結果為空、或超過該欄 `maximumNameLength`：**該欄視為失敗**，不寫入、不截斷，並提示玩家。
    *   （選用）常見簡體字黑名單檢查（如「们、尔、兰、罗、丽」），命中則視為失敗。
    *   寫入前最後再用 `CharacterCardUtility.ValidNameRegex` 驗證一次，確保寫進存檔的內容與原版打字規則一致。
5.  **反射效能快取**：遊戲啟動時以 `HarmonyLib.AccessTools` 一次建立靜態存取委派，不在 `DoWindowContents` 每幀迴圈中呼叫傳統反射。
    *   涵蓋成員：`Dialog_NamePawn.names`、`size`、`pawn`；`NameContext.current`、`editable`、`nameIndex`、`maximumNameLength`。
    *   `NameContext` 是 private 巢狀類別，使用 `AccessTools.Inner(typeof(Dialog_NamePawn), "NameContext")` 加上以 `Type` 為參數的 `FieldRefAccess` 多載。
    *   不建議用 Publicizer 直接存取 private 成員：它依賴 Mono 執行環境不檢查存取權限，1.7 換引擎後是否仍成立尚不明確。
6.  **友善錯誤處理**：所有例外都在背景執行緒攔截，轉成錯誤類型，回到主執行緒後以 `Messages.Message(..., MessageTypeDefOf.RejectInput)` 提示：

| 情況 | 提示內容 |
| :--- | :--- |
| 未設定 API Key | 「尚未設定 API Key；原版名字仍可透過內建字典翻譯」 |
| `400` / `403` | 依錯誤內容區分「金鑰無效」、「無權使用此模型」或「參數不受支援」，並建議按「測試連線」 |
| `404` | 「模型代號不存在」 |
| `429` | 「請求過於頻繁或已達每日額度；每日額度於臺灣時間 15:00／16:00 重置」 |
| `5xx`、逾時、斷線 | 「Gemini 暫時無法連線，請稍後再試」 |

7.  **Patch 失敗隔離**：
    *   所有 Postfix／Prefix 邏輯都包在 `try/catch` 中，因為 OnGUI 內的例外會讓整個原版視窗無法操作。
    *   Transpiler 找不到注入點時，只停用本模組 UI 並記錄一次警告。
8.  **IMGUI 焦點**：文字框有鍵盤焦點時，程式改寫內容可能不會反映在畫面上。套用結果前先呼叫 `UI.UnfocusCurrentTextField()`；此項列入 Step 0 實測。

---

## 8. 版本相容與發佈

*   **About.xml**：
    *   `supportedVersions` 先只列 `1.6`。
    *   `modDependencies` 宣告 `brrainz.harmony`（附 Workshop 連結）。
    *   `loadAfter` 包含 `brrainz.harmony`。
*   **LoadFolders.xml**：`<v1.6>` 載入 `/` 與 `1.6`。1.7 驗證後新增 `<v1.7>`；若組件需要分開編譯，就新增 `1.7/Assemblies`。
*   **建置**：
    *   `TargetFramework net472`，引用 NuGet 套件 `Krafs.Rimworld.Ref`（1.6.\*）。
    *   `Lib.Harmony` 設定 `ExcludeAssets="runtime"`，不把 Harmony 一起打包，而是依賴 Harmony 模組。
    *   輸出到 `1.6/Assemblies/`。
*   **1.7（Waylith 引擎）因應策略**：
    *   已知資訊：存檔可在 1.6 與 1.7 之間雙向相容；官方稱大多數 Mod 不需修改即可運作；遊戲會以黃色標示未宣告 1.7 支援的 Mod。
    *   盡量使用 .NET 標準函式庫（`HttpClient`、自帶 JSON parser），避免 `UnityWebRequest` 與 `JsonUtility`。
    *   UI Patch 層（`UI_Patches/`）與管線層（`Pipeline/`、`Llm/`）嚴格分離。萬一 1.7 的 UI 結構改變，只需重寫 Patch 層。
    *   MVP 完成後到 unstable 分支實測，確認無誤再把 `1.7` 加進 `supportedVersions`。
*   **語言包**：繁體中文為主，英文 Keyed 為後備。
*   **與其他模組相容**：測試同樣修改命名或名字庫的模組（如 Pawn Name Variety、PawnNameZH、角色編輯器類模組）。

---

## 9. 實作階段檢查清單（Implementation Checklist）

### Step 0：技術驗證（Spike）
- [x] 建立 `RimTaiName.csproj`，確認可在 1.6.4871 載入並套用 Harmony patch。
    *   2026-10-16 通過。專案直接引用遊戲 `Managed/` 的 DLL 與 Harmony 模組的 `0Harmony.dll` 編譯（.NET 10 SDK，`net472`），不需任何 NuGet 套件。
    *   測試環境為另一台電腦，RimWorld 1.6.4871 rev591，載入 386 個 Mod（含 Prepatcher、Humanoid Alien Races、HugsLib、大量漢化包），3 個 patch 全數套用。
    *   測試電腦的 Harmony 為 2.4.2，編譯時引用的是 2.4.3，仍可正常運作。
- [x] 在遊戲內以 `HttpClient` 成功連線 `generativelanguage.googleapis.com`（驗證 Mono 的 TLS）。
    *   2026-10-16 通過：不帶金鑰收到 `HTTP 403 Forbidden`，耗時 231 ms。
- [ ] 驗證 Transpiler 注入點，以及鎖頭在成年小人（四列）與新生兒（三列，姓氏列為「建議...」）視窗中的對齊。
    *   成年小人：2026-10-16 通過；動物改名視窗不受影響。
    *   **新生兒：尚未測試。**
    *   記錄檔顯示工具列「注入」了兩次：載入順序在後的 Mod 也 patch 了同一個方法時，Harmony 會重跑我們的 Transpiler，兩次都成功找到注入點。
- [x] 驗證文字框有焦點時程式寫入 `current` 的顯示行為（7.8）。
    *   2026-10-16 實測：「保留焦點」與「取消焦點」兩種寫入方式畫面都正確更新，鎖定的欄位不會被寫入。正式版仍保留先取消焦點的做法作為保險。

### Step 1：基礎建設與設定頁
- [ ] 建立目錄結構、`About.xml`、`LoadFolders.xml`、繁中與英文語言包。
- [ ] 實作 `RimTaiNameSettings`：
    - [ ] API Key（遮罩輸入）、模型代號（預設 `gemini-3.5-flash-lite`）、「測試連線」。
    - [ ] 最短請求間隔、「保留原版稱呼」開關。
    - [ ] 清除／開啟個人快取、隱私告知文字。
- [ ] 實作 `PersonalNameCache`（XML、Config 資料夾、執行緒安全、主執行緒存檔）。

### Step 2：內建字典與翻譯管線
- [x] 以 `Tools/extract_vanilla_names.py` 離線抽取原版名字，產出 `NameTable/`（2026-10-14 完成，取代原訂的遊戲內 `NameBankExporter`）。
- [x] 批次翻譯全部 24,596 個名字詞，產出 `Dictionary/BaseNames_zhTW.tsv` v0.1（2026-10-15；Antigravity CLI `gemini-3.8-flash-low`，8 行程，15.7 分鐘）。Tier 1 已逐筆校對，官方譯名已逐筆對照，詳見 `NameTable/translations/README.md`。
- [ ] Tier 2（624 詞）與 Tier 4（549 詞）逐筆校對；第 3 層依玩家回報修正。
- [ ] 實作 `NameKey`、`BuiltInDictionary`、`NickAliasResolver`、`NameSanitizer`、`NameProcessorService`。
- [ ] 在 `RimTaiName.Tests` 完成單元測試，涵蓋：
    - [ ] 暱稱四種來源與空白暱稱。
    - [ ] 鎖定組合。
    - [ ] 非法字元與超長輸出。
    - [ ] 命名空間隔離（`Fox` 當姓與當暱稱）。

### Step 3：Gemini API
- [ ] 定義 `ILlmClient`，實作 `GeminiInteractionsClient`：`store: false`、header 驗證、Schema、逾時、重試、錯誤分類。
- [ ] 實作 `MiniJson`（或內嵌 MIT 授權的單檔 parser 並改到自有命名空間）。
- [ ] 實作 `PromptBuilder`（MVP 翻譯 Prompt 含臺灣譯名範例；Phase 2 範本預留）。
- [ ] 確認任何記錄檔都不會輸出 API Key。

### Step 4：UI 攔截與命名視窗擴充
- [ ] `Patch_Dialog_NamePawn_Ctor`：Prefix 解鎖（僅限人類＋`NameTriple`）；Postfix 加寬 41px、建立 `DialogState`。
- [ ] `Patch_NameContext_MakeRow`：Postfix 繪製鎖頭（僅限 `nameIndex` 0~2 且可編輯的列）。
- [ ] `Patch_Dialog_NamePawn_DoWindowContents`：
    - [ ] Transpiler 插入工具列。
    - [ ] Prefix 套用結果。
    - [ ] Postfix 偵測「確定」並觸發學習。
- [ ] 手動測試情境：
    - [ ] 成年殖民者；新生兒出生信件；開發者模式。
    - [ ] 動物與機械體改名（不得出現模組 UI）。
    - [ ] 等待中按 ESC、點視窗外、按 Enter。
    - [ ] 還原；全鎖定；部分鎖定。
    - [ ] 無 API Key 時僅靠字典翻譯。

### Step 5：第二階段功能
- [ ] 實作 `PawnContext` 擷取（性別、背景故事標題、文化、異種族、年齡層）。
- [ ] 依 6.4 實作生成 Prompt，連結 `ProcessMode.Generate` 管線。
- [ ] 顯示並啟用「生成中文名」按鈕。

### Step 6：發佈
- [ ] 撰寫 Workshop 說明：功能、API Key 申請教學、隱私告知、額度說明。
- [ ] 於 1.7 unstable 分支實測，通過後更新 `supportedVersions` 與 `LoadFolders.xml`。

---

## 10. 參考資料

*   Gemini 模型停用時程：https://ai.google.dev/gemini-api/docs/deprecations
*   Gemini 更新紀錄（2.5 存取限制、取樣參數棄用）：https://ai.google.dev/gemini-api/docs/changelog
*   Gemini 模型列表：https://ai.google.dev/gemini-api/docs/models
*   Gemini 速率限制（以專案計、太平洋時間午夜重置）：https://ai.google.dev/gemini-api/docs/rate-limits
*   Gemini 定價（免費層資料用途）：https://ai.google.dev/gemini-api/docs/pricing
*   Gemini thinking 設定：https://ai.google.dev/gemini-api/docs/generate-content/thinking
*   Interactions API：https://ai.google.dev/gemini-api/docs/interactions
*   Interactions API reference：https://ai.google.dev/api/interactions-api
*   結構化輸出：https://ai.google.dev/gemini-api/docs/structured-output
*   Ludeon：Waylith 引擎與 1.7 更新公告：https://ludeon.com/blog/2026/10/announcing-the-waylith-engine-and-update-1-7/
*   RimWorld 1.6 反編譯原始碼（參考用）：https://github.com/Dyyrlysh/RimworldDecompile
*   PawnNameZH（簡中人名對照表模組）：https://steamcommunity.com/sharedfiles/filedetails/changelog/2556008664
