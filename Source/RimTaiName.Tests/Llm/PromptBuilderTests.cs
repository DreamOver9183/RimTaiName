using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimTaiName.Llm;
using RimTaiName.Pipeline;
using Xunit;

namespace RimTaiName.Tests.Llm
{
    public class PromptBuilderTests
    {
        private static readonly string ShippedRulesPath = Path.Combine(AppContext.BaseDirectory, "Data", PromptBuilder.RulesFileName);

        private static string ShippedRules => PromptBuilder.LoadRules(ShippedRulesPath, out _);

        private static LlmRequest Request(string first, string nick, string last, params NameField[] wanted)
        {
            var request = new LlmRequest();
            request.Text[0] = first;
            request.Text[1] = nick;
            request.Text[2] = last;
            foreach (NameField field in wanted)
                request.Wanted[(int)field] = true;
            return request;
        }

        private static List<Dictionary<string, object>> Items(string input) =>
            MiniJson.AsArray(MiniJson.AsObject(MiniJson.Parse(input))["items"]).Select(MiniJson.AsObject).ToList();

        [Fact]
        public void ShippedRulesAreUsedVerbatim()
        {
            string rules = ShippedRules;
            Assert.NotNull(rules);
            var builder = new PromptBuilder(rules);
            Assert.True(builder.HasSharedRules);
            string instruction = builder.SystemInstruction;
            foreach (string line in rules.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0))
                Assert.Contains(line, instruction);
            Assert.DoesNotContain("\r", instruction);
        }

        [Fact]
        public void InstructionOrderIsIntroInputRulesOutputRuleExamples()
        {
            string instruction = new PromptBuilder(ShippedRules).SystemInstruction;
            int input = instruction.IndexOf("【輸入欄位】", StringComparison.Ordinal);
            int rules = instruction.IndexOf("【規則】", StringComparison.Ordinal);
            int rule5 = instruction.IndexOf("\n5. ", StringComparison.Ordinal);
            int rule6 = instruction.IndexOf("\n6. 只翻譯 items", StringComparison.Ordinal);
            int examples = instruction.IndexOf("【範例】", StringComparison.Ordinal);
            Assert.True(0 < input && input < rules && rules < rule5 && rule5 < rule6 && rule6 < examples,
                        $"positions {input} {rules} {rule5} {rule6} {examples}");
            Assert.EndsWith("\n", instruction);
            Assert.DoesNotContain("\n\n\n", instruction);
        }

        [Fact]
        public void PythonOnlyTextIsNotSentToGemini()
        {
            string instruction = new PromptBuilder(ShippedRules).SystemInstruction;
            Assert.DoesNotContain("不要使用任何工具", instruction);
            Assert.DoesNotContain("每個 id 都必須回傳", instruction);
        }

        [Fact]
        public void CrLfRulesGiveTheSameInstruction()
        {
            string lf = ShippedRules.Replace("\r\n", "\n");
            Assert.Equal(new PromptBuilder(lf).SystemInstruction, new PromptBuilder(lf.Replace("\n", "\r\n")).SystemInstruction);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   \n")]
        public void MissingRulesStillGiveAUsableInstruction(string rules)
        {
            var builder = new PromptBuilder(rules);
            Assert.False(builder.HasSharedRules);
            Assert.Contains("【規則】\n6. 只翻譯 items", builder.SystemInstruction);
            Assert.DoesNotContain("【範例】", builder.SystemInstruction);
        }

        [Fact]
        public void RulesWithoutExamplesSectionAreAccepted()
        {
            var builder = new PromptBuilder("【規則】\n1. 規則一。\n");
            Assert.True(builder.HasSharedRules);
            Assert.EndsWith("【規則】\n1. 規則一。\n6. 只翻譯 items 中列出的 id，以 JSON 物件回傳 first、nick、last 三個欄位，每個欄位只放一個譯名；沒有列出的 id 回傳空字串。\n",
                            builder.SystemInstruction);
        }

        [Fact]
        public void LoadRulesReportsMissingFile()
        {
            Assert.Null(PromptBuilder.LoadRules(Path.Combine(AppContext.BaseDirectory, "no-such-rules.txt"), out string error));
            Assert.StartsWith("FileNotFoundException", error);
        }

        [Fact]
        public void InputListsOnlyWantedFieldsWithContext()
        {
            var items = Items(PromptBuilder.BuildInput(Request(" Zzyzx ", "Mad Dog", "吉賽兒", NameField.First, NameField.Nick)));
            Assert.Equal(2, items.Count);
            Assert.Equal("first", items[0]["id"]);
            Assert.Equal("Zzyzx", items[0]["text"]);
            Assert.Equal("given_name", items[0]["type"]);
            Assert.Equal("nick", items[1]["id"]);
            Assert.Equal("nickname", items[1]["type"]);
            Assert.All(items, i => Assert.Equal("Zzyzx 'Mad Dog' 吉賽兒", i["example"]));
        }

        [Fact]
        public void SurnameItemAndExampleWithoutNickname()
        {
            var item = Assert.Single(Items(PromptBuilder.BuildInput(Request("Gisele", "  ", "O'Brien", NameField.Last))));
            Assert.Equal("last", item["id"]);
            Assert.Equal("surname", item["type"]);
            Assert.Equal("Gisele O'Brien", item["example"]);
        }

        [Fact]
        public void InputEscapesQuotesAndOmitsEmptyExample()
        {
            string input = PromptBuilder.BuildInput(Request("\"Q\"", null, null, NameField.First));
            var item = Assert.Single(Items(input));
            Assert.Equal("\"Q\"", item["text"]);
            Assert.Equal("\"Q\"", item["example"]);
            Assert.Empty(Items(PromptBuilder.BuildInput(Request(null, null, null))));
        }

        [Fact]
        public void BodyCarriesInstructionInputAndStructuredOutput()
        {
            var builder = new PromptBuilder(ShippedRules);
            Dictionary<string, object> body = builder.BuildTranslateBody(Request("Zzyzx", "", "Quill", NameField.First));
            Assert.Equal(new[] { "input", "response_format", "system_instruction" }, body.Keys.OrderBy(k => k, StringComparer.Ordinal));
            Assert.Equal(builder.SystemInstruction, body["system_instruction"]);

            var format = MiniJson.AsObject(MiniJson.Parse(MiniJson.Serialize(body["response_format"])));
            Assert.Equal("text", format["type"]);
            Assert.Equal("application/json", format["mime_type"]);
            var schema = MiniJson.AsObject(format["schema"]);
            Assert.Equal("object", schema["type"]);
            Assert.Equal(new object[] { "first", "nick", "last" }, MiniJson.AsArray(schema["required"]));
            var properties = MiniJson.AsObject(schema["properties"]);
            Assert.All(new[] { "first", "nick", "last" }, p => Assert.Equal("string", MiniJson.AsObject(properties[p])["type"]));
        }
    }
}
