# RimTaiName 專案知識庫

RimWorld 1.6 Mod：在原版命名視窗（`Dialog_NamePawn`）加上鎖頭與「翻譯姓名」，把小人的英文姓名轉成臺灣繁體中文譯名。翻譯順序為個人快取 → 內建字典 → Gemini API。

*   **設計與決策：** `RimTaiName 開發計畫書 v3.markdown`（唯一的設計依據，v1／v2 只是歷史）。
*   **工作清單與進度：** `RimTaiName 實作計畫.markdown`。完成一個 Step 就打勾，並同步更新 v3 §2「現況總覽」。
*   **資料來源說明：** `NameTable/README.md`、`NameTable/translations/README.md`。
*   使用者以臺灣繁體中文溝通；文件、註解以外的說明一律用繁體中文。

---

## 環境

| 項目 | 值 |
| :--- | :--- |
| 開發機遊戲 | `D:\SteamLibrary\steamapps\common\RimWorld`（1.6.4871 rev590）；`Mods\RimTaiName` 以 junction 連到本專案 |
| 遊戲 DLL | `RimWorldWin64_Data\Managed\`；反編譯原始碼：https://github.com/Dyyrlysh/RimworldDecompile |
| Harmony | 開發機 2.4.3（Workshop 2009463077）；**測試機 2.4.2，只能用 2.4.2 已有的 API** |
| 測試機 | 另一台電腦，`E:\SteamLibrary`，386 個 Mod。使用者回傳 `Player.log` 到 `TestLog/`（已 gitignore） |
| SDK | .NET 10 SDK 10.0.402；Python 3.12；gh 2.98（帳號 DreamOver9183）；Antigravity CLI `%LOCALAPPDATA%\agy\bin\agy.exe` |
| 遠端 | https://github.com/DreamOver9183/RimTaiName（**Public**），預設分支 `main` |

---

## 架構規範

```
Source/RimTaiName/
├── Core/        Mod 進入點、設定、ApiKeyStore、KeyRedactor、ModLog（可用遊戲型別；KeyRedactor 除外）
├── Pipeline/    翻譯管線，純 C#
├── Llm/         Gemini 用戶端，純 C#
└── UI_Patches/  Harmony patch 與視窗狀態（遊戲型別）
Source/RimTaiName.Tests/   xUnit，net10.0，以連結方式編譯 Pipeline/**、Llm/**、Core/KeyRedactor.cs
```

*   **純 C# 層（`Pipeline/`、`Llm/`、`Core/KeyRedactor.cs`）：**
    *   不引用 `Verse`、`RimWorld`、`UnityEngine`、`HarmonyLib`，也不自己寫記錄；錯誤以回傳值交給呼叫端。
    *   必須同時能在 net472（遊戲 Mono）與 net10.0（測試）編譯。禁用：`record`、`init`、`Span`、`System.Text.Json`、`HashCode`、`string.Contains(char)`、`ServicePointManager`（net10 過時）。
    *   檔案路徑由呼叫端傳入，不自己找 `GenFilePaths`。
*   **執行緒：** 背景執行緒只產生純資料、寫入記憶體快取（加 lock）。`Messages`、`Find.*`、`Translate()`、寫入 `NameContext.current`、快取存檔只在主執行緒。
*   **零存檔污染：** 不新增 `ThingComp`、`WorldComponent`、`GameComponent`，只透過原版流程寫入 `Pawn.Name`。
*   **UI：** 只用純文字、Keyed 語言檔與原版 `Widgets`（IMGUI 字型不支援 Emoji）。新字串同時加到 `Languages/ChineseTraditional` 與 `Languages/English` 的 `RimTaiName_Keys.xml`。
*   **記錄：** 一律經 `Core/ModLog`，訊息先過 `ApiKeyStore.Redact`。訊息前綴 `[RimTaiName]`。

### API Key 規則（不可違反）

*   **可讀取金鑰明文的程式只有三處：** `ApiKeyStore` 內部（含 `Redact`）、`GeminiInteractionsClient`（注入的 `Func<string>`，送出時才取用）、設定頁按「顯示」時。
*   金鑰只放 `x-goog-api-key` 標頭，絕不放網址、記錄、例外訊息、Prompt、Mod 設定 XML。
*   端點寫死 `https://generativelanguage.googleapis.com`；`AllowAutoRedirect = false`；不覆寫 TLS 驗證；模型代號必須符合 `^[a-z0-9]+(?:[.-][a-z0-9]+)*$`。
*   金鑰樣式：舊式 `AIza[0-9A-Za-z_-]{35}`；2026-05 起 AI Studio 新金鑰 `AQ\.[0-9A-Za-z_.-]{20,}`（實測長度 53，打包掃描用這個寬鬆版）。`KeyRedactor` 的 `AQ.` 樣式要求結尾不是 `.`，以免吞掉句號。遮蔽格式為「前綴…末 4 碼」。
*   Windows 用 DPAPI（`ProtectedData`，CurrentUser）存 `Config/RimTaiName_ApiKey.dat`；非 Windows 一律只在本次遊戲期間記憶。
*   **開發用金鑰：** 專案根目錄的 `RimTaiName_KEY_01.txt`（gitignore 規則 `*_KEY_*.txt`）。只能由腳本或測試在程式內讀取，**絕不 `cat`／`Get-Content` 印出、不貼進對話、不寫入任何檔案或 PR**。檢查格式時只輸出布林值與長度。
*   **金鑰驗證：** 只接受可列印 ASCII（`!`～`~`）、20～256 字，不檢查前綴（金鑰要放進 HTTP 標頭，且 Google 改過格式）。
*   **測試中的假金鑰：** 一律在執行時串接組出（例如 `"AI" + "za" + new string('x', 31) + …`），原始碼裡不得出現符合金鑰樣式的字面值；本 repo 是 Public，GitHub secret scanning 會攔截或警示。

---

## 常用指令

PowerShell 5.1 沒有 `&&`；以下指令在專案根目錄執行。

| 用途 | 指令 |
| :--- | :--- |
| 建置（typecheck） | `dotnet build Source/RimTaiName/RimTaiName.csproj -c Release -warnaserror` |
| C# lint | `dotnet format Source/RimTaiName/RimTaiName.csproj --verify-no-changes`（測試專案同理） |
| 單元測試 | `dotnet test --project Source/RimTaiName.Tests`（Microsoft.Testing.Platform，由根目錄 `global.json` 啟用） |
| 即時 API 測試 | `$env:RIMTAINAME_KEY_FILE = (Resolve-Path RimTaiName_KEY_01.txt).Path; dotnet test --project Source/RimTaiName.Tests --filter-trait "Category=Live"; $env:RIMTAINAME_KEY_FILE = $null`。每次用掉約 4 次請求額度；未設環境變數時這些測試自動略過 |
| 語言檔 lint | `python -I Tools/check_keys.py`：兩種語言的鍵與 `{0}` 一致、程式引用的 `"RimTaiName_…"` 都有定義。用列舉組出的動態鍵要登記在腳本的 `DYNAMIC` |
| Python 語法檢查 | `python -I -m py_compile Tools/<file>.py`（沒有安裝 ruff／pyflakes） |
| PowerShell 語法檢查 | `[System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$errors)`（沒有 PSScriptAnalyzer） |
| 打包 | `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/package_mod.ps1` → `dist/RimTaiName/`、`dist/RimTaiName.zip`（含建置與金鑰掃描；`-NoBuild` 略過建置） |
| 金鑰稽核（測試機） | 把 `Tools/audit_key_leak.ps1` 複製到測試機，在 **PowerShell**（不是 cmd）執行 `powershell -NoProfile -ExecutionPolicy Bypass -File audit_key_leak.ps1`；結束碼 0 乾淨、1 找到金鑰、2 找不到資料夾、3 有檔案讀不到。自我測試：`Tools/test_audit_key_leak.ps1`（13 個情境） |
| 打包腳本自我測試 | `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/test_package_mod.ps1`（改動打包腳本時執行，11 個情境） |
| 重新產生字典 | `python -I Tools/merge_dictionary.py` |
| 規則一致性 fixture | `python -I Tools/export_rule_fixtures.py` → `Source/RimTaiName.Tests/Fixtures/rules.tsv`。改 `name_rules.py` 或重新產生字典後都要重跑，再 `dotnet test` 確認 C# 的 `NameKey`／`SpecialFormRule`／`NameSanitizer.Check` 與 Python 逐筆一致 |
| 批次翻譯名字 | `python -I Tools/agy_translate.py <in.csv> <out.csv> --workers 8`（可接續） |
| PR 自動審查 | `python -I Tools/agy_review.py <PR#> --post` |
| 遊戲記錄 | 開發機 `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log` |

---

## 開發流程 SOP（每完成一個獨立模組或可測試單元，依序執行，不可跳過）

1.  **自我審查與測試：** 在功能分支（`feat/<短名>`、`fix/<短名>`）上執行 build（`-warnaserror`）、`dotnet format --verify-no-changes`、`dotnet test --project Source/RimTaiName.Tests`、`python -I Tools/check_keys.py`，以及變更檔案的 Python／PowerShell 語法檢查；改到 `Llm/` 時加跑即時 API 測試，改到打包腳本時跑 `test_package_mod.ps1`；自己讀一次 diff 做初步 Code Review。
2.  **發起 PR：** `git push -u origin <branch>`，`gh pr create --base main`。內文附變更摘要與測試結果，結尾加 `🤖 Generated with [Claude Code](https://claude.com/claude-code)`。
3.  **雙軌審核：**
    *   先請 Copilot 審查：`gh pr edit <PR#> --add-reviewer "@copilot"`，再以 `gh pr view <PR#> --json reviews,comments` 檢查是否完成。
    *   Copilot 回報額度耗盡、未回應或無法使用時，改用 `python -I Tools/agy_review.py <PR#> --post`（Antigravity CLI，`gemini-3.8-flash-high`，以子代理平行審查安全性、邊界條件、架構合規）。
4.  **修正與合併：** 審核意見僅供參考，自行判斷是否採納；修正推上同一分支後 `gh pr merge <PR#> --squash --delete-branch`，再 `git switch main`、`git pull`。
5.  **提煉更新知識庫：** 只把本次「新增的架構決策、新慣例、踩到的技術坑」寫進本檔，直接在 main commit 並 push。

其他慣例：
*   Commit 訊息用繁體中文，結尾加 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`。在 PowerShell 中先把訊息寫成檔案再 `git commit -F <file>`。
*   需要遊戲內驗證的項目，打包後交給使用者在測試機實測，`Player.log` 放進 `TestLog/` 再檢查。
*   雜事可交給 Antigravity CLI；呼叫 Gemini 時盡量讓它用子代理（`invoke_subagent`）平行處理。

---

## 設計模式

*   **逐類別套用 Harmony patch：** `RimTaiNameMod` 建構子對每個 `[HarmonyPatch]` 類別分別 `CreateClassProcessor(type).Patch()` 並 try/catch，一個失敗不影響其他。
*   **反射存取：** `NamePawnAccess` 在啟動時以 `AccessTools.FieldRefAccess` 建立委派（私有巢狀類別 `NameContext` 用 `AccessTools.Inner`），不使用 Publicizer。
*   **每個視窗的狀態：** `ConditionalWeakTable<Dialog_NamePawn, DialogState>`；`DialogStateRegistry.Drawing` 記錄目前正在繪製的視窗（IMGUI 單執行緒）。
*   **寫入文字框：** 只在 `DoWindowContents` Prefix 且 `Event.current.type == EventType.Layout` 時寫入 `NameContext.current`，寫入前先 `UI.UnfocusCurrentTextField()`。
*   **插入工具列：** Transpiler 以 `CodeMatcher` 找 `rectAggregator.NewRow(17.5f)`，在前面插入 `ToolbarDrawer.Draw(this, ref rectAggregator)`；找不到時只停用工具列。
*   **設定檔類寫入：** 先寫 `<path>.tmp`，目標存在時 `File.Replace(temp, path, null)`、否則 `File.Move`，`finally` 刪除殘留的暫存檔。讀檔失敗時從空狀態開始，不讓 Mod 載入中斷。
*   **讀檔失敗要分兩種：** 內容損壞（`XmlException`）→ 改名為 `.corrupt` 後從空狀態開始；暫時讀不到（`IOException`、`UnauthorizedAccessException`，例如防毒鎖定）→ 保留原檔並標記，存檔前先重讀合併，仍讀不到就拒絕覆寫。否則玩家累積的資料會被新存檔蓋掉。
*   **自訂 XML 檔：** 一律 `XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })` 再 `XDocument.Load(reader)`。存在屬性裡的文字不得含 Tab／CR／LF（屬性正規化會把它們變成空白，讀回後鍵值對不上）。
*   **可測試的 HTTP 用戶端：** 正式建構子建立 `HttpClientHandler { AllowAutoRedirect = false }`；`internal` 建構子注入 `HttpMessageHandler`、逾時與重試延遲，測試用假 handler（延遲要尊重 `CancellationToken`，否則 HttpClient 逾時測不到）。
*   **UI 觸發的背景請求：** 主執行緒建立 `CancellationTokenSource` 並遞增世代編號，再 `Task.Run`；背景只在世代仍相符時寫入 volatile 結果欄位。金鑰變更、關閉頁面時呼叫 reset（遞增世代、取消、清除結果）。結果的記錄與 `Translate()` 都在主執行緒第一次繪製結果時進行（`Verse.Log` 是遊戲 API）。
*   **受限的文字輸入：** 用 `Widgets.TextField(rect, text, maxLength, regex)`，超長或不符的輸入直接被拒絕，不要事後 `Substring` 截斷（可能切斷代理對）。
*   **給玩家或記錄的錯誤細節：** 先遮蔽金鑰、再截斷（Gemini 為 200 字）；截斷點落在高代理字元時往前退一格，不切斷代理對。錯誤 JSON 沒有 `message` 時改用原始回應內容。
*   **字典產生：** Python 工具鏈 `extract_vanilla_names.py` → `build_full_input.py` → `agy_translate.py` → `merge_dictionary.py`；人工修正寫在 `NameTable/translations/` 的 CSV，不直接改 TSV。

---

## 踩坑紀錄

### RimWorld
*   對 `Assembly-CSharp.dll` 做反射時，會碰到 Unity 的靜態建構子會丟例外；`ReflectionOnlyLoadFrom` 需要先載入相依組件。只看中繼資料即可。
*   `BuildName()` 每次都 `new NameTriple`，所以用 `ReferenceEquals(pawn.Name, 開窗時的 Name)` 偵測「確定」。不要 patch `Pawn.Name` setter（可能被 inline；開局畫面每次按鍵都會重設名字）。
*   開發模式**且**開啟上帝模式時（`DebugSettings.ShowDevGizmos` = `Prefs.DevMode && godMode`），任何小人的改名視窗都是新生兒版（三列可編輯、無頭銜、姓欄為「建議...」）；只開開發模式不會。
*   姓欄「建議」清單為空時，`MakeRow` 不畫任何按鈕；鎖頭必須 `NewCol(24f, HorizontalJustification.Right)`。
*   `CharacterCardUtility.ValidNameRegex` 擋 `·`、`・` 與 CJK 擴充 B 以後的字（代理對、長度算 2）。
*   Mod 設定檔名是 `Mod_<資料夾名>_<Mod 類別>.xml`：junction 開發版與 Workshop 版的設定不互通。
*   `Listing_Standard.TextEntry` 沒有長度上限參數，要用 `Widgets.TextField(rect, text, maxLength)`。
*   **`Verse.Listing` 會自動開新欄：** `GetRect` 在內容超過清單高度時把 `curX` 右移一欄（`NewColumnIfNeeded`），畫在可視範圍外，`CurHeight` 也變成新欄的高度。放在捲動區裡的 Listing 一定要設 `maxOneColumn = true`；否則以「上一幀 CurHeight」當捲動高度時，某一幀變高就會讓整頁永久塌陷（Step 1 實測：儲存金鑰後設定頁下半部消失）。
*   設定頁每個區塊各自 try/catch／finally：記錄一次例外，並還原 `GUI.color`、`Text.Font`、`Text.Anchor`；最外層 finally 一定要 `list.End()`、`Widgets.EndScrollView()`，否則 IMGUI 群組失衡會讓整個視窗異常。
*   Mono 的 `ProtectedData` 在 macOS／Linux 會靜默改用 ManagedProtection，不會丟例外。

### Gemini Interactions API
*   `response_format` 是 `{"type":"text","mime_type":"application/json","schema":{…}}`，schema 不能直接放。
*   回應在 `steps[]`，要找 `type == "model_output"`（前面可能有 `thought`），且 `status == "completed"`。
*   錯誤 `code` 是 snake_case **字串**；`models.get` 等舊 API 是數字 code ＋ `status`。429 分 `quota_exceeded`（每日，不分大小寫比對）與 `rate_limit_exceeded`（每分鐘）。舊 API 的 `RESOURCE_EXHAUSTED` 兩種限制都會用，**不能**當成每日額度；只有訊息含 `PerDay`／`per day` 時才算。
*   2026-10-16 即時測試確認：`AQ.` 金鑰可用 `x-goog-api-key` 呼叫 Interactions；`response_format` 的 schema 會被遵守；錯誤金鑰回 401、未知模型回 404。
*   `temperature`／`top_p`／`top_k` 已棄用，未來模型會回 400；flash-lite 預設 `thinking_level = minimal`。

### 測試與審查流程
*   .NET 10 SDK 的 `dotnet test` 不再支援 VSTest：xunit.v3 必須搭配根目錄 `global.json` 的 `"test": {"runner": "Microsoft.Testing.Platform"}`，指令改為 `dotnet test --project <dir>`；測試專案不需要 `Microsoft.NET.Test.Sdk`、`xunit.runner.visualstudio`。
*   xUnit 分析器 xUnit1051（測試專案設 `TreatWarningsAsErrors`）：呼叫接受 `CancellationToken` 的方法時要傳 `TestContext.Current.CancellationToken`。
*   即時測試用 `Assert.SkipWhen`／`Assert.SkipUnless`（xunit.v3）在沒有金鑰時略過，並以 `[Trait("Category", "Live")]` 標記。
*   本帳號 Copilot 審查額度已用完：`gh pr edit --add-reviewer "@copilot"` 會立刻留下「reached their quota limit」的 review，此時直接走 `Tools/agy_review.py`（約 10～15 分鐘，建議背景執行）。
*   PowerShell 中 `gh ... --jq '<含空白與引號的運算式>'` 會被拆成多個參數；複雜查詢改用 Python 呼叫 `gh api` 再解析 JSON。
*   推送修正後立刻 `gh pr merge` 可能回報「Base branch was modified」；這是 GitHub 還在重算合併狀態，稍候重試即可，合併前確認 squash 內容含最新 commit。
*   驗證某個分支能單獨建置時，用暫存 worktree（`git worktree add --detach <tmp> <branch>`），避免工作區中下一個單元的未追蹤檔案混入。
*   多個 PR 平行審查時，切換分支會讓「已提交在別的分支」的檔案從工作區消失；回頭修前一個 PR 前，先 `git stash push --include-untracked -- <後續單元的路徑>`，修完、合併後再 `git stash pop`。
*   本 session 的工具不允許 `Remove-Item Env:<名稱>`（會被誤判成刪除路徑）；清除環境變數用 `$env:<名稱> = $null`。

### PowerShell 5.1 腳本
*   路徑一律 `-LiteralPath`（或直接用 .NET API）：`-Path` 會把 `[`、`]` 當萬用字元，`[Modding]` 這類資料夾會被當成不存在。`New-Item -Path` 改用 `[System.IO.Directory]::CreateDirectory`。
*   `Compress-Archive` 遇方括號路徑會失敗；`ZipFile.CreateFromDirectory` 在 .NET Framework 會寫入 `\` 分隔符。改用 `ZipFile.Open(..., Create)` ＋ `ZipFileExtensions.CreateEntryFromFile`，項目名稱自行換成 `/`。
*   `Get-ChildItem` 預設略過隱藏檔；做安全掃描時一定要加 `-Force`。
*   `Join-Path` 不會處理已是絕對路徑的子路徑；把參數轉成絕對路徑要用 `[System.IO.Path]::GetFullPath([System.IO.Path]::Combine((Get-Location).ProviderPath, $p))`。
*   腳本檔維持 ASCII（註解用英文），避免 PowerShell 5.1 把無 BOM 的 UTF-8 當成 ANSI 讀取。
*   PowerShell 5.1 呼叫外部程式時會**丟掉空字串參數**（`-X ''` 變成缺少參數值），測試「空值」要用空白字串。
*   掃描可能很大的檔案（`Player.log` 可達數百 MB）要分塊串流讀取，區塊間保留重疊以免漏掉跨邊界的樣式，並只解碼實際讀到的位元組；開檔用 `FileShare.ReadWrite | Delete`，不妨礙遊戲寫入與輪替 log。
*   給使用者手動執行的單行指令容易在 cmd 執行或碰到資料夾而報錯；需要可靠結果時改成附自我測試的腳本。

### 名字規則（C# 與 Python 雙實作）
*   規則同時存在於 `Tools/name_rules.py`（字典產生）與 `Pipeline/`（遊戲內），任何一邊改了都要同步另一邊並重跑 fixture。中文字元範圍兩邊都是 U+3400–U+4DBF、U+4E00–U+9FFF（不含中間的易經卦符）。
*   `NameSanitizer.Check` 只對應 Python 的三種「不合格」；`Sanitize` 是寫入前的清理（去標點符號、檢查遊戲 regex），所有來源的譯名都要經過它。
*   .NET 正規表示式的 `$` 會匹配結尾換行之前的位置，要求整串時用 `\z`。
*   `string.Normalize` 遇到孤立代理字元會丟 `ArgumentException`；一律經 `TextNormalization.Normalize`。

### C#／Mono 相容
*   字串解析器在呼叫 `string.CompareOrdinal(a, i, b, j, len)` 前先檢查剩餘長度：.NET 10 會自動截短比較，Mono 可能直接丟 `ArgumentOutOfRangeException`，測試抓不到差異。

### 工具與環境
*   `agy` 逾時時仍回傳 SUCCESS，但回應是空的；要檢查 `structured_output` 是否存在。
*   `agy -p` 的提示放在命令列參數，Windows 上限約 32,767 字；大量內容改寫成檔案讓代理自己讀。
*   `python -I` 會忽略 `PYTHONIOENCODING`，而主控台是 cp950：腳本內要 `sys.stdout.reconfigure(encoding='utf-8')`。
*   PowerShell 的 here-string 無法以 `git commit -F -` 從 stdin 傳入；先寫成檔案。
*   PowerShell 5.1 對原生程式的 stderr 重新導向會產生 `NativeCommandError` 並讓 `$?` 變 false，即使程式回傳 0。
*   `*.tsv` 由 `.gitattributes` 固定為 LF（遊戲端解析器與 `merge_dictionary.py` 都用 LF）。
