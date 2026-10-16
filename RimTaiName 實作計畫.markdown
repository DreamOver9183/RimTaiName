# RimTaiName 實作計畫（依《開發計畫書 v3》）

*   **建立日期**：2026-10-16
*   **依據**：`RimTaiName 開發計畫書 v3.markdown`

## 進度

- [x] 實作計畫核准並存入專案資料夾（2026-10-16）
- [ ] 準備工作：git init、`.gitignore`、第一個 commit
- [ ] Step 0 收尾：新生兒命名視窗
- [ ] Step 1：基礎建設、設定頁與 API Key
- [ ] Step 2：翻譯管線（純 C#）＋ 單元測試
- [ ] Step 3：Gemini 用戶端
- [ ] Step 4：命名視窗正式版（MVP 完成）
- [ ] Step 5：字典 v1.0
- [ ] Step 6：第二階段（生成中文名）
- [ ] Step 7：上架 Steam 創意工坊

## Context

v3 計畫書（`RimTaiName 開發計畫書 v3.markdown`，2026-10-16）已定案設計與路線圖：Step 0 驗證版已通過（新生兒視窗待測），Step 1～7 未開始。本文件把 v3 第 11 節的路線圖展開成可直接執行的工作清單：每一步要建立／修改哪些檔案、類別的職責與介面、重用哪些既有程式、以及怎麼驗證。目標是依序完成 MVP（Step 1～4）、字典 v1.0（Step 5）、第二階段（Step 6），最後上架 Workshop（Step 7）。

使用者已決定：
* 實作前先 `git init`，每個 Step 驗證通過後 commit（只在本機）。
* 計畫核准後另存一份到專案資料夾：`RimTaiName 實作計畫.markdown`，之後每完成一步就在上面打勾。

---

## 已查證的事實（2026-10-16，本機 1.6.4871）

| 項目 | 結果 | 對實作的影響 |
| :--- | :--- | :--- |
| `System.Security.dll` | 存在，含 `ProtectedData`、`DataProtectionScope`，以 P/Invoke 呼叫 `CryptProtectData` | DPAPI 可行；非 Windows 會丟例外 → 退回「只在本次遊戲期間記憶」 |
| `System.Xml.dll`、`System.Xml.Linq.dll` | 存在 | 個人快取用 `XDocument` 自行讀寫，不用 `Scribe`，管線維持純 C# |
| `mscorlib` 正規化 | 含 `load_normalization_resource`、`IsNormalized` | `string.Normalize(NFC/NFKC)` 可用；仍在實機測一次 |
| `GenFilePaths.ConfigFolderPath` | 存在 | 金鑰檔與快取檔的位置 |
| `Window.closeOnClickedOutside` 等 | 公開欄位 | 請求在途時暫設為 `false` |
| `Dialog_NamePawn` 的「確定」 | **沒有**覆寫 `OnAcceptKeyPressed`；按鈕或 Enter 在 `DoWindowContents` 內呼叫 `BuildName()` → `name.IsValid` → `pawn.Name = name` → `WindowStack.TryRemove(this)` | 偵測「確定」改為：`DoWindowContents` Postfix 比對 `pawn.Name` 參考是否已改變 |
| `Dialog_NamePawn` 建構子旗標 | `forcePause`、`absorbInputAroundWindow`、`closeOnClickedOutside = true`、`closeOnAccept = false` | 等待中按 Enter 會直接以目前內容確定；晚到的結果只進快取（符合 v3 §8.1） |
| `Dialog_NamePawn` 私有欄位 | `pawn`、`names`、`size` 都在 | `NamePawnAccess` 需新增 `pawn` 存取委派 |
| 字典校對佇列 | `review_queue.csv` 541 筆：Tier1 68、Tier2 19、Tier3 430、Tier4 24；`review` 欄每次重新產生都是空的 | Step 5 需要「已校對保留」清單，否則無法判定「已處理」 |
| 建置環境 | .NET SDK 10.0.402 | 不變 |

---

## 對 v3 的補充修正（實作時採用，同步回寫 v3）

1. **模型代號驗證：** v3 的 `^[a-z0-9.-]+$` 會放行 `..`。改為 `^[a-z0-9]+(?:[.-][a-z0-9]+)*$`；使用者貼上 `models/xxx` 時自動去掉前綴。
2. **測試連線：** 改呼叫 `GET /v1beta/models/{model}`，一次驗證金鑰與模型代號（200 正確、400/403 金鑰問題、404 模型不存在），不需列出整份清單。
3. **金鑰讀取者：** 測試連線併入 `GeminiInteractionsClient`，刪除 `Spike/ConnectivityProbe.cs`。可讀取金鑰的只有兩處：`GeminiInteractionsClient`（透過注入的 `Func<string>`，送出時才取用）與設定頁的金鑰欄位（遮罩顯示末 4 碼、按「顯示」才露出）。程式碼審查以此為準。
4. **xUnit 測試專案提前到 Step 1 建立：** 第 9 節要求 `KeyRedactor` 有單元測試，屬於 Step 1 的驗證項目。
5. **`NameRequestPayload` 放在 `Pipeline/`：** 它是純資料，放在 `Pipeline/` 讓測試專案只要連結 `Pipeline/**` 與 `Llm/**`。
6. **429 重置時間：** 不依賴 `TimeZoneInfo` 的時區代號（Mono 在 Windows 上不可靠），自行以美國夏令規則計算太平洋時間午夜，換成本機時間顯示。
7. **字典校對狀態：** 新增 `NameTable/translations/review_accepted.csv`（namespace, text），列出「已檢查、保留原譯」的條目；`merge_dictionary.py` 把它們排除在 `review_queue.csv` 外。

---

## 共通規則（每一步都適用）

* **相容性：** `Pipeline/`、`Llm/`、`Core/KeyRedactor.cs` 要同時能在 net472（遊戲 Mono）與 net10.0（測試）編譯。不使用 `record`、`init`、`Span`、`System.Text.Json`、`HashCode`、`string.Contains(char)` 等 net472 沒有的 API；不引用任何遊戲型別。
* **執行緒：** 背景只產生純資料、寫入記憶體快取（加 lock）。遊戲 API（`Messages`、`Find.*`、`Translate()`、寫入 `current`、快取存檔）只在主執行緒。
* **記錄：** 所有記錄經由新的 `Core/ModLog.cs`（包裝 `Log.Message/Warning/Error`），一律先過 `KeyRedactor`。
* **語言檔：** 新字串同時加到 `Languages/ChineseTraditional` 與 `Languages/English` 的 `RimTaiName_Keys.xml`；移除 `RimTaiName_Spike_*` 鍵。
* **每一步的收尾流程：**
  1. `dotnet build Source/RimTaiName/RimTaiName.csproj -c Release`：0 警告、0 錯誤。
  2. `dotnet test Source/RimTaiName.Tests`：全數通過（Step 1 起）。
  3. `Tools/package_mod.ps1` 打包 → 使用者在測試機實測 → `Player.log` 放進 `TestLog/` → 我檢查。
  4. 更新 `RimTaiName 實作計畫.markdown` 的勾選與 v3 的「現況總覽」，然後 git commit（訊息結尾附 Co-Authored-By）。

---

## 準備工作（Step 1 之前）

1. 把本計畫存成 `D:\Claude Code\RimTaiName\RimTaiName 實作計畫.markdown`。
2. `git init`，建立 `.gitignore`：
   * 忽略：`Source/**/obj/`、`Source/**/bin/`、`1.6/Assemblies/*.dll`（可重建）、`dist/`、`Tools/__pycache__/`、`NameTable/**/raw/`、`TestLog/`（含測試機路徑等個資）。
   * 保留：計畫書、`Dictionary/`、`NameTable/` 的 CSV（修正清單是真正的原始資料）、`Tools/`、`Source/`。
3. 第一個 commit：Step 0 驗證版現狀。

---

## Step 0 收尾：新生兒命名視窗

* **不改程式。** 請使用者在測試機觸發嬰兒出生（自然分娩，或開發者模式下對懷孕小人使用分娩相關的除錯動作），從出生信件開啟命名視窗。
* **確認：** 鎖頭欄對齊三列；工具列位於文字框與「取消／確定」之間；姓欄的「建議...」選單仍可用；`Player.log` 無 `[RimTaiName]` 錯誤。
* 若版面錯位，修正 `Patch_NameContext_MakeRow` 的欄寬計算後再測。

---

## Step 1：基礎建設、設定頁與 API Key

**新增／修改檔案：**

| 檔案 | 內容 |
| :--- | :--- |
| `Core/RimTaiNameSettings.cs` | `ModSettings`：`modelId`（預設 `gemini-3.5-flash-lite`）、`minIntervalSeconds`（3）、`keepVanillaAddress`（保留原版稱呼，預設 true）。`ExposeData` **不含金鑰** |
| `Core/ApiKeyStore.cs` | `Config/RimTaiName_ApiKey.dat`；`ProtectedData.Protect(..., CurrentUser)`。首次使用時做一次加解密往返探測，失敗則切到「僅本次遊戲」模式（金鑰只存在私有靜態欄位）。API：`HasKey`、`Last4`、`Save(string)`、`Clear()`、`internal Read()`、`Mode`（Dpapi／SessionOnly） |
| `Core/KeyRedactor.cs` | 純 C#：`Redact(string text, string knownKey = null)`；把 `AIza[0-9A-Za-z_-]{35}` 與 `knownKey` 的完整值換成 `AIza…` 加末 4 碼 |
| `Core/ModLog.cs` | 記錄包裝，全部經 `KeyRedactor` |
| `Core/RimTaiNameMod.cs` | 移除 `ConnectivityProbe.Start()`；`GetSettings<RimTaiNameSettings>()`；改寫 `DoSettingsWindowContents`（見下） |
| `Pipeline/PersonalNameCache.cs` | `XDocument` 讀寫 `RimTaiName_Cache.xml`；欄位 namespace／key／value／origin；`TryGet`、`Put`（加 lock、設 dirty）、`SaveIfDirty`（主執行緒呼叫）、`Clear`、`Count`。檔案路徑由呼叫端傳入 |
| `Source/RimTaiName.Tests/` | xUnit、net10.0；`<Compile Include="..\RimTaiName\Pipeline\**\*.cs;..\RimTaiName\Llm\**\*.cs;..\RimTaiName\Core\KeyRedactor.cs" Link=...>`。**建立前徵求同意下載 NuGet：`xunit`、`xunit.runner.visualstudio`、`Microsoft.NET.Test.Sdk`** |
| `Tools/package_mod.ps1` | 只複製 `About`、`LoadFolders.xml`、`1.6`、`Languages`、`Dictionary`、`Prompts` 到 `dist/RimTaiName/`；以 ASCII 讀取所有檔案（含 DLL）掃描 `AIza[0-9A-Za-z_-]{35}`，命中即刪除輸出並以非零碼結束；另產生 `dist/RimTaiName.zip` 方便傳到測試機。相容 Windows PowerShell 5.1 |
| `RimTaiName.csproj` | `GameReference` 加入 `System.Security;System.Xml;System.Xml.Linq` |
| `Spike/ConnectivityProbe.cs` | 暫時改寫成設定頁的「測試連線」：帶金鑰（經 `ApiKeyStore.Read`）呼叫 `GET models/{model}`、`AllowAutoRedirect = false`、錯誤訊息經 `ModLog`。Step 3 併入 `GeminiInteractionsClient` 後刪除 |

**設定頁（`Listing_Standard`）：**
* API Key：預設顯示 `●●●●` + 末 4 碼；按鈕「貼上」（讀 `GUIUtility.systemCopyBuffer`，去頭尾空白後 `ApiKeyStore.Save`）、「顯示／隱藏」（顯示時為可編輯文字框，關閉設定頁即恢復遮罩）、「清除金鑰」。
* 儲存方式說明：DPAPI 加密／僅本次遊戲（含原因）。
* 「測試連線」與結果文字（金鑰正確／金鑰錯誤或無權限／模型不存在／網路錯誤）。
* 模型代號文字框（套用修正 1 的驗證，不合法時以紅字提示且不儲存）、最短間隔、保留原版稱呼。
* 快取：目前筆數、「清除快取」、「開啟快取資料夾」（`Application.OpenURL` 開資料夾）。
* 隱私與金鑰安全說明（v3 §9「給玩家的建議」四點）。

**單元測試：** `KeyRedactor`（AIza 樣式、knownKey、多個金鑰、無金鑰字串不變、例外訊息）；`PersonalNameCache`（存讀往返、namespace 隔離、user 覆寫 api、損壞檔案時從空快取開始）。

**驗證：**
* 設定可存可讀；Mod 設定 XML（`Config/Mod_*_RimTaiNameMod.xml`）不含金鑰；`RimTaiName_ApiKey.dat` 不是明文。
* 本機與測試機各確認設定頁顯示的儲存方式為 DPAPI（即 `ProtectedData` 實測可用）。
* 測試連線能分辨：正確金鑰、錯誤金鑰、不存在的模型。
* 打包：正常打包成功；放一個假金鑰到 `Languages/` 測試檔中，打包必須中止。
* 打包後的資料夾在測試機能直接載入；搜尋 `Player.log` 與 Config 資料夾，沒有金鑰明文。

---

## Step 2：翻譯管線（純 C#）＋ 單元測試

**檔案（全部在 `Pipeline/`）：**

| 類別 | 職責 |
| :--- | :--- |
| `NameRequestPayload` / `NameResult` | v3 §5.4 資料契約；`FieldSource` 列舉：Unchanged／SpecialForm／Alias／PersonalCache／Dictionary／Api／Failed，另加 `Pending`（待 API） |
| `NameKey` | `Trim` → NFC → `ToLowerInvariant`；`NameSpace` 列舉 Name／Nick |
| `SpecialFormRule` | 對應 `name_rules.special_form()`：NFKC、2～4 大寫保留、單字母 → 名姓保留／暱稱「阿」+字母 |
| `NameSanitizer` | v3 §8.4：NFKC → Trim → 移除非法字元 → 拉丁字母只允許原文中的大寫（對應 `latin_ok()`）→ 長度（名姓 12、暱稱 16）→ `^[\p{L}0-9 '\-.]*$`；失敗回傳 null，不截斷 |
| `NickAliasResolver` | v3 §6.3：依開窗時的 `NickSet`、`Nick`、名、姓判定 `Explicit`／`AliasOfFirst`／`AliasOfLast`；翻譯時套用「填入對應欄位最終值」與 `keepVanillaAddress` |
| `BuiltInDictionary` | 載入 `Dictionary/BaseNames_zhTW.tsv`（略過標頭，`Lazy` 首次使用時載入）；`TryGet(ns, key)` |
| `NameProcessorService` | `ResolveLocal(payload)`：依 v3 §6.1 順序（空白 → 已含中文 → 特殊字形 → 暱稱同名 → 個人快取 → 字典）逐欄解析，未解析者標為 `Pending`；`ResolveRemoteAsync(payload, local, ILlmClient, ct)`：剩餘欄位一次送出，結果過 `NameSanitizer`，成功者寫入快取（origin = api），最後重算暱稱別名 |
| `Llm/ILlmClient.cs` | 介面先定義（Step 3 實作）：`Task<LlmResponse> TranslateAsync(LlmRequest, CancellationToken)` |

**一致性測試資料：** 新增 `Tools/export_rule_fixtures.py`，從 `dictionary_draft.csv` 匯出全部 24,596 筆加上手寫邊界案例到 `Source/RimTaiName.Tests/Fixtures/rules.tsv`，欄位：text、namespace、`NameKey` 預期值、`special_form()` 結果、`final_zh`、Python 判定是否合格（無「空白／含非中文字元／超過長度上限」）。

**測試內容：**
* 與 Python 一致：每一列的鍵正規化、特殊字形、合格判定完全相同。
* 字典健全：每個字典值都能通過 `NameSanitizer`（不然遊戲內會被拒絕）。
* 暱稱四種來源與空白暱稱；`keepVanillaAddress` 開／關。
* 各種鎖定組合（鎖定欄位永不改變，但可作為別名來源）。
* 非法字元、超長、全形字母（ＭＪ → MJ）、拉丁字母規則。
* `name:` 與 `nick:` 隔離（Fox 名 → 福克斯、暱稱 → 狐狸；暱稱查不到不回退）。
* `ResolveRemoteAsync` 用假 `ILlmClient`：部分成功、清理失敗、例外。

**驗證：** `dotnet test` 全數通過；主專案 0 警告 0 錯誤。

---

## Step 3：Gemini 用戶端

**開工前：** 以 WebFetch 查 Interactions API reference 與模型頁（v3 附錄 C），確認請求欄位、`response_format` 寫法、回應結構與 `gemini-3.5-flash-lite` 仍可用；與 v3 不同處先回寫 v3 §7。

**檔案：**

| 檔案 | 內容 |
| :--- | :--- |
| `Prompts/NameRules_zhTW.txt` | 從 `Tools/name_rules.py` 的 `TRANSLATION_RULES` 移出「【規則】」與「【範例】」兩段（共用部分） |
| `Tools/name_rules.py` | 改為讀取上述檔案，再拼上自己的開頭與「【輸入欄位】」說明；**組出的字串必須與移出前逐字相同**（以 diff 驗證） |
| `Llm/MiniJson.cs` | 最小 JSON 序列化／解析（物件、陣列、字串跳脫、數字、布林、null） |
| `Llm/PromptBuilder.cs` | 讀規則檔 + C# 端的輸入格式（三欄文字、角色、是否鎖定、暱稱來源、範例姓名）；輸出 JSON Schema（`first`／`nick`／`last` 必填字串） |
| `Llm/GeminiInteractionsClient.cs` | 靜態 `HttpClient`（`HttpClientHandler { AllowAutoRedirect = false }`、逾時 15 秒）；端點常數 `https://generativelanguage.googleapis.com/v1beta/interactions`；`store: false`；`x-goog-api-key` 標頭；不傳 thinking 與取樣參數；5xx 等 2 秒重試 1 次、429 不重試；`CheckModelAsync()`（測試連線，取代 `ConnectivityProbe`）；回傳分類好的錯誤（`InvalidKey`、`Forbidden`、`BadRequest`、`ModelNotFound`、`RateLimited`、`ServerError`、`Timeout`、`Network`） |
| `Llm/RequestGate.cs` | 全域只允許 1 個請求在途，最短間隔可設定 |
| `Llm/QuotaResetClock.cs` | 計算下一次太平洋時間午夜（含夏令規則），供 429 提示 |
| `Spike/` | 刪除整個資料夾 |

**單元測試（以假的 `HttpMessageHandler` 提供回應）：** 正常解析、回應不是 JSON、缺欄位、各錯誤碼分類、5xx 重試一次、429 不重試、302 視為錯誤不跟隨；模型代號含 `/`、`?`、`..`、大寫、空字串時拒絕送出；`QuotaResetClock` 在夏令切換前後的結果。

**驗證：**
* 遊戲內以使用者金鑰實際翻譯一次（可先在設定頁加一個開發者模式才顯示的「翻譯測試」）。
* 故意製造錯誤：錯誤金鑰、斷網、不存在的模型 → 之後全文搜尋 `Player.log` 與 Config 資料夾，沒有金鑰明文。
* 程式碼審查：`ApiKeyStore.Read` 只出現在 `RimTaiNameMod` 的注入點與設定頁金鑰欄位。

---

## Step 4：命名視窗正式版

**修改 `UI_Patches/`：**

| 檔案 | 改動 |
| :--- | :--- |
| `NamePawnAccess.cs` | 新增 `Pawn` 欄位存取委派 |
| `DialogState.cs` | 擴充：`OriginalName`（開窗時的 `pawn.Name` 參考）、`NickSource`、`Snapshot`、`Locked`、`LastWritten[3]`（模組寫入的值）、`SourceText[3]`（被翻譯的原文）、`RequestId`、`InFlight`、`ValuesAtRequest[3]`、`volatile PendingResult`、`SavedCloseOnClickedOutside` |
| `Patch_Dialog_NamePawn.cs` | 見下 |

**流程：**
1. **建構子 Postfix：** 記錄 `OriginalName`、三欄初值；用 `NickAliasResolver` 判定暱稱來源。
2. **按「翻譯姓名」（主執行緒）：** 讀三欄目前值與鎖頭 → `ResolveLocal` → 本機結果放進 `PendingWrite` → 若還有 `Pending` 欄位：
   * 沒有金鑰 → 提示「原版名字仍可用內建字典翻譯」，該欄不變。
   * `RequestGate` 不允許 → 提示稍後再試。
   * 否則 `RequestId++`、記下 `ValuesAtRequest`、`closeOnClickedOutside = false`、按鈕顯示「處理中...」並停用，`Task.Run(ResolveRemoteAsync)`；完成時只設定 `PendingResult`（帶 RequestId），快取在背景以 lock 寫入記憶體。
3. **`DoWindowContents` Prefix（僅 `EventType.Layout`）：**
   * 套用 `PendingWrite`（沿用驗證版 `ApplyPending`）。
   * 若有 `PendingResult` 且 RequestId 是最新：只覆寫「未鎖定、且目前值仍等於 `ValuesAtRequest`」的欄位；失敗欄位依錯誤分類以 `Messages.Message(..., MessageTypeDefOf.RejectInput, false)` 提示（429 附重置時間）；恢復 `closeOnClickedOutside`；`cache.SaveIfDirty()`。
   * 開窗時也呼叫一次 `SaveIfDirty()`，處理前一個視窗關閉後才到達的結果。
4. **`DoWindowContents` Postfix：** 若 `pawn.Name` 參考已不同於 `OriginalName`（玩家按了確定）→ 學習：對每個「`LastWritten` 非空、最終值不同、含中文且通過 `NameSanitizer`」的欄位，以 `SourceText` 為鍵寫入快取（origin = user）；暱稱為別名時不學習 → `SaveIfDirty()`。
5. **「還原」：** 未鎖定欄位恢復 `Snapshot`，清掉 `LastWritten`。
6. **工具列：** 驗證版三個按鈕換成「翻譯姓名」與「還原」兩個（Step 6 再加「生成中文名」）。

**驗證（手動，本機＋測試機）：**
* 成年殖民者、新生兒、開發者模式下的視窗。
* 動物與機械體的視窗沒有任何模組 UI。
* 等待中按 ESC、點視窗外（不應關閉）、按 Enter（以目前內容確定，結果晚到只進快取）。
* 全鎖定、部分鎖定、還原；等待中手動改某欄（不應被覆寫）。
* 沒有 API Key：原版名字全由字典翻譯；Mod 名字顯示提示。
* 修改模組譯名後按確定 → 再開同名小人時使用玩家的修正。
* 全形字母名字（ＭＪ）、已含中文的欄位、混合名。
* 測試機 386 個 Mod 環境下正常；`Player.log` 無錯誤且無金鑰。

**MVP 完成點：** Step 4 通過後 commit 並在 v3 現況總覽標記 MVP 完成。

---

## Step 5：字典 v1.0

1. 修改 `Tools/merge_dictionary.py`：讀取新的 `review_accepted.csv`，排除在 `review_queue.csv` 外。
2. 匯出 Tier 2（624 詞）與 Tier 4（549 詞）全部條目為校對表；我先逐筆預審並標出建議修改，使用者確認。
3. 確認的修改寫入 `review_corrections.csv`／`official_overrides.csv`；保留原譯者寫入 `review_accepted.csv`；Tier 1 的 68 筆旗標（已校對過）直接列入 `review_accepted.csv`。
4. 從字典挑 Prompt 範例詞（名、姓、暱稱、日文、中文拼音、複合字名各數個），更新 `Prompts/NameRules_zhTW.txt` 的【範例】。
5. `python -I Tools/merge_dictionary.py` 重新產生字典；重跑 `export_rule_fixtures.py` 與 `dotnet test`。

**驗證：** `review_queue.csv` 中沒有 Tier 1、2、4 的列；字典健全測試通過。

---

## Step 6：第二階段（生成中文名）

**開工前：** 對照 1.6 反編譯原始碼確認 `PawnContext` 用到的 API（性別、`story.Childhood/Adulthood` 標題、`Ideo.culture`、`genes.Xenotype`、`DevelopmentalStage`）。

* `UI_Patches/PawnContextReader.cs`（主執行緒擷取）→ `Pipeline/PawnContext`（純資料）。
* `NameRequestPayload.Mode = Generate`；`NameProcessorService` 的生成路徑：跳過字典與快取，直接送 API；鎖定欄位作為風格參考且不變。
* `PromptBuilder` 加入生成規則（v3 §7.4）：只生成音譯／意譯風格、維持「名 姓」順序、暱稱依背景意譯或設為別名。
* 生成結果**不寫入快取**（沒有原文可當鍵）。
* 工具列加「生成中文名」；同樣受 `RequestGate`、`RequestId`、鎖頭保護。

**驗證：** 男／女、不同背景、不同文化與異種族各生成數次，確認風格、鎖定欄位不變、長度與字元限制；單元測試涵蓋 Generate 的 payload 與 Prompt 組裝。

---

## Step 7：上架 Steam 創意工坊

1. 使用者決定正式 `packageId` 與作者名稱 → 更新 `About.xml`（名稱、說明、`packageId`）與 `RimTaiNameMod.HarmonyId`。
2. 製作 `About/Preview.png`（使用者提供或確認）。
3. 撰寫 Workshop 說明（`Docs/Workshop_zhTW.txt`、英文版）：功能、不需金鑰即可翻譯原版名字、API Key 申請教學、第 9 節安全建議、隱私與額度。
4. `Tools/package_mod.ps1` 打包且金鑰掃描通過；**上傳由使用者在遊戲內的 Workshop 上傳功能執行**。
5. 1.7 unstable 實測（可在 Step 4 之後任何時間做）：通過才更新 `supportedVersions` 與 `LoadFolders.xml`。

---

## 端對端驗證總表

| 時機 | 指令／動作 |
| :--- | :--- |
| 每次改程式 | `dotnet build Source/RimTaiName/RimTaiName.csproj -c Release` |
| Step 1 起 | `dotnet test Source/RimTaiName.Tests` |
| 改規則或字典後 | `python -I Tools/merge_dictionary.py`、`python -I Tools/export_rule_fixtures.py`、`dotnet test` |
| 交付測試機 | `powershell -File Tools/package_mod.ps1` → 複製 `dist/RimTaiName.zip` → 測試 → `TestLog/Player.log` |
| 金鑰稽核（Step 1、3、4、7） | 全文搜尋 `Player.log`、Config 資料夾、`dist/` 中的金鑰明文與 `AIza` 樣式 |
