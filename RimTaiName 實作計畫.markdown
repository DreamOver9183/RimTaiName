# RimTaiName 實作計畫（依《開發計畫書 v3》）

*   **建立日期**：2026-10-16
*   **重審日期**：2026-10-16。重新查證遊戲 API 與 Gemini API 現況，修正內容已回寫 v3。重審前的版本在 git 第一個 commit。
*   **依據**：`RimTaiName 開發計畫書 v3.markdown`

## 進度

- [x] 實作計畫核准並存入專案資料夾（2026-10-16）
- [x] 準備工作：git、`.gitignore`、第一個 commit（2026-10-16）
- [x] 實作計畫全面重審，修正回寫 v3（2026-10-16）
- [x] 建立 CLAUDE.md 知識庫、GitHub 遠端與 PR 審查流程（2026-10-16）
- [x] Step 0 收尾：新生兒命名視窗（開發模式＋上帝模式）（2026-10-16）
  - [x] 鎖頭靠右對齊（PR #1）
  - [x] 遊戲內實測三種視窗（測試機：有父母、無父母、成年）
- [x] Step 1：基礎建設、設定頁、API Key 與 Gemini 傳輸層（2026-10-16）
  - [x] 金鑰儲存與遮蔽、xUnit 測試專案（PR #1）
  - [x] Gemini 傳輸層；即時 API 測試 4／4 通過（PR #2）
  - [x] 個人快取（PR #3）
  - [x] 打包腳本與自我測試（PR #4）
  - [x] 設定頁與 Mod 整合，移除 Spike（PR #5）
  - [x] 第一次實測：儲存金鑰後設定頁下半部消失 → 修正 Listing 自動開新欄（PR #6）
  - [x] 金鑰稽核腳本 `audit_key_leak.ps1`（PR #7）
  - [x] 遊戲內實測（`TestLog/3`）：DPAPI 儲存與重開後保留、顯示／隱藏、測試連線（HTTP 200）、不存在的模型（HTTP 404）、稽核無金鑰
  - 未測：開發機上的 DPAPI（開發機沒有執行過遊戲）；遊戲內輸入錯誤金鑰（已由即時單元測試涵蓋）
- [x] Step 2：翻譯管線（純 C#）＋ 單元測試（2026-10-16）
  - [x] 名字規則 `NameKey`／`SpecialFormRule`／`NameSanitizer`，以及 Python 一致性 fixture（24,623 列）（PR #8）
  - [x] 內建字典 `BuiltInDictionary`（PR #9）
  - [x] 資料契約、`NickAliasResolver`、`NameProcessorService`、`ILlmClient` 介面（PR #10）
  - 單元測試共 252 項通過（另 4 項即時測試在未設定金鑰時略過）
  - 與原計畫的差異：`ProcessMode.Generate` 暫時丟 `NotSupportedException`（Step 6 實作）；別名只在對應欄位已是中文時才比對開窗時的英文
  - Mono 執行期的 Unicode 正規化仍待 Step 4 在遊戲內驗證（André、ＭＪ）
- [x] Step 3：Gemini 翻譯請求（2026-10-16）
  - [x] 共用規則檔 `Prompts/NameRules_zhTW.txt`、`name_rules.py` 改讀規則檔（提示逐字不變）、`PromptBuilder`（PR #11）
  - [x] `GeminiNameTranslator`（實作 `ILlmClient`）與 `RequestGate`；即時測試：三個字典沒有的名字經整條管線實際翻譯成功（PR #12）
  - [x] 接進 Mod、`UiJob`、測試連線經請求限制、開發者模式的「翻譯測試」（PR #13）
  - [x] 遊戲內實測（`TestLog/4`）：翻譯測試兩次（三欄 Gemini；Gisele／Fox／Millere → Gemini、內建字典、Gemini，姓欄實際輸入由測試機快取檔確認）、測試連線兩次皆 HTTP 200；連點時按鈕在請求進行中停用，沒有重複送出；稽核與 log 掃描都沒有金鑰
  - 未在遊戲內觸發：請求限制的拒絕訊息（按鈕停用已擋下連點；拒絕邏輯由 `RequestGateTests` 涵蓋）、斷網
  - 遊戲內測試用名：真實字典有 Fox（名、暱稱）、Miller，**沒有 Gisele**（只有單元測試的假字典有）
  - 與原計畫的差異：`TranslateAsync` 放在獨立的 `GeminiNameTranslator`（傳輸與翻譯語意分開）；新增 `Core/UiJob<T>` 供設定頁與 Step 4 共用
- [x] Step 4：命名視窗正式版——**MVP 完成**（2026-10-16）
  - [x] 視窗狀態判斷抽成純 C# 的 `Pipeline/NameDialogSession`，單元測試 26 項（PR #14）
  - [x] 接進命名視窗：「翻譯姓名」「還原」、等待中保護、確定後學習、訊息與語言鍵（PR #16；#15 因基底分支刪除被關閉）
  - [x] 遊戲內實測（`TestLog/5`、`TestLog/5-2`）：成年視窗翻譯、還原、鎖定；Gemini 翻譯（HTTP 200）；等待中點外不關閉、改過的欄位不被覆寫、ESC 後結果進快取並在下次開窗取用；ＭＪ → MJ（特殊字形）、André → 安德烈（遊戲內 Unicode 正規化正常）；新生兒版視窗與鎖頭對齊；動物與機械體視窗沒有模組 UI；學習修正（`name:miller = 米樂`，origin user）；log 沒有模組錯誤、稽核沒有金鑰
  - 第一次實測（`TestLog/5`）的第 5 步沒有留下學習紀錄，第二次重測（`TestLog/5-2`）有，原因無法從 log 判斷；若再發生，先加「確定時學到幾筆」的記錄再查
  - 未在遊戲內測試：沒有 API Key 時的提示、等待中按 Enter（邏輯與 ESC 相同，晚到的結果只進快取）
  - 與原計畫的差異：`RequestId` 改由 `NameDialogSession` 的世代計數與 `UiJob` 處理；特殊字形與從「建議...」選的姓不學習；把譯名換成英文後再改回中文也不算修正
- [x] Step 5：字典 v1.0（2026-10-16，PR #17）
  - [x] `Tools/review_sheet.py`（export／apply）與 `review_accepted.csv`；`merge_dictionary.py` 的 `review_queue.csv` 排除已定案條目
  - [x] 第 2、4 層 1,170 筆逐筆校對：Claude 全數檢查，Gemini 子代理獨立審查，使用者確認（調整 7 筆）。修正 100 筆，其餘 1,135 筆（含第 1 層單字名 65 筆）列入 `review_accepted.csv`
  - [x] `review_queue.csv` 只剩第 3 層 430 筆；字典與 fixture 重新產生，單元測試 322 項通過、即時測試 5／5
  - [x] Prompt【範例】新增 9 個字典內的例子
  - 與原計畫的差異：`review_accepted.csv` 多記 `value`，譯名變動時會重新進入校對佇列；只帶提示性標記的人工修正也視為定案；修正官方譯名一律寫進 `review_corrections.csv`
- [ ] Step 6：第二階段（生成中文名）
  - [x] 生成管線與 Prompt（純 C#）：`PawnContext`、`PlanGeneration`、`FieldSource.Generated`（不寫快取、不學習）、`PromptBuilder.GenerateInstruction`；即時測試實際生成通過（PR #18）
  - [x] 命名視窗：`PawnContextReader`、「生成中文名」按鈕、依模式區分的訊息與記錄（PR #19）
  - [ ] 遊戲內驗證：男女、不同背景／文化／異種族各生成數次；鎖定欄位不變；兒童、沒有意識形態的小人不出錯
  - 與原計畫的差異：以 `Seed` 產生外文名開頭字母的提示，讓重複生成的結果不同（取樣參數已棄用）；未鎖定欄位的舊值不送出
- [ ] Step 7：上架 Steam 創意工坊

## Context

v3 計畫書（`RimTaiName 開發計畫書 v3.markdown`，2026-10-16）已經定案設計與路線圖。Step 0～5 已完成，MVP 達成，字典升級為 v1.0（皆為 2026-10-16）；下一步是 Step 6（第二階段：生成中文名）。

本文件把 v3 第 11 節的路線圖展開成可以直接執行的工作清單，每一步都列出：
*   要建立或修改哪些檔案。
*   類別的職責與介面。
*   可重用的既有程式。
*   驗證方式。

執行順序：先完成 MVP（Step 1～4），再做字典 v1.0（Step 5）、第二階段（Step 6），最後上架 Workshop（Step 7）。

使用者已決定：
*   每個 Step 驗證通過後才 commit，只 commit 在本機。git 作者身分設在 repo 本地（`cytsai0318`）。
*   每完成一步就在本文件打勾。

---

## 已查證的事實（2026-10-16）

### 遊戲（本機 1.6.4871 rev590，以反射、IL 與反編譯原始碼核對）

| 項目 | 結果 | 對實作的影響 |
| :--- | :--- | :--- |
| `Dialog_NamePawn` 的建構子、`pawn`／`names`／`size`、`NameContext`（`current`、`editable`、`nameIndex`、`maximumNameLength`、`MakeRow`） | 全部存在 | `NamePawnAccess` 需新增 `pawn` 的存取委派 |
| 「確定」流程 | 沒有覆寫 `OnAcceptKeyPressed`。按 Enter 或確定時，`DoWindowContents` 內依序執行 `BuildName()`（**每次都 `new NameTriple`**）→ `IsValid` → `pawn.Name = name` → `WindowStack.TryRemove(this)` | 在 `DoWindowContents` Postfix 用 `ReferenceEquals(pawn.Name, OriginalName)` 偵測。不要 patch `Pawn.Name` 的 setter：它可能被 inline，而且開局畫面每次按鍵都會重設名字 |
| 視窗旗標 | 建構子設定 `forcePause`、`absorbInputAroundWindow`、`closeOnClickedOutside = true`、`closeOnAccept = false`，五個旗標都是公開可寫的欄位 | 請求在途時把 `closeOnClickedOutside` 暫設為 `false`。等待中按 Enter 會以目前內容直接確定；晚到的結果只寫入快取 |
| 新生兒視窗 | `PawnNamingUtility.NamePawnDialog` 在「命名期限內，**或開發模式且開啟上帝模式**（`DebugSettings.ShowDevGizmos`）」時走新生兒分支：三列可編輯、隱藏頭銜、姓欄的隨機鈕換成「建議...」（父母的姓） | 開發模式＋上帝模式下，任何小人都會開出新生兒版視窗，可以直接測試；只開開發模式不會 |
| 姓欄「建議」清單是空的 | 父母都沒有姓時，`MakeRow` 在姓欄**不畫任何按鈕** | 鎖頭要用 `NewCol(24f, HorizontalJustification.Right)` 靠右對齊，否則會錯位 |
| `ValidNameRegex` | 位在 `CharacterCardUtility`（公開、可寫）：`^[\p{L}0-9 '\-.]*$`；只在打字時檢查；長度以 UTF-16 計算 | 會擋 `·`、`・` 與 CJK 擴充 B 以後的字。`NameSanitizer` 只允許 U+3400–4DBF、U+4E00–9FFF 與原文中的大寫字母 |
| `System.Security.dll` | Windows 版用 crypt32 DPAPI。Linux 與 macOS 的 Mono 會**靜默改用 ManagedProtection**，不會丟例外 | 金鑰儲存方式依平台判斷，不靠加解密探測。解密失敗會丟 `CryptographicException` |
| `System.Xml.dll`、`System.Xml.Linq.dll`、`System.Net.Http.dll` | 都存在；`HttpClientHandler.AllowAutoRedirect`、`HttpClient.Timeout` 都有 | 個人快取用 `XDocument` 自行讀寫，不用 `Scribe` |
| `mscorlib` 正規化 | `string.Normalize` 與 `NormalizationForm.FormKC` 都存在 | 尚未在遊戲內實際執行過，Step 4 手動測試 |
| `GenFilePaths.ConfigFolderPath` | 存在。設定檔名為 `Mod_<資料夾名>_RimTaiNameMod.xml`，在設定視窗關閉時寫入 | 開發版（junction）與 Workshop 版的設定不互通。金鑰檔與快取檔用固定檔名，兩版共用 |
| UI API | `Listing_Standard.TextEntry` 沒有長度上限參數。以下都存在：`Widgets.TextField(Rect, string, int maxLength, Regex)`、`GUI.PasswordField(Rect, string, char)`、`GUIUtility.systemCopyBuffer`、`Application.OpenURL`、`UI.UnfocusCurrentTextField` | 模型代號用 `Widgets.TextField`，金鑰輸入用 `PasswordField` |
| `Messages.Message(string, MessageTypeDef, bool)`、`MessageTypeDefOf.RejectInput` | 存在 | 錯誤提示使用 |
| Harmony | 開發機 2.4.3、測試機 2.4.2 | 只使用 2.4.2 已有的 API |
| 第二階段需要的 API | `gender`、`story.Childhood/Adulthood.TitleCapFor(Gender)`、`Ideo?.culture`、`genes?.XenotypeLabel`、`DevelopmentalStage`（Flags）、`ageTracker.AgeBiologicalYears` | 兒童的 `Adulthood` 是 null，`Ideo` 也可能是 null |

### Gemini API（官方文件 2026-10-16）

| 項目 | 結果 | 對實作的影響 |
| :--- | :--- | :--- |
| 端點與驗證 | `POST https://generativelanguage.googleapis.com/v1beta/interactions`，標頭 `x-goog-api-key` | 不變 |
| 請求欄位 | `model`、`input`（字串）、`system_instruction`（字串）、`store`（預設 true）、`generation_config`、`response_format` | 規則放在 `system_instruction`，三欄資料放在 `input` |
| 結構化輸出 | `response_format` 的格式是 `{"type":"text","mime_type":"application/json","schema":{…}}`，**不是**直接放 JSON Schema | 照此格式組裝 |
| 回應 | 從 `steps[]` 中找 `type == "model_output"` 的 step，讀它 `content[]` 裡 `type == "text"` 的 `text`。前面可能有 `thought` step；`status` 的值包括 `completed`、`failed`、`incomplete` 等 | `status` 不是 `completed` 就視為失敗 |
| thinking 與取樣 | flash-lite 預設 `thinking_level = minimal`；`temperature`、`top_p`、`top_k` 已於 07-21 棄用，未來的模型收到會回 400 | 一律不傳 |
| 模型 | `gemini-3.5-flash-lite` 已 GA，免費層可用；`gemini-3.1-flash-lite` 最早 2027-05-07 停用；2.5 限舊用戶使用；`-latest` 別名沒有文件 | 預設模型不變 |
| 錯誤格式 | `{"error":{"code":"<snake_case 字串>","message":…}}`，沒有 `status`／`details`。常見值：401 `authentication`、403 `permission_denied`、404 `model_not_found`、402 `payment_required`、429 `rate_limit_exceeded`（每分鐘）／`quota_exceeded`（每日）／`too_many_requests` | 分類依「狀態碼＋字串 code」；`code` 不可當數字解析 |
| `models.get` | 使用舊式 Google RPC 錯誤格式，而且回 200 也不代表能生成或還有額度 | 不用它做測試連線 |
| 額度 | 每日額度在太平洋時間午夜重置；免費層的數字只能在 AI Studio 查看 | 只有 `quota_exceeded` 才顯示重置時間 |
| 金鑰格式 | 05-28 起 AI Studio 發的新金鑰是「auth key」，論壇與官方人員表示以 **`AQ.`** 開頭，長度沒有公布；新金鑰預設只能呼叫 Gemini API | 遮蔽與打包掃描都要涵蓋 `AQ.` 開頭的金鑰 |
| 條款 | 免費層的內容會用於改善 Google 產品；提供給 EEA、瑞士、英國使用者的應用只能用付費服務 | 寫進設定頁與 Workshop 說明 |

### 專案資料

| 項目 | 結果 | 對實作的影響 |
| :--- | :--- | :--- |
| 字典 | 24,596 列，沒有重複鍵，值全部在 CJK 字元與大寫字母範圍內，沒有超長的值。147 個鍵帶重音，519 個鍵含空白或標點，**132 個值含大寫字母** | 本機命中的值也要以實際原文跑一次 `NameSanitizer`（修正 12） |
| 來源詞 | 全部是 NFC，NFKC 不改變任何詞，`lower()` 不改變長度 | Python 與 C# 的鍵正規化在原版資料上一致；仍以 fixture 測試保證 |
| 校對佇列 | `review_queue.csv` 541 筆：Tier1 68、Tier2 19、Tier3 430、Tier4 24。`review` 欄每次重新產生時都是空的 | Step 5 需要「已校對保留」清單，否則無法判定哪些已處理 |
| 建置環境 | .NET SDK 10.0.402 | 不變 |

---

## 對 v3 的補充修正（已回寫 v3）

1.  **模型代號驗證：** 使用 `^[a-z0-9]+(?:[.-][a-z0-9]+)*$`，取代會放行 `..` 的 `^[a-z0-9.-]+$`。使用者貼上 `models/xxx` 時自動去掉前綴。輸入欄位上限 64 字。
2.  **測試連線：** 送出一次極小的 Interactions 請求，一次驗證金鑰、模型、權限與額度，也只需要處理一種錯誤格式。代價是每按一次用掉 1 次請求額度，設定頁會註明；這個請求同樣受 `RequestGate` 限制。
3.  **可讀取金鑰明文的地方只有三處：**
    *   `ApiKeyStore` 內部。其中 `ApiKeyStore.Redact(text)` 把已知金鑰交給 `KeyRedactor`，供 `ModLog` 使用。
    *   `GeminiInteractionsClient`：透過注入的 `Func<string>`，在送出時才取用。
    *   設定頁：按下「顯示」時。

    程式碼審查以這三處為準。`Spike/ConnectivityProbe.cs` 在 Step 1 刪除。
4.  **xUnit 測試專案提前到 Step 1 建立：** 第 9 節要求 `KeyRedactor` 有單元測試。
5.  **`NameRequestPayload` 放在 `Pipeline/`：** 它是純資料，這樣測試專案只要連結 `Pipeline/**` 與 `Llm/**`。
6.  **429 的處理：**
    *   只有 `quota_exceeded`（每日額度）才顯示重置時間。重置時間自行以美國夏令規則計算太平洋時間午夜，再換成本機時間；不依賴 `TimeZoneInfo` 的時區代號，因為 Mono 在 Windows 上不可靠。
    *   其他 429 一律提示「稍後再試」。
7.  **字典校對狀態：** 新增 `NameTable/translations/review_accepted.csv`（欄位為 namespace、text），列出「已檢查、保留原譯」的條目。`merge_dictionary.py` 產生 `review_queue.csv` 時排除這些條目。
8.  **金鑰儲存依平台決定：**
    *   Windows：DPAPI（`ProtectedData`，`CurrentUser`）。
    *   其他平台：一律只在本次遊戲期間記憶，因為 Mono 的 ManagedProtection 防護較弱，也無法在開發環境實測。
    *   解密時發生 `CryptographicException`（例如設定檔是從別台電腦複製來的）：視為沒有金鑰，並在設定頁提示重新輸入。
9.  **遮蔽與掃描：**
    *   `KeyRedactor` 以已知金鑰的完整值為主要遮蔽依據；樣式比對只是備援，涵蓋 `AIza[0-9A-Za-z_-]{35}` 與 `AQ\.[0-9A-Za-z_.-]{20,}`。
    *   遮蔽後的格式為「前綴…末 4 碼」。
    *   打包腳本同時掃描這兩種樣式。
    *   設定頁不以前綴判斷金鑰是否合法，只去除頭尾空白，並拒絕含空白或控制字元的金鑰。
10. **錯誤分類：** `error.code` 先讀成物件，可能是字串或數字。

    | 情況 | 分類 |
    | :--- | :--- |
    | 400 | 請求錯誤：附上 `message` 前 200 字，先經 `KeyRedactor` |
    | 401，或 400 且 `message` 提到 API key | 金鑰無效 |
    | 402 | 需要付費額度 |
    | 403 | 無權使用此模型，或金鑰受限 |
    | 404 | 模型代號不存在 |
    | 429 `quota_exceeded` | 今日額度已用完，附重置時間 |
    | 其他 429 | 請求太頻繁 |
    | 5xx | 等 2 秒後重試 1 次 |
    | 逾時、斷線 | 稍後再試 |
11. **鎖頭靠右對齊：** 改用 `divider.NewCol(24f, HorizontalJustification.Right)`。新生兒的姓欄沒有「建議」選項時不會畫按鈕，原本的左側切法會錯位。
12. **本機命中也要清理：** 字典與快取是用小寫鍵查詢的，例如「Ad」會查到「AD」，但原文只有大寫 A。每次本機命中都要以實際原文跑 `NameSanitizer`，不通過就當作沒命中，往下一層查。
13. **Step 0 收尾改用開發模式＋上帝模式：** 不必等嬰兒出生。

---

## 共通規則（每一步都適用）

*   **相容性：**
    *   `Pipeline/`、`Llm/`、`Core/KeyRedactor.cs` 要同時能在 net472（遊戲 Mono）與 net10.0（測試）編譯。
    *   不使用 net472 沒有的 API，例如 `record`、`init`、`Span`、`System.Text.Json`、`HashCode`、`string.Contains(char)`。
    *   不引用任何遊戲型別（`Verse`、`RimWorld`、`UnityEngine`），也不自行寫記錄；錯誤一律回傳給呼叫端處理。
    *   `ServicePointManager.SecurityProtocol |= Tls12` 放在 `RimTaiNameMod` 建構子。它在 net10 是過時 API，放在共用程式碼會讓測試專案出現警告。
*   **Harmony：** 只使用 2.4.2 已有的 API（測試機的版本）。
*   **執行緒：**
    *   背景執行緒只產生純資料，並寫入記憶體快取（加 lock）。
    *   遊戲 API 只在主執行緒呼叫，包括 `Messages`、`Find.*`、`Translate()`、寫入 `current`、快取存檔。
*   **記錄：** 所有記錄都經由 `Core/ModLog.cs`（包裝 `Log.Message/Warning/Error/ErrorOnce`），寫出前一律先過 `ApiKeyStore.Redact`。
*   **語言檔：** 新字串同時加到 `Languages/ChineseTraditional` 與 `Languages/English` 的 `RimTaiName_Keys.xml`；移除 `RimTaiName_Spike_*` 鍵。
*   **每一步的收尾流程：**
    1.  執行 `dotnet build Source/RimTaiName/RimTaiName.csproj -c Release`：0 警告、0 錯誤。
    2.  執行 `dotnet test Source/RimTaiName.Tests`：全數通過（Step 1 起）。
    3.  以 `Tools/package_mod.ps1` 打包 → 使用者在測試機實測 → `Player.log` 放進 `TestLog/` → 我檢查。
    4.  更新本文件的勾選與 v3 的「現況總覽」，然後 git commit，訊息結尾附 Co-Authored-By。

---

## 準備工作（已完成）

1.  實作計畫存成 `RimTaiName 實作計畫.markdown`。
2.  `git init`，建立 `.gitignore` 與 `.gitattributes`（`*.tsv` 固定用 LF）。
    *   **忽略：** `Source/**/obj/`、`Source/**/bin/`、`1.6/Assemblies/*.dll`、`dist/`、`__pycache__/`、`NameTable/**/raw/`、`TestLog/`。
3.  在 repo 本地設定作者身分，完成第一個 commit：Step 0 驗證版與 v3 計畫書。
4.  全面重審本計畫，並把修正回寫 v3。

---

## Step 0 收尾：新生兒命名視窗

*   **程式修改：** `Patch_NameContext_MakeRow` 改用 `divider.NewCol(24f, HorizontalJustification.Right)`（補充修正 11）。
*   **測試方式：** 開發機或測試機都可以。開啟開發模式與上帝模式後，從角色資訊卡的改名鈕開啟視窗，測以下三種情況：
    1.  有具名父母的小人：姓欄有「建議...」選單，而且仍可使用。
    2.  沒有具名父母的小人：姓欄沒有任何按鈕，鎖頭仍對齊在同一欄。
    3.  關閉上帝模式後的成年殖民者：四列；原版只有暱稱與頭銜可編輯，本模組另外解鎖名與姓。
*   **確認項目：**
    *   鎖頭欄與三列對齊。
    *   工具列位在文字框與「取消／確定」之間。
    *   `Player.log` 沒有 `[RimTaiName]` 錯誤。
*   **可選：** 若測試機剛好有嬰兒出生，再從出生信件開一次視窗確認。
*   **收尾：** 這個改動與 Step 1 一起打包、一起送測試機，一起 commit。

---

## Step 1：基礎建設、設定頁、API Key 與 Gemini 傳輸層

**新增或修改的檔案：**

| 檔案 | 內容 |
| :--- | :--- |
| `Core/RimTaiNameSettings.cs` | 繼承 `ModSettings`，欄位：`modelId`（預設 `gemini-3.5-flash-lite`）、`minIntervalSeconds`（預設 3）、`keepVanillaAddress`（保留原版稱呼，預設 true）。`ExposeData` **不含金鑰** |
| `Core/ApiKeyStore.cs` | 金鑰檔為 `Config/RimTaiName_ApiKey.dat`。Windows 用 `ProtectedData.Protect(..., CurrentUser)`；其他平台用 `SessionOnly` 模式，金鑰只存在私有靜態欄位。解密遇到 `CryptographicException` 時視為沒有金鑰，並記下原因供設定頁顯示。對外 API：`HasKey`、`MaskedHint`（前綴…末 4 碼）、`Mode`（`Dpapi`／`SessionOnly`）、`LoadError`、`Save(string)`、`Clear()`、`Redact(string)`、`internal Read()` |
| `Core/KeyRedactor.cs` | 純 C#：`Redact(string text, string knownKey = null)`。先替換 `knownKey` 的完整值，再替換 `AIza[0-9A-Za-z_-]{35}` 與 `AQ\.[0-9A-Za-z_.-]{20,}`，一律換成「前綴…末 4 碼」 |
| `Core/ModLog.cs` | 記錄的包裝層，所有訊息都先經 `ApiKeyStore.Redact` |
| `Core/RimTaiNameMod.cs` | 移除 `ConnectivityProbe`；加入 `Tls12`；呼叫 `GetSettings<RimTaiNameSettings>()`；建立 `GeminiInteractionsClient`，並注入 `ApiKeyStore.Read`；改寫 `DoSettingsWindowContents`（見下方） |
| `Pipeline/PersonalNameCache.cs` | 用 `XDocument` 讀寫 `RimTaiName_Cache.xml`，欄位為 namespace、key、value、origin。方法：`TryGet`、`Put`（加 lock 並標記 dirty）、`SaveIfDirty`（由主執行緒呼叫）、`Clear`、`Count`。檔案路徑由呼叫端傳入 |
| `Llm/MiniJson.cs` | 最小的 JSON 序列化與解析，支援物件、陣列、字串跳脫、數字、布林、null |
| `Llm/GeminiInteractionsClient.cs` | **傳輸層：**<br>• 靜態 `HttpClient`，使用 `HttpClientHandler { AllowAutoRedirect = false }`，逾時 15 秒；測試時可改注入 `HttpMessageHandler`。<br>• 端點寫死為常數；模型代號用補充修正 1 的正規表示式驗證，不合格就拒絕送出。<br>• 一律帶 `store: false`；金鑰只放 `x-goog-api-key` 標頭。<br>• 依補充修正 10 分類錯誤（`InvalidKey`、`Forbidden`、`BadRequest`、`ModelNotFound`、`PaymentRequired`、`QuotaExceeded`、`RateLimited`、`ServerError`、`Timeout`、`Network`）；5xx 重試 1 次。<br>• 回應依 `steps`／`status` 解析。<br>• `CheckAsync()` 為測試連線：送一個極小的純文字請求，確認狀態為 `completed` 並取得文字 |
| `Llm/QuotaResetClock.cs` | 計算下一次太平洋時間午夜（含美國夏令規則），轉為本機時間 |
| `Source/RimTaiName.Tests/` | xUnit、net10.0。以連結方式編譯：`<Compile Include="..\RimTaiName\Pipeline\**\*.cs;..\RimTaiName\Llm\**\*.cs;..\RimTaiName\Core\KeyRedactor.cs" Link=...>`。**建立前先徵求同意，才從 NuGet 下載 `xunit`、`xunit.runner.visualstudio`、`Microsoft.NET.Test.Sdk`** |
| `Tools/package_mod.ps1` | 複製 `About`、`LoadFolders.xml`、`1.6`、`Languages`、`Dictionary`、`Prompts` 到 `dist/RimTaiName/`，不存在的資料夾就跳過。以 ASCII 讀取所有檔案（含 DLL），掃描兩種金鑰樣式；一旦命中，就刪除輸出並以非零碼結束。另外產生 `dist/RimTaiName.zip`。必須相容 Windows PowerShell 5.1 |
| `RimTaiName.csproj` | `GameReference` 加入 `System.Security;System.Xml;System.Xml.Linq` |
| `Spike/` | 整個資料夾刪除 |
| `UI_Patches/Patch_Dialog_NamePawn.cs` | 套用 Step 0 的靠右對齊；`Log.*` 改用 `ModLog` |

**設定頁（`Listing_Standard`）：**
*   **API Key：**
    *   已儲存時顯示「已儲存：`AIza…xxxx`」與儲存方式（DPAPI 加密／僅本次遊戲，並說明原因）；解密失敗時顯示提示。
    *   輸入欄用 `GUI.PasswordField(rect, text, '*')`。
    *   按鈕：「儲存」、「貼上」（讀取 `GUIUtility.systemCopyBuffer`，去除頭尾空白後直接儲存，不經過畫面）、「顯示／隱藏」（顯示時改為一般文字框，關閉設定頁就恢復遮罩）、「清除金鑰」。
*   **「測試連線」：** 結果文字分為金鑰正確、金鑰無效、無權限、模型不存在、額度用完（附重置時間）、網路錯誤。旁邊註明「會用掉 1 次請求額度」。
*   **模型代號：** 用 `Widgets.TextField(rect, text, 64)`。不合法時以紅字提示，且不儲存。
*   **其他設定：** 最短間隔、保留原版稱呼。
*   **快取：** 顯示目前筆數，並提供「清除快取」與「開啟快取資料夾」（`Application.OpenURL`）。
*   **隱私與金鑰安全說明（v3 §9「給玩家的建議」）：**
    *   新金鑰預設只能呼叫 Gemini API。
    *   為本模組建立專用金鑰，並建在沒有啟用帳單的專案上。
    *   外洩時刪除金鑰並重建。
    *   免費層的內容會用於改善 Google 產品。
    *   EEA、瑞士、英國地區的條款限制。

**單元測試：**
*   `KeyRedactor`：`AIza` 樣式、`AQ.` 樣式、`knownKey`、多把金鑰、沒有金鑰的字串保持不變、例外訊息。
*   `PersonalNameCache`：存讀往返、namespace 隔離、`user` 覆寫 `api`、檔案損壞時從空快取開始。
*   `MiniJson`：往返、跳脫、巢狀、非法輸入。
*   `GeminiInteractionsClient`（用假的 `HttpMessageHandler` 模擬）：
    *   各狀態碼與字串 code 的分類。
    *   `code` 為數字的舊格式。
    *   5xx 只重試一次；429 不重試；302 不跟隨。
    *   `status` 不是 `completed`、缺少 `model_output` step、前面有 `thought` step。
    *   模型代號含 `/`、`?`、`..`、大寫字母或為空字串時拒絕送出。
    *   金鑰只出現在標頭，不出現在網址。
*   `QuotaResetClock`：夏令切換前後的結果。

**驗證：**
*   設定可以存檔與讀取。Mod 設定 XML 不含金鑰；`RimTaiName_ApiKey.dat` 不是明文。
*   本機與測試機的設定頁都顯示 DPAPI。
*   測試連線能分辨正確金鑰、錯誤金鑰、不存在的模型。
*   **打包：** 正常情況下打包成功。在 `Languages/` 的測試檔中放一把假的 `AIza` 金鑰、再放一把假的 `AQ.` 金鑰，兩種情況都必須中止打包。
*   打包出的資料夾在測試機能直接載入，且 Step 0 收尾的三種視窗都正常。
*   全文搜尋 `Player.log` 與 Config 資料夾，沒有金鑰明文。

---

## Step 2：翻譯管線（純 C#）＋ 單元測試

**檔案（全部在 `Pipeline/`）：**

| 類別 | 職責 |
| :--- | :--- |
| `NameRequestPayload` / `NameResult` | v3 §5.4 的資料契約。`FieldSource` 列舉：Unchanged、SpecialForm、Alias、PersonalCache、Dictionary、Api、Failed，另加 `Pending`（等待 API） |
| `NameKey` | 依序做 `Trim` → NFC → `ToLowerInvariant`。`NameSpace` 列舉：Name、Nick |
| `SpecialFormRule` | 對應 `name_rules.special_form()`：先 NFKC；2～4 個大寫字母保留原樣；單一字母當名或姓時保留，當暱稱時寫成「阿」加字母 |
| `NameSanitizer` | 依 v3 §8.4 依序處理：NFKC → Trim → 移除非法字元 → 只允許 U+3400–4DBF、U+4E00–9FFF 與原文中的大寫字母（對應 `latin_ok()`）→ 長度檢查（名、姓 12 字，暱稱 16 字）→ 以 `^[\p{L}0-9 '\-.]*$` 驗證。失敗時回傳 null，不截斷 |
| `NickAliasResolver` | 依 v3 §6.3，根據開窗時的 `NickSet`、`Nick`、名、姓判定 `Explicit`、`AliasOfFirst` 或 `AliasOfLast`；翻譯時套用「填入對應欄位的最終值」與 `keepVanillaAddress` 設定 |
| `BuiltInDictionary` | 載入 `Dictionary/BaseNames_zhTW.tsv`，略過標頭，用 `Lazy` 在第一次使用時才載入。提供 `TryGet(ns, key)` |
| `NameProcessorService` | `ResolveLocal(payload)`：依 v3 §6.1 的順序逐欄解析（空白 → 已含中文 → 特殊字形 → 暱稱同名 → 個人快取 → 字典）。**快取與字典的命中值要以實際原文跑 `NameSanitizer`，不通過就往下一層**；最後仍未解析的欄位標為 `Pending`。<br>`ResolveRemoteAsync(payload, local, ILlmClient, ct)`：把剩餘欄位一次送出，結果過 `NameSanitizer`，成功的欄位寫入快取（origin = api），最後重新計算暱稱別名 |
| `Llm/ILlmClient.cs` | 先定義介面（Step 3 實作）：`Task<LlmResponse> TranslateAsync(LlmRequest, CancellationToken)` |

**一致性測試資料：** 新增 `Tools/export_rule_fixtures.py`，把 `dictionary_draft.csv` 全部 24,596 筆加上手寫的邊界案例，匯出到 `Source/RimTaiName.Tests/Fixtures/rules.tsv`。欄位如下：
*   `text`、`namespace`
*   `NameKey` 的預期值
*   `special_form()` 的結果
*   `final_zh`
*   Python 判定是否合格（不是「空白」「含非中文字元」「超過長度上限」）

**測試內容：**
*   **與 Python 一致：** 每一列的鍵正規化、特殊字形、合格判定都要相同。
*   **字典健全：** 每個字典值用其原文跑 `NameSanitizer` 都要通過。
*   **大小寫不同的本機命中：** 「Ad」查到「AD」時必須視為沒命中。
*   **暱稱：** 四種來源與空白暱稱；`keepVanillaAddress` 開與關。
*   **鎖定：** 各種組合下，鎖定欄位永遠不會改變，但仍可作為別名來源。
*   **清理規則：** 非法字元、超長、全形字母（ＭＪ → MJ）、拉丁字母規則、`·` 與 CJK 擴充 B 的字。
*   **命名空間隔離：** Fox 當名時是福克斯，當暱稱時是狐狸；暱稱查不到時不回退到 `name:`。
*   **`ResolveRemoteAsync`：** 用假的 `ILlmClient` 測部分成功、清理失敗、丟出例外。

**驗證：** `dotnet test` 全數通過；主專案 0 警告、0 錯誤。

---

## Step 3：Gemini 翻譯請求

**開工前：** 再查一次 Interactions API reference（v3 附錄 C），確認 `response_format`、`system_instruction` 與回應結構沒有再改變；有差異時先回寫 v3 §7。

**檔案：**

| 檔案 | 內容 |
| :--- | :--- |
| `Prompts/NameRules_zhTW.txt` | 從 `Tools/name_rules.py` 的 `TRANSLATION_RULES` 移出「【規則】」與「【範例】」兩段，作為兩邊共用的部分 |
| `Tools/name_rules.py` | 改為讀取上述檔案，再拼上自己的開頭與「【輸入欄位】」說明。**組出的字串必須與移出前逐字相同**，以 diff 驗證 |
| `Llm/PromptBuilder.cs` | `system_instruction` = 規則檔 + C# 端的輸入說明；`input` 包含三欄文字、角色、是否鎖定、暱稱來源、範例姓名，以 JSON 文字表示；`response_format` = `{"type":"text","mime_type":"application/json","schema":{三個必填字串 first、nick、last}}` |
| `Llm/GeminiInteractionsClient.cs` | 加入 `TranslateAsync`（實作 `ILlmClient`）：組請求 → 傳輸層 → 解析 JSON 文字 → 三欄結果 |
| `Llm/RequestGate.cs` | 全域只允許 1 個請求在途；最短間隔可設定；測試連線也受它限制 |

**單元測試：**
*   `PromptBuilder` 的輸出結構，包含 `response_format` 的格式與是否帶 `store:false`。
*   `TranslateAsync`：正常解析、`text` 不是 JSON、缺少欄位、欄位不是字串。
*   `RequestGate`：同時發兩個請求、間隔不足。

**驗證：**
*   在遊戲內用使用者的金鑰實際翻譯一次。可以先在設定頁加一個只在開發者模式顯示的「翻譯測試」。
*   故意製造錯誤（錯誤金鑰、斷網、不存在的模型），之後全文搜尋 `Player.log` 與 Config 資料夾，不得出現金鑰明文。
*   程式碼審查：只有補充修正 3 列出的三處會讀取金鑰明文。

---

## Step 4：命名視窗正式版

**修改 `UI_Patches/`：**

| 檔案 | 改動 |
| :--- | :--- |
| `NamePawnAccess.cs` | 新增 `pawn` 欄位的存取委派 |
| `DialogState.cs` | 擴充以下欄位：`OriginalName`（開窗時的 `pawn.Name` 參考）、`NickSource`、`Snapshot`、`Locked`、`LastWritten[3]`（模組寫入的值）、`SourceText[3]`（被翻譯的原文）、`RequestId`、`InFlight`、`ValuesAtRequest[3]`、`volatile PendingResult`、`SavedCloseOnClickedOutside` |
| `Patch_Dialog_NamePawn.cs` | 見下方流程 |

**流程：**
1.  **建構子 Postfix：** 記錄 `OriginalName` 與三欄初值，並用 `NickAliasResolver` 判定暱稱來源。
2.  **按下「翻譯姓名」（主執行緒）：**
    1.  讀取三欄目前的值與鎖頭狀態，執行 `ResolveLocal`，把本機結果放進 `PendingWrite`。
    2.  若還有 `Pending` 欄位：
        *   沒有金鑰：提示「原版名字仍可用內建字典翻譯」，該欄不變。
        *   `RequestGate` 不允許：提示稍後再試。
        *   其他情況：`RequestId++`；記下 `ValuesAtRequest`；`closeOnClickedOutside = false`；按鈕改為「處理中...」並停用；以 `Task.Run(ResolveRemoteAsync)` 送出。完成時只設定 `PendingResult`（帶 `RequestId`）；快取在背景以 lock 寫入記憶體。
3.  **`DoWindowContents` Prefix（只在 `EventType.Layout` 時）：**
    *   套用 `PendingWrite`，沿用驗證版的 `ApplyPending`。
    *   若有 `PendingResult` 且 `RequestId` 是最新的：
        *   只覆寫「未鎖定、且目前的值仍等於 `ValuesAtRequest`」的欄位。
        *   失敗的欄位依錯誤分類，以 `Messages.Message(..., MessageTypeDefOf.RejectInput, false)` 提示；`quota_exceeded` 附上重置時間。
        *   恢復 `closeOnClickedOutside`，並呼叫 `cache.SaveIfDirty()`。
    *   開窗時也呼叫一次 `SaveIfDirty()`，處理前一個視窗關閉後才到達的結果。
4.  **`DoWindowContents` Postfix：** 若 `!ReferenceEquals(pawn.Name, OriginalName)`，表示玩家按了確定，進行學習：
    *   學習條件：`LastWritten` 不是空的、最終值與它不同、含中文、通過 `NameSanitizer`。
    *   符合條件的欄位以 `SourceText` 為鍵寫入快取，origin = user。
    *   暱稱是別名時不學習。
    *   最後呼叫 `SaveIfDirty()`。
5.  **「還原」：** 未鎖定的欄位恢復為 `Snapshot`，並清掉 `LastWritten`。
6.  **工具列：** 驗證版的三個按鈕換成「翻譯姓名」與「還原」兩個；「生成中文名」到 Step 6 再加。

**驗證（手動，本機與測試機）：**
*   成年殖民者、開發模式＋上帝模式（新生兒版）的視窗，以及姓欄有／沒有「建議」的情況。
*   動物與機械體的視窗沒有任何模組 UI。
*   等待中按 ESC；點視窗外，不應關閉；按 Enter，應以目前內容確定，晚到的結果只進快取。
*   全鎖定、部分鎖定、還原；等待中手動改某一欄，該欄不應被覆寫。
*   沒有 API Key 時，原版名字全部由字典翻譯；其他 Mod 的名字顯示提示。
*   修改模組給的譯名後按確定，再開同名小人時應使用玩家的修正。
*   全形字母的名字（ＭＪ）、帶重音的名字（André，驗證遊戲內的正規化）、已含中文的欄位、混合名。
*   在測試機 386 個 Mod 的環境下正常運作；`Player.log` 沒有錯誤，也沒有金鑰。

**MVP 完成點：** Step 4 通過後 commit，並在 v3 的現況總覽標記 MVP 完成。

---

## Step 5：字典 v1.0

1.  修改 `Tools/merge_dictionary.py`：讀取新的 `review_accepted.csv`，把其中的條目排除在 `review_queue.csv` 外。
2.  把 Tier 2（624 詞）與 Tier 4（549 詞）的全部條目匯出成校對表。我先逐筆預審並標出建議修改，再由使用者確認。
3.  寫回結果：
    *   確認要改的寫入 `review_corrections.csv` 或 `official_overrides.csv`。
    *   保留原譯的寫入 `review_accepted.csv`。
    *   Tier 1 的 68 筆旗標已經校對過，直接列入 `review_accepted.csv`。
4.  從字典挑選 Prompt 範例詞（名、姓、暱稱、日文、中文拼音、複合字名各數個），更新 `Prompts/NameRules_zhTW.txt` 的【範例】。
5.  執行 `python -I Tools/merge_dictionary.py` 重新產生字典，再重跑 `export_rule_fixtures.py` 與 `dotnet test`。

**驗證：** `review_queue.csv` 中沒有 Tier 1、2、4 的列；字典健全測試通過。

---

## Step 6：第二階段（生成中文名）

*   `UI_Patches/PawnContextReader.cs` 在主執行緒擷取小人資料，轉成純資料的 `Pipeline/PawnContext`。各欄位的來源：
    *   性別：`gender`。
    *   背景故事標題：`story.Childhood?.TitleCapFor(gender)`、`story.Adulthood?.TitleCapFor(gender)`；兒童沒有成年背景。
    *   文化：`Ideo?.culture?.defName`；`Ideo` 可能是 null。
    *   異種族：`genes?.XenotypeLabel`。
    *   年齡層：`DevelopmentalStage`。
*   `NameRequestPayload.Mode = Generate`。`NameProcessorService` 的生成路徑跳過字典與快取，直接送 API；鎖定的欄位作為風格參考，內容不變。
*   `PromptBuilder` 加入 v3 §7.4 的生成規則：只生成音譯或意譯風格、維持「名 姓」順序、暱稱依背景故事意譯或設為名或姓的別名。
*   生成結果**不寫入快取**，因為沒有原文可以當鍵。
*   工具列加入「生成中文名」按鈕，同樣受 `RequestGate`、`RequestId` 與鎖頭保護。

**驗證：**
*   男、女、不同背景、不同文化與異種族各生成數次，確認風格正確、鎖定欄位不變、長度與字元都在限制內。
*   兒童、沒有意識形態的小人都不會出錯。
*   單元測試涵蓋 Generate 模式的 payload 與 Prompt 組裝。

---

## Step 7：上架 Steam 創意工坊

1.  使用者決定正式的 `packageId` 與作者名稱，然後更新 `About.xml`（名稱、說明、`packageId`）與 `RimTaiNameMod.HarmonyId`。
2.  製作 `About/Preview.png`，由使用者提供或確認。
3.  撰寫 Workshop 說明（`Docs/Workshop_zhTW.txt` 與英文版），內容包括：
    *   功能介紹，並說明「不需金鑰即可翻譯原版名字」。
    *   API Key 申請教學：新金鑰以 `AQ.` 開頭，預設只能呼叫 Gemini API。
    *   第 9 節的金鑰安全建議。
    *   隱私與額度：免費層資料會用於改善產品；測試連線會用掉額度；EEA、瑞士、英國的條款限制。
    *   Workshop 版與開發版的設定不互通，但金鑰與快取共用。
4.  以 `Tools/package_mod.ps1` 打包，金鑰掃描必須通過。**由使用者在遊戲內以 Workshop 上傳功能上傳。**
5.  1.7 unstable 實測（Step 4 之後隨時可以做）：通過後才更新 `supportedVersions` 與 `LoadFolders.xml`。

---

## 端對端驗證總表

| 時機 | 指令或動作 |
| :--- | :--- |
| 每次改程式 | `dotnet build Source/RimTaiName/RimTaiName.csproj -c Release` |
| Step 1 起 | `dotnet test Source/RimTaiName.Tests` |
| 改規則或字典後 | 依序執行 `python -I Tools/merge_dictionary.py`、`python -I Tools/export_rule_fixtures.py`、`dotnet test` |
| 交付測試機 | 執行 `powershell -File Tools/package_mod.ps1`，複製 `dist/RimTaiName.zip` 到測試機，測試後把 `Player.log` 放進 `TestLog/` |
| 金鑰稽核（Step 1、3、4、7） | 在 `Player.log`、Config 資料夾、`dist/` 全文搜尋金鑰明文，以及 `AIza` 與 `AQ.` 兩種樣式 |
