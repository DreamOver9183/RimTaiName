using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RimTaiName.Pipeline;

namespace RimTaiName.Llm
{
    /// <summary>
    /// Builds the Gemini request body for translating name fields, or for generating new ones (phase 2). Rules 1–5 and the examples come from
    /// Prompts/NameRules_zhTW.txt, the same file Tools/name_rules.py uses for the offline dictionary; this class
    /// adds its own introduction, input description and output rule 6 around them (one set per mode).
    /// </summary>
    public sealed class PromptBuilder
    {
        public const string RulesFileName = "NameRules_zhTW.txt";

        private const string ExamplesHeading = "【範例】\n";

        private const string Intro =
            "你是《RimWorld》（邊緣世界）的臺灣繁體中文在地化譯者。請把輸入 items 中列出的角色姓名欄位翻成中文。";

        private const string InputFields =
            "【輸入欄位】\n" +
            "items 的每一項：id（first 名／nick 暱稱／last 姓）、text（原文）、type（given_name 名／surname 姓／nickname 暱稱）、" +
            "example（含此詞的完整姓名，格式為「名 '暱稱' 姓」，可能為空）。不提供 gender、origin、used_by。";

        private const string OutputRule =
            "6. 只翻譯 items 中列出的 id，以 JSON 物件回傳 first、nick、last 三個欄位，每個欄位只放一個譯名；沒有列出的 id 回傳空字串。";

        private const string GenerateIntro =
            "你是《RimWorld》（邊緣世界）的臺灣繁體中文在地化譯者，也負責替角色取名。請依 input 的角色資料，為 generate 列出的欄位取新的中文名字。";

        private const string GenerateInputFields =
            "【輸入欄位】\n" +
            "pawn：gender（male 男／female 女／none）、childhood（童年背景）、adulthood（成年背景，兒童沒有）、culture（文化）、" +
            "xenotype（異種族；Baseliner 是一般人類）、life_stage（newborn／baby／child／adult），都可能為空。\n" +
            "locked：玩家鎖定的欄位與內容，不會被更改；新名字要與它們搭配（同一種文化風格、名與姓相配）。\n" +
            "generate：要取名的欄位（first 名／nick 暱稱／last 姓）。\n" +
            "hints：外文名開頭字母的建議，用來讓每次的結果不同；不適合這個角色時可以不採用。";

        private const string GenerateSteps =
            "【取名方式】\n" +
            "A. 先依性別、文化、異種族與背景故事，構想一個符合 RimWorld 世界觀的外文姓名（名在前、姓在後），再依下方【規則】把它轉成中文。不要取姓在前的華人姓名。\n" +
            "B. 暱稱：依背景故事或特質取一個意譯外號；也可以直接用名或姓的中文，表示別人以名或姓稱呼他。\n" +
            "C. 文化或異種族有明顯特色時（例如帝國貴族、部落、豬人、廢土人），名字要符合該族群的氣質。";

        private const string GenerateOutputRule =
            "6. 只填 generate 中列出的欄位，以 JSON 物件回傳 first、nick、last；沒有要求的欄位回傳空字串。不要回傳外文原名，也不要用任何拉丁字母。";

        /// <summary>Initial letters for the hints: common name initials, no Q, X, Y, Z.</summary>
        private const string HintLetters = "ABCDEFGHIJKLMNOPRSTVW";

        /// <summary>Upper bound for each context value; a custom xenotype name is free text typed by the player.</summary>
        private const int MaxContextLength = 64;

        public PromptBuilder(string sharedRules)
        {
            string text = (sharedRules ?? string.Empty).TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n');
            int split = text.IndexOf("\n" + ExamplesHeading, StringComparison.Ordinal);
            string rules = (split >= 0 ? text.Substring(0, split) : text).Trim('\n');
            string examples = split >= 0 ? text.Substring(split + 1).TrimEnd('\n') : string.Empty;
            HasSharedRules = rules.Trim().Length > 0;
            string sharedPart = HasSharedRules ? rules : "【規則】";

            SystemInstruction = Compose(Intro + "\n\n" + InputFields, sharedPart, OutputRule, examples);
            GenerateInstruction = Compose(GenerateIntro + "\n\n" + GenerateInputFields + "\n\n" + GenerateSteps, sharedPart,
                                          GenerateOutputRule, examples);
        }

        /// <summary>False when the rules file was missing or empty: translation still works, with weaker guidance.</summary>
        public bool HasSharedRules { get; }

        /// <summary>The system instruction for translating names.</summary>
        public string SystemInstruction { get; }

        /// <summary>The system instruction for generating names (phase 2).</summary>
        public string GenerateInstruction { get; }

        /// <summary>Reads the shared rules file; returns null and an error message when it cannot be read.</summary>
        public static string LoadRules(string path, out string error)
        {
            error = null;
            try
            {
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException ||
                                      e is NotSupportedException || e is System.Security.SecurityException)
            {
                error = e.GetType().Name + ": " + e.Message;
                return null;
            }
        }

        /// <summary>The request body for <paramref name="request"/>'s mode.</summary>
        public Dictionary<string, object> BuildBody(LlmRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            return request.Mode == ProcessMode.Generate ? BuildGenerateBody(request) : BuildTranslateBody(request);
        }

        public Dictionary<string, object> BuildGenerateBody(LlmRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            return new Dictionary<string, object>
            {
                ["system_instruction"] = GenerateInstruction,
                ["input"] = BuildGenerateInput(request),
                ["response_format"] = ResponseFormat(),
            };
        }

        /// <summary>
        /// JSON text with the pawn context, the locked fields (style reference), the fields to fill and the
        /// initial-letter hints. Unlocked current values are not sent: they are about to be replaced.
        /// </summary>
        public static string BuildGenerateInput(LlmRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            PawnContext context = request.Context ?? new PawnContext();
            var pawn = new Dictionary<string, object>();
            AddContext(pawn, "gender", context.Gender);
            AddContext(pawn, "childhood", context.Childhood);
            AddContext(pawn, "adulthood", context.Adulthood);
            AddContext(pawn, "culture", context.Culture);
            AddContext(pawn, "xenotype", context.Xenotype);
            AddContext(pawn, "life_stage", context.LifeStage);

            var locked = new Dictionary<string, object>();
            var generate = new List<object>();
            foreach (NameField field in NameFields.All)
            {
                int i = (int)field;
                if (request.Wanted[i])
                    generate.Add(Id(field));
                else if (request.Locked[i] && !string.IsNullOrWhiteSpace(request.Text[i]))
                    locked[Id(field)] = request.Text[i].Trim();
            }

            var hints = new Dictionary<string, object>();
            uint seed = unchecked((uint)request.Seed);
            if (request.Wanted[(int)NameField.First])
                hints["first_initial"] = HintLetters[(int)(seed % (uint)HintLetters.Length)].ToString();
            if (request.Wanted[(int)NameField.Last])
                hints["last_initial"] = HintLetters[(int)(seed / (uint)HintLetters.Length % (uint)HintLetters.Length)].ToString();

            var input = new Dictionary<string, object>
            {
                ["pawn"] = pawn,
                ["locked"] = locked,
                ["generate"] = generate,
            };
            if (hints.Count > 0)
                input["hints"] = hints;
            return MiniJson.Serialize(input);
        }

        /// <summary>The request fields besides <c>model</c> and <c>store</c>, which the transport adds.</summary>
        public Dictionary<string, object> BuildTranslateBody(LlmRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            return new Dictionary<string, object>
            {
                ["system_instruction"] = SystemInstruction,
                ["input"] = BuildInput(request),
                ["response_format"] = ResponseFormat(),
            };
        }

        /// <summary>JSON text listing only the wanted fields, each with the full name as context.</summary>
        public static string BuildInput(LlmRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            string example = FullName(request.Text);
            var items = new List<object>();
            foreach (NameField field in NameFields.All)
            {
                int i = (int)field;
                if (!request.Wanted[i])
                    continue;
                var item = new Dictionary<string, object>
                {
                    ["id"] = Id(field),
                    ["text"] = request.Text[i]?.Trim() ?? string.Empty,
                    ["type"] = field == NameField.Nick ? "nickname" : field == NameField.First ? "given_name" : "surname",
                };
                if (example.Length > 0)
                    item["example"] = example;
                items.Add(item);
            }
            return MiniJson.Serialize(new Dictionary<string, object> { ["items"] = items });
        }

        /// <summary>Structured output: an object with the three fields as required strings.</summary>
        public static Dictionary<string, object> ResponseFormat()
        {
            var properties = new Dictionary<string, object>();
            var required = new List<object>();
            foreach (NameField field in NameFields.All)
            {
                properties[Id(field)] = new Dictionary<string, object> { ["type"] = "string" };
                required.Add(Id(field));
            }
            return new Dictionary<string, object>
            {
                ["type"] = "text",
                ["mime_type"] = "application/json",
                ["schema"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["required"] = required,
                },
            };
        }

        /// <summary>The JSON property name used for a field in the input items and in the response.</summary>
        public static string Id(NameField field) =>
            field == NameField.First ? "first" : field == NameField.Nick ? "nick" : "last";

        private static string Compose(string head, string rules, string outputRule, string examples)
        {
            var sb = new StringBuilder();
            sb.Append(head).Append("\n\n").Append(rules).Append('\n').Append(outputRule).Append('\n');
            if (examples.Length > 0)
                sb.Append('\n').Append(examples).Append('\n');
            return sb.ToString();
        }

        private static void AddContext(Dictionary<string, object> target, string name, string value)
        {
            string text = value?.Trim();
            if (string.IsNullOrEmpty(text))
                return;
            if (text.Length > MaxContextLength)
            {
                int cut = MaxContextLength;
                if (char.IsHighSurrogate(text[cut - 1]))
                    cut--; // never split a surrogate pair
                text = text.Substring(0, cut);
            }
            target[name] = text;
        }

        private static string FullName(string[] text)
        {
            string first = text[(int)NameField.First]?.Trim() ?? string.Empty;
            string nick = text[(int)NameField.Nick]?.Trim() ?? string.Empty;
            string last = text[(int)NameField.Last]?.Trim() ?? string.Empty;
            string full = nick.Length > 0 ? first + " '" + nick + "' " + last : first + " " + last;
            return full.Trim();
        }
    }
}
