# 內建字典翻譯紀錄（v1.0，2026-10-16；v0.1 為 2026-10-15）

*   **輸出**：`Dictionary/BaseNames_zhTW.tsv`（24,596 筆，欄位為 `namespace`、`key`、`zh_tw`；`key` 已做 Trim、NFC 正規化並轉成小寫）。
*   **翻譯**：Antigravity CLI，模型 `gemini-3.8-flash-low`，8 個行程平行執行，每批 40 筆。共 615 批，耗時 15.7 分鐘，0 失敗。

## 流程與重跑方式

```
python -I Tools/build_full_input.py      # tokens.csv → full_input.csv（附類型、性別、來源、範例全名）
python -I Tools/agy_translate.py NameTable/translations/full_input.csv NameTable/translations/full_results.csv
python -I Tools/merge_dictionary.py      # 合併、標記、輸出字典
```

*   `agy_translate.py` 會把每次回應存進 `raw/`。中斷後重跑同一個指令，只會補翻還沒完成的 id。偵測到額度用完會自動停止。
*   只改了修正清單時，重跑 `merge_dictionary.py` 即可，不必重新翻譯。

**校對流程（`Tools/review_sheet.py`）：**

```
python -I Tools/review_sheet.py export   # 匯出尚未定案的第 1 層（有標記）、第 2、4 層 → review_sheet.csv
                                         # 填 suggestion／reason／decision（fix＝採用建議、keep＝維持原譯）
python -I Tools/review_sheet.py apply    # fix → review_corrections.csv；keep → review_accepted.csv
python -I Tools/merge_dictionary.py      # 重新產生字典；review_queue.csv 不再列出已定案的條目
python -I Tools/export_rule_fixtures.py  # 再跑 dotnet test
```

*   `review_accepted.csv` 記錄「已檢查、保留原譯」的條目與當時的譯名。之後譯名若有變動，該條會重新出現在 `review_queue.csv`。
*   人工修正（`review_corrections.csv`）若只帶有提示性標記（單字名、名姓偏長、「者」字尾），也視為已定案。
*   `export` 不會覆寫還沒 `apply` 的校對表，要重來時加 `--force`。

## 譯名採用順序

1.  **`review_corrections.csv`**：人工修正，170 筆（v0.1 的 70 筆，加上 Step 5 校對的 100 筆）。
2.  **特殊字形規則**：縮寫與單一字母，112 筆（見下方「特殊字形」）。
3.  **`official_overrides.csv`**：官方誤譯的覆寫，45 筆。值留空表示改用 Gemini 的譯名。
4.  **官方繁中譯名**：309 筆，包括帝國名與怪人暱稱。繁中玩家在遊戲內已經看到這些譯名，所以優先採用。
5.  **Gemini 譯名**：其餘 23,895 筆；另有 30 筆是官方譯名被覆寫、改用 Gemini 的譯名。
6.  **自動統一**：暱稱若只是第 1～2 層人名的拼法變體，改用人名的譯法（66 筆，例如 James 的「詹姆士」改為「詹姆斯」）。有意義的外號保留意譯（例如 Fox →「狐狸」、Riddle →「謎語人」）。

## 檢查範圍與品質估計

| 範圍 | 方式 | 結果 |
| :--- | :--- | :--- |
| Tier 1（1,654 詞） | 逐筆人工檢查 | 約 30 筆有問題，已全數修正（約 2%） |
| 官方譯名（355 詞） | 逐筆與 Gemini 對照 | 覆寫 45 筆：43 筆官方誤譯或不適合當暱稱，2 筆是官方一詞多譯擇一 |
| 系統性問題 | 程式找出後逐筆處理 | 俄文名的「季」（15 筆）、「XX者」直譯（9 筆）、暱稱與人名不一致（66 筆） |
| Tier 3（21,769 詞） | 隨機抽查 150 筆 | 沒有明顯錯誤，2～3 筆見仁見智（估計 1～2%） |
| Tier 2（624 詞）、Tier 4（549 詞） | 2026-10-16 逐筆校對：Claude 全數檢查，Gemini（Antigravity 子代理）另外獨立審一次，再由使用者確認 | 修正 100 筆（約 8.5%），其餘列入 `review_accepted.csv`。清單與理由見 `review_sheet_fixes.md` |

## 檔案

| 檔案 | 內容 |
| :--- | :--- |
| `full_input.csv` / `full_results.csv` | 送出的內容與 Gemini 的原始譯名 |
| `dictionary_draft.csv` | 每個詞的官方譯名、Gemini 譯名、最終譯名、採用來源與標記 |
| `review_queue.csv` | 仍待檢查的標記條目：只剩第 3 層的 430 筆（第 1、2、4 層都已定案） |
| `review_accepted.csv` | 已檢查、保留原譯的 1,135 筆（含第 1 層單字名 65 筆） |
| `review_sheet_fixes.md` | Step 5 校對的修改清單（原譯、新譯、理由） |
| `nick_collisions.csv` | 不同原文譯成同一個暱稱的 634 組，多為拼法變體或同義詞，僅供參考 |
| `official_overrides.csv` / `review_corrections.csv` | 覆寫與修正清單，各列附理由。修正官方譯名（例如 Grit 的「毅力」改「砂礫」）也寫在 `review_corrections.csv`，它的優先順序最高 |

## 特殊字形（2026-10-15 決定：保留英文字母）

| 類型 | 數量 | 處理 |
| :--- | --: | :--- |
| 全大寫縮寫（MJ、DJ、ET） | 89 | 規則：保留原樣 |
| 單一字母 | 23 | 規則：當名或姓時保留字母（B）；當暱稱時寫成「阿」＋字母（阿B） |
| 字詞＋字母（Big D、Justin J） | 19 | 人工決定，列在 `review_corrections.csv`，原則是字詞照翻、字母保留 |
| 大小寫混排（GuanYu、MrD） | 57 | Gemini 多數正確（關羽、D先生），含字母的少數幾筆已人工處理 |

*   規則寫在 `Tools/name_rules.py` 的 `special_form()`。模組內的 `SpecialFormRule` 要用同一套規則，讓其他 Mod 的名字和玩家手打的名字結果一致。
*   檢查程式只允許「原文中本來就是大寫的字母」出現在譯名裡，其他拉丁字母一律標記為錯誤。
