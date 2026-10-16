using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RimTaiName.Llm;
using Xunit;

namespace RimTaiName.Tests.Llm
{
    /// <summary>
    /// Real requests to Gemini. Skipped unless RIMTAINAME_KEY_FILE points at a file holding a key, e.g.
    /// <c>$env:RIMTAINAME_KEY_FILE = (Resolve-Path RimTaiName_KEY_01.txt)</c>. Each run uses four requests of the
    /// daily quota. The key is never written to test output.
    /// </summary>
    [Trait("Category", "Live")]
    public class GeminiLiveTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static readonly string KeyFile = Environment.GetEnvironmentVariable("RIMTAINAME_KEY_FILE");

        private static string Model => Environment.GetEnvironmentVariable("RIMTAINAME_MODEL") ?? ModelId.Default;

        private static string ReadKey()
        {
            Assert.SkipWhen(string.IsNullOrEmpty(KeyFile), "RIMTAINAME_KEY_FILE is not set");
            Assert.SkipUnless(File.Exists(KeyFile), "RIMTAINAME_KEY_FILE does not exist");
            return File.ReadAllText(KeyFile).Trim();
        }

        private static void AssertNoKey(GeminiResult result, string key)
        {
            Assert.True(result.Detail == null || !result.Detail.Contains(key), "key leaked into Detail");
            Assert.True(result.Text == null || !result.Text.Contains(key), "key leaked into Text");
        }

        [Fact]
        public async Task CheckSucceedsWithRealKey()
        {
            string key = ReadKey();
            var client = new GeminiInteractionsClient(() => key);
            GeminiResult result = await client.CheckAsync(Model, Ct);
            AssertNoKey(result, key);
            Assert.True(result.Success, $"{result.Error} HTTP {result.StatusCode}: {result.Detail}");
            Assert.False(string.IsNullOrWhiteSpace(result.Text));
        }

        [Fact]
        public async Task StructuredOutputUsesResponseFormatSchema()
        {
            string key = ReadKey();
            var client = new GeminiInteractionsClient(() => key);
            var schema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["first"] = new Dictionary<string, object> { ["type"] = "string" },
                    ["nick"] = new Dictionary<string, object> { ["type"] = "string" },
                    ["last"] = new Dictionary<string, object> { ["type"] = "string" },
                },
                ["required"] = new List<object> { "first", "nick", "last" },
            };
            var body = new Dictionary<string, object>
            {
                ["system_instruction"] = "把輸入的英文姓名三欄翻成臺灣繁體中文音譯，暱稱意譯。只輸出 JSON。",
                ["input"] = "{\"first\":\"Gisele\",\"nick\":\"Fox\",\"last\":\"Miller\"}",
                ["response_format"] = new Dictionary<string, object>
                {
                    ["type"] = "text",
                    ["mime_type"] = "application/json",
                    ["schema"] = schema,
                },
            };
            GeminiResult result = await client.SendAsync(Model, body, Ct);
            AssertNoKey(result, key);
            Assert.True(result.Success, $"{result.Error} HTTP {result.StatusCode}: {result.Detail}");
            var names = MiniJson.AsObject(MiniJson.Parse(result.Text));
            Assert.NotNull(names);
            foreach (string field in new[] { "first", "nick", "last" })
                Assert.False(string.IsNullOrWhiteSpace(MiniJson.GetString(names, field)), field);
        }

        [Fact]
        public async Task WrongKeyIsReportedAsInvalidKey()
        {
            ReadKey();
            string fake = "AQ" + "." + "Ab" + new string('Z', 48);
            var client = new GeminiInteractionsClient(() => fake);
            GeminiResult result = await client.CheckAsync(Model, Ct);
            AssertNoKey(result, fake);
            Assert.Equal(GeminiErrorKind.InvalidKey, result.Error);
        }

        [Fact]
        public async Task UnknownModelIsReportedAsModelNotFound()
        {
            string key = ReadKey();
            var client = new GeminiInteractionsClient(() => key);
            GeminiResult result = await client.CheckAsync("gemini-0-nonexistent-lite", Ct);
            AssertNoKey(result, key);
            Assert.Equal(GeminiErrorKind.ModelNotFound, result.Error);
        }
    }
}
