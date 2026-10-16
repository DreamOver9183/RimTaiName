using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RimTaiName.Pipeline;

namespace RimTaiName.Llm
{
    /// <summary>
    /// Builds the Gemini request body for translating name fields. Rules 1–5 and the examples come from
    /// Prompts/NameRules_zhTW.txt, the same file Tools/name_rules.py uses for the offline dictionary; this class
    /// adds its own introduction, input description and output rule 6 around them.
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

        public PromptBuilder(string sharedRules)
        {
            string text = (sharedRules ?? string.Empty).TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n');
            int split = text.IndexOf("\n" + ExamplesHeading, StringComparison.Ordinal);
            string rules = (split >= 0 ? text.Substring(0, split) : text).Trim('\n');
            string examples = split >= 0 ? text.Substring(split + 1).TrimEnd('\n') : string.Empty;
            HasSharedRules = rules.Trim().Length > 0;

            var sb = new StringBuilder();
            sb.Append(Intro).Append("\n\n").Append(InputFields).Append("\n\n");
            sb.Append(HasSharedRules ? rules : "【規則】").Append('\n');
            sb.Append(OutputRule).Append('\n');
            if (examples.Length > 0)
                sb.Append('\n').Append(examples).Append('\n');
            SystemInstruction = sb.ToString();
        }

        /// <summary>False when the rules file was missing or empty: translation still works, with weaker guidance.</summary>
        public bool HasSharedRules { get; }

        public string SystemInstruction { get; }

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
