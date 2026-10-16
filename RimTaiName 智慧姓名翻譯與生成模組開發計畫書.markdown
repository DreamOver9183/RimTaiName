# RimTaiName - RimWorld 智慧姓名翻譯與生成模組開發計畫書

## 1. 專案概述與核心目標

本專案（代號：**RimTaiName**）旨在為《RimWorld》（邊緣世界）開發一款**極低侵入性、100% 相容原版機制**的角色命名輔助模組。透過擴充原版命名視窗（`Dialog_NamePawn`）並串接 **Google Gemini API** 與**本機持久化快取**，讓玩家能一鍵將小人的英文姓名轉換為符合臺灣繁體中文習慣的譯名，並在第二階段支援根據角色背景與鎖定欄位自動生成中文姓名。

### 1.1 階段開發里程碑
*   **第一階段（MVP - 最小可行性產品）**：
    *   解鎖角色資訊欄右上角「重新命名」按鈕的三欄編輯權限（名、暱稱、姓）。
    *   保留原版「先點隨機抽英文、再決定是否翻譯」的操作習慣。
    *   實作欄位鎖定機制（鎖頭開關）、底部單一「翻譯姓名」與「還原」按鈕。
    *   串接 Gemini API 實現「姓名音譯 ＋ 暱稱意譯優先」之智慧轉換，並內建本機自動學習快取（Auto-Learning Cache）。
*   **第二階段（Phase 2 - 擴充功能）**：
    *   啟用預留之「生成中文名」接口，讀取角色性別（`Gender`）、背景故事（`Backstory`）與文化/基因資訊。
    *   支援「固定已鎖定姓氏（或名字），由 AI 搭配生成其餘未鎖定欄位」之進階命名玩法。

---

## 2. 遊戲底層機制整合規範

為確保不破壞原版存檔結構、社交文本與《Biotech》生育繼承邏輯，**RimTaiName** 嚴格遵循以下原生規範：

| 項目 | 設計規範與底層行為 | 架構效益 |
| :--- | :--- | :--- |
| **姓名資料結構** | 維持原版 `NameTriple(First, Nick, Last)` 三欄獨立格式，不強行在暱稱中加入特殊符號。 | 1. 新生兒能原生繼承父母已中文化的 `Last`（姓氏）。<br>2. 婚姻冠姓事件能正常運作。<br>3. 無須修改原版 `ValidNameRegex` 字元驗證器。 |
| **同名隱藏引號規則** | 若原名中 `Nick == First` 或 `Nick == Last`（代表無獨立外號），翻譯後強制使兩欄中文完全一致。 | 原版 `ToStringFull` 會在 `Nick == First` 時自動顯示為乾淨的 `吉賽兒 蓮娜`，避免出現 `吉賽兒 '吉賽兒' 蓮娜` 的冗餘顯示。 |
| **零存檔污染 (Zero Save Footprint)** | 模組不新增任何自訂 `ThingComp` 或 `WorldComponent` 寫入存檔，僅在按下「確定」時更新原版 `Pawn.Name` 字串。 | 玩家可隨時於中途加入或移除本模組，已改名的小人名字永久保留於原版存檔中，絕不引發存檔損壞或紅字。 |
| **UI 字型安全** | 所有新增按鈕與提示全面採用純文字（搭配語言包 Keyed XML）或原版內建圖示（`TexButton` / `Widgets`），**禁用 Emoji**。 | 徹底迴避 Unity IMGUI 舊版字型不支援 Emoji 所導致的缺字方塊（`□`）問題。 |

---

## 3. 使用者介面（UI）與互動流程設計

### 3.1 視窗版面配置藍圖（擴充 `Dialog_NamePawn`）
透過 Harmony 攔截 `PawnNamingUtility.NamePawnDialog`，將成年小人右上角改名權限升級為 `NameFilter.First | NameFilter.Nick | NameFilter.Last`，並利用 `ref Rect` 邊界預留法（右側預留 `36px`、底部增高 `42px`）嵌入模組控制項：

```
+-------------------------------------------------------+--------+
| 為 Gisele 'Fox' Miller 重新命名                        | (鎖定欄)|
|-------------------------------------------------------+--------|
| 名 (First)  : [ Gisele               ]  [ 隨機 ]      | [未鎖] |
| 暱稱 (Nick) : [ 狐狸                 ]  [ 隨機 ]      | [鎖定] |
| 姓 (Last)   : [ 米勒                 ]  [ 隨機 ]      | [鎖定] |
+-------------------------------------------------------+--------+
| [ 翻譯姓名 ]    [ 生成中文名 (Phase 2) ]              [ 還原 ] |
+----------------------------------------------------------------+
| [ 取消 ]                                              [ 確定 ] |
+----------------------------------------------------------------+
```

### 3.2 標準使用者操作流程（User Flow）

```
[ 1. 開啟命名視窗 ] (點擊角色面板右上角改名圖示 / 嬰兒出生信件)
        │
        ├─► 系統建立開啟瞬間之姓名快照 (InitialSnapshot: First, Nick, Last)
        └─► 初始化三個欄位的鎖頭狀態為「未鎖定 (Unlocked)」
        │
[ 2. 抽籤與鎖定階段 (可重複循環) ]
        ├─► 玩家可點擊任一列的原版 [ 隨機 ] 抽取新的英文詞彙
        ├─► 玩家可直接點擊文字輸入框，以鍵盤手動打字修改
        └─► 對滿意的欄位（例如已翻好的姓氏「米勒」），點擊右側 [ 鎖頭 ] 將其鎖定
        │
[ 3. 觸發 AI 處理階段 ]
        ├─► 點擊底部 [ 翻譯姓名 ] (MVP) 或 [ 生成中文名 ] (Phase 2)
        ├─► 系統檢查：是否三欄全鎖定？是否未鎖定欄位皆為空白？
        │     └─ 若是：直接攔截並提示，不消耗 API 額度
        ├─► 系統檢查：未鎖定欄位是否已存在於「本機快取字典」？
        │     └─ 若全部命中快取：0 毫秒直接寫入未鎖定之輸入框，消耗 0 額度
        └─► 若包含未知詞彙：
              ├─ 按鈕狀態切換為「處理中...」並暫時禁用點擊 (防連點保護)
              ├─ 背景執行緒發送單一打包 HTTPS 請求至 Gemini API
              └─ 收到回傳後，僅將結果覆寫至「未鎖定」之輸入框，並自動存入本機快取
        │
[ 4. 確認或還原 ]
        ├─► 點擊 [ 還原 ]：將「未鎖定」欄位恢復為 InitialSnapshot 的初始值
        └─► 點擊原版 [ 確定 ]：原版邏輯驗證字元並正式寫入 Pawn.Name，關閉視窗
```

---

## 4. 系統核心架構與模組職責劃分

### 4.1 專案目錄結構（命名空間：`RimTaiName`）

```
RimTaiName/
├── About/
│   └── About.xml                        # 模組描述 (PackageId: Author.RimTaiName) 與 Harmony 依賴宣告
├── Languages/
│   └── ChineseTraditional/
│       └── Keyed/
│           └── RimTaiName_Keys.xml      # 純文字介面標籤與錯誤提示訊息
└── Source/
    └── RimTaiName/
        ├── Core/
        │   ├── RimTaiNameMod.cs         # Mod 進入點、Harmony 初始化
        │   ├── RimTaiNameSettings.cs    # ModSettings (API Key、模型名稱、快取開關)
        │   ├── NameRequestPayload.cs    # 封裝三欄文字、鎖定狀態、處理模式與角色背景之 DTO
        │   └── INameProcessorService.cs # 統一處理介面 (解耦 MVP 翻譯與 Phase 2 生成)
        ├── Services/
        │   ├── GeminiApiClient.cs       # 非同步 HTTPS 通訊、JSON 解析、錯誤碼處理
        │   ├── PromptBuilder.cs         # 負責組裝 MVP 翻譯 Prompt 與 Phase 2 生成 Prompt
        │   ├── LocalNameCache.cs        # 讀寫本機 Config/RimTaiName_Cache.json
        │   └── NameProcessorService.cs  # 核心管線協調器 (快取檢查 -> API 呼叫 -> 鎖定過濾)
        └── UI_Patches/
            ├── DialogStateController.cs # 管理當前開啟視窗之 Snapshot、鎖頭陣列與非同步狀態
            ├── Patch_PawnNamingUtility.cs # 解鎖成年小人重新命名視窗之三欄編輯權限
            └── Patch_Dialog_NamePawn.cs # 調整視窗尺寸、繪製右側鎖頭與底部功能列
```

### 4.2 統一資料傳輸契約（`NameRequestPayload`）
為確保第一階段與第二階段共用同一套底層管線，定義以下資料結構：

*   `ProcessMode Mode`：`Translate`（MVP 英翻中）或 `Generate`（Phase 2 隨機生成）。
*   `string First / Nick / Last`：當前三個文字輸入框內的即時字串。
*   `bool LockFirst / LockNick / LockLast`：三個欄位的鎖頭狀態。
*   `PawnContext Metadata`（Phase 2 專用）：包含 `Gender`（性別）、`ChildhoodBackstory`（童年背景）、`AdulthoodBackstory`（成年背景）、`CultureName`（文化名稱）。

---

## 5. Gemini API 規格與翻譯規則設計

### 5.1 免費額度（Free Tier）效益最大化策略
*   **預設推薦模型**：`gemini-2.5-flash-lite` 或 `gemini-3.1-flash-lite`（免費額度高達 **1,500 次/日**、**15~30 次/分**），並於模組設定頁開放玩家自由切換或手填新模型代號。
*   **三欄打包單一請求（Request Bundling）**：無論修改幾個欄位，每次點擊按鈕僅發送 **1 次** HTTP POST 請求（內含三個欄位的 JSON），單次消耗僅約 150 Tokens。
*   **極速回應最佳化**：
    *   強制設定 `thinkingBudget: 0`（關閉推論思考過程），將 API 延遲壓低至 **0.3 ～ 0.5 秒**。
    *   強制設定 `responseMimeType: "application/json"`，確保模型 100% 只回傳結構化 JSON：
        ```json
        {
          "first": "吉賽兒",
          "nick": "狐狸",
          "last": "米勒"
        }
        ```

### 5.2 欄位翻譯邏輯（MVP System Prompt 規則）
1.  **`first`（名）與 `last`（姓）**：
    *   採用自然、符合臺灣繁體中文習慣的西方或外文人名**音譯**（例如：`Gisele` -> `吉賽兒`、`Miller` -> `米勒`）。
    *   若為部落複合字姓氏（如 `Greywind`），可依字根翻為自然複合姓氏（如 `灰風` 或音譯）。
2.  **`nick`（暱稱 - 意譯優先原則）**：
    *   **規則 A（同名同步）**：若輸入的 `nick` 與 `first` 或 `last` 英文完全相同，輸出必須與該欄位的中文譯名完全一致。
    *   **規則 B（外號意譯優先）**：若 `nick` 為具象名詞、動物、自然現象、職業或特徵外號（例如 `Fox`, `Snake`, `Bear`, `Doc`, `Sparky`, `Red`, `Shadow`），優先採用符合《RimWorld》角色外號風格的**意譯**（如 `狐狸`, `毒蛇`, `大熊`, `老醫`, `火花`, `紅髮`, `暗影`）。
    *   **規則 C（簡稱音譯保底）**：若 `nick` 為常見人名簡稱（如 `Lena`, `Bob`, `Alex`），則採用臺灣慣用**音譯**（如 `蓮娜`, `鮑伯`, `艾歷克斯`）。
3.  **鎖定與中文保留規則**：
    *   已標記為鎖定（Locked）或已經是純中文字元的欄位，模型僅將其視為上下文參考（協助搭配音韻），回傳時保持原字不變。

---

## 6. 安全防呆與邊界條件防護機制

1.  **非同步執行緒安全（Main Thread Safety）**：
    *   所有網路請求皆封裝於背景 `Task.Run` 中執行，嚴禁阻塞 Unity 主渲染執行緒。
    *   背景執行緒完成解析後，將結果寫入執行緒安全的佇列或狀態變數，於下一幀 `DoWindowContents` 渲染時由主執行緒安全更新 UI 文字框。
2.  **視窗存活檢查（Window Alive Check）**：
    *   當 API 回傳結果時，先檢查當前 `Find.WindowStack` 是否仍開啟著同一個 `Dialog_NamePawn` 實例。若玩家在等待期間已按 ESC 關閉視窗，則僅將翻譯結果寫入本機快取，放棄操作 UI，防止 `NullReferenceException`。
3.  **雙重鎖定防覆寫（Dual-Layer Lock Enforcer）**：
    *   除了在 Prompt 中告知模型哪些欄位已鎖定外，C# 端在將 JSON 結果寫回 `curName` 輸入框前，會強制檢查 `if (!IsLocked[i])` 才允許覆寫，100% 杜絕模型幻覺竄改玩家鎖定的文字。
4.  **反射效能快取（Reflection Caching）**：
    *   存取 `Dialog_NamePawn` 內部私有清單 `names` 及內部類別 `NameContext.curName` 時，統一於遊戲啟動階段透過 `HarmonyLib.AccessTools` 建立靜態 Getter/Setter 委派，不在 `DoWindowContents` 每幀迴圈中呼叫高成本的傳統反射。
5.  **友善錯誤處理（Graceful Degradation）**：
    *   當玩家未設定 API Key、網路斷線或觸發每分鐘請求上限（HTTP `429`）時，攔截所有例外並透過遊戲左上角原生訊息 `Messages.Message(..., MessageTypeDefOf.RejectInput)` 提示玩家，確保遊戲絕不崩潰或跳出紅字視窗。

---

## 7. 實作階段檢查清單（Implementation Checklist）

### 階段一：基礎建設與設定頁（Step 1）
- [ ] 建立 `RimTaiName` 模組目錄結構、`About.xml` 與繁體中文語言包 `RimTaiName_Keys.xml`。
- [ ] 實作 `RimTaiNameMod` 與 `RimTaiNameSettings` 設定介面（支援輸入 API Key、選擇模型、清除本機快取按鈕）。
- [ ] 實作 `LocalNameCache` 本機 JSON 快取讀寫機制（`Config/RimTaiName_Cache.json`）。

### 階段二：Gemini API 與核心翻譯管線（Step 2）
- [ ] 定義 `NameRequestPayload` 與 `INameProcessorService` 統一接口（含 Phase 2 預留欄位）。
- [ ] 實作 `PromptBuilder`（MVP 翻譯 Prompt 與 Phase 2 生成 Prompt 模板）。
- [ ] 實作 `GeminiApiClient` 非同步 HTTP 請求與輕量 JSON 解析器。

### 階段三：UI 攔截與命名視窗擴充（Step 3）
- [ ] 實作 `Patch_PawnNamingUtility`：解鎖角色面板右上角改名視窗的 `First / Nick / Last` 三欄權限。
- [ ] 實作 `DialogStateController`：管理視窗初始快照（`InitialSnapshot`）、三欄鎖頭布林陣列與請求狀態。
- [ ] 實作 `Patch_Dialog_NamePawn`：微調視窗寬高、繪製右側三顆鎖頭開關與底部 `[ 翻譯姓名 ]`、`[ 還原 ]` 按鈕。

### 階段四：第二階段功能啟用（Step 4 - 後續擴充）
- [ ] 串接 `Pawn.story` 與 `Pawn.gender` 資料擷取邏輯。
- [ ] 啟用視窗底部 `[ 生成中文名 ]` 按鈕並連結至 `ProcessMode.Generate` 管線。