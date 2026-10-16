# 邊緣臺名 RimTaiName

RimWorld 1.6 Mod：把小人的英文姓名翻成臺灣繁體中文。原版名字由內建字典離線翻譯，不需要金鑰；其他 Mod 的名字與「生成中文名」使用你自己的免費 Google Gemini API Key。

A RimWorld 1.6 mod that translates pawn names into Traditional Chinese (Taiwan). Vanilla names are translated offline from a built-in dictionary; names from other mods and brand-new Chinese names use your own free Google Gemini API key.

*   **玩家說明**：[`Docs/Workshop_zhTW.txt`](Docs/Workshop_zhTW.txt)（功能、API Key 申請、金鑰安全、隱私與額度）。
*   **Player guide (English)**：[`Docs/Workshop_en.txt`](Docs/Workshop_en.txt)
*   **需要**：[Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)
*   **問題回報**：[Issues](https://github.com/DreamOver9183/RimTaiName/issues)，請附上 `Player.log`（不含金鑰）。

## 開發

*   設計與決策：`RimTaiName 開發計畫書 v3.markdown`；進度：`RimTaiName 實作計畫.markdown`；開發規範、指令與踩坑紀錄：`CLAUDE.md`。
*   建置：`dotnet build Source/RimTaiName/RimTaiName.csproj -c Release`（直接引用本機遊戲的 `Managed` DLL）。
*   測試：`dotnet test --project Source/RimTaiName.Tests`。
*   打包：`powershell -NoProfile -ExecutionPolicy Bypass -File Tools/package_mod.ps1` → `dist/RimTaiName/`（含金鑰掃描）。上傳創意工坊時一律從這個乾淨的資料夾上傳。
*   字典：`Dictionary/BaseNames_zhTW.tsv`，產生與校對流程見 `NameTable/translations/README.md`。
