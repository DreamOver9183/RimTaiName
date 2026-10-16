using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimTaiName.Llm;
using RimTaiName.Pipeline;
using Xunit;

namespace RimTaiName.Tests.Llm
{
    public sealed class GeminiNameTranslatorTests : IDisposable
    {
        private static readonly string Key = "AQ" + "." + new string('t', 46) + "Wxyz";

        private const string Model = "gemini-3.5-flash-lite";

        private readonly string dir = Path.Combine(Path.GetTempPath(), "rimtainame_translator_" + Guid.NewGuid().ToString("N"));

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        public void Dispose()
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }

        private static string Completed(string text) =>
            "{\"status\":\"completed\",\"steps\":[{\"type\":\"model_output\",\"content\":[{\"type\":\"text\",\"text\":" +
            MiniJson.Serialize(text) + "}]}]}";

        private static Func<HttpRequestMessage, HttpResponseMessage> Respond(int status, string body) =>
            _ => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private static (GeminiNameTranslator Translator, GeminiInteractionsClientTests.FakeHandler Handler) Create(
            Func<string> model = null, params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            var handler = new GeminiInteractionsClientTests.FakeHandler(responses);
            var transport = new GeminiInteractionsClient(() => Key, handler, TimeSpan.FromSeconds(5), (s, t) => Task.CompletedTask);
            var translator = new GeminiNameTranslator(transport, new PromptBuilder("【規則】\n1. 規則。\n"), model ?? (() => Model));
            return (translator, handler);
        }

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

        [Fact]
        public async Task SendsPromptAndReadsStructuredOutput()
        {
            var (translator, handler) = Create(responses: Respond(200, Completed("{\"first\":\"佐吉克斯\",\"nick\":\"瘋狗\",\"last\":\"\"}")));
            LlmResponse response = await translator.TranslateAsync(Request("Zzyzx", "Mad Dog", "Miller", NameField.First, NameField.Nick), Ct);

            Assert.True(response.Success);
            Assert.Equal("佐吉克斯", response[NameField.First]);
            Assert.Equal("瘋狗", response[NameField.Nick]);
            Assert.Equal("", response[NameField.Last]);

            var body = MiniJson.AsObject(MiniJson.Parse(Assert.Single(handler.Requests).Body));
            Assert.Equal(Model, body["model"]);
            Assert.Equal(false, body["store"]);
            Assert.StartsWith("你是《RimWorld》", (string)body["system_instruction"]);
            Assert.Contains("\"id\":\"first\"", (string)body["input"]);
            Assert.DoesNotContain("\"id\":\"last\"", (string)body["input"]);
            Assert.Equal("application/json", MiniJson.AsObject(body["response_format"])["mime_type"]);
        }

        [Fact]
        public async Task ModelIsReadForEveryRequest()
        {
            string model = "gemini-3.5-flash-lite";
            var (translator, handler) = Create(() => model,
                Respond(200, Completed("{\"first\":\"甲\",\"nick\":\"\",\"last\":\"\"}")),
                Respond(200, Completed("{\"first\":\"乙\",\"nick\":\"\",\"last\":\"\"}")));
            await translator.TranslateAsync(Request("A", null, null, NameField.First), Ct);
            model = "gemini-3.1-flash-lite";
            await translator.TranslateAsync(Request("A", null, null, NameField.First), Ct);
            Assert.Equal("gemini-3.1-flash-lite", MiniJson.AsObject(MiniJson.Parse(handler.Requests[1].Body))["model"]);
        }

        [Fact]
        public async Task TransportErrorsPassThrough()
        {
            var (translator, _) = Create(responses: Respond(429, "{\"error\":{\"code\":\"quota_exceeded\",\"message\":\"daily\"}}"));
            LlmResponse response = await translator.TranslateAsync(Request("A", null, null, NameField.First), Ct);
            Assert.Equal(GeminiErrorKind.QuotaExceeded, response.Error);
            Assert.Equal(429, response.StatusCode);
            Assert.Equal("daily", response.Detail);
        }

        [Fact]
        public async Task InvalidModelIsRejectedWithoutSending()
        {
            var (translator, handler) = Create(() => "../files");
            LlmResponse response = await translator.TranslateAsync(Request("A", null, null, NameField.First), Ct);
            Assert.Equal(GeminiErrorKind.InvalidModelId, response.Error);
            Assert.Empty(handler.Requests);
        }

        [Theory]
        [InlineData("{\"first\":\"甲\",\"nick\":\"乙\",\"last\":\"丙\"}", "甲", "乙", "丙")]
        [InlineData("```json\n{\"first\":\"甲\",\"nick\":\"乙\",\"last\":\"丙\"}\n```", "甲", "乙", "丙")]
        [InlineData("  {\"first\":\"甲\"}  ", "甲", null, null)]
        [InlineData("{\"first\":1,\"nick\":null,\"last\":[\"丙\"]}", null, null, null)]
        public void ParsesOutputLeniently(string text, string first, string nick, string last)
        {
            LlmResponse response = GeminiNameTranslator.ParseOutput(text);
            Assert.True(response.Success);
            Assert.Equal(first, response[NameField.First]);
            Assert.Equal(nick, response[NameField.Nick]);
            Assert.Equal(last, response[NameField.Last]);
        }

        [Theory]
        [InlineData("吉賽兒")]
        [InlineData("[\"甲\"]")]
        [InlineData("")]
        [InlineData(null)]
        public void RejectsOutputThatIsNotAJsonObject(string text)
        {
            Assert.Equal(GeminiErrorKind.InvalidResponse, GeminiNameTranslator.ParseOutput(text).Error);
        }

        [Fact]
        public async Task PipelineFillsPendingFieldsThroughTheTranslator()
        {
            Directory.CreateDirectory(dir);
            string dictionaryPath = Path.Combine(dir, "dict.tsv");
            File.WriteAllText(dictionaryPath, BuiltInDictionary.Header + "\nname\tmiller\t米勒\n");
            var cache = new PersonalNameCache(Path.Combine(dir, "cache.xml"));
            cache.Load();
            var service = new NameProcessorService(new BuiltInDictionary(dictionaryPath), cache);
            var (translator, handler) = Create(responses: Respond(200, Completed("{\"first\":\"佐吉克斯\",\"nick\":\"瘋·狗\",\"last\":\"米樂\"}")));

            var payload = new NameRequestPayload("Zzyzx", "Mad Dog", "Miller");
            NameResult result = await service.ResolveRemoteAsync(payload, service.ResolveLocal(payload), translator, Ct);

            Assert.Equal("佐吉克斯", result[NameField.First].Value);
            Assert.Equal("瘋狗", result[NameField.Nick].Value);      // sanitized
            Assert.Equal("米勒", result[NameField.Last].Value);      // dictionary result kept
            Assert.Equal(FieldSource.Dictionary, result[NameField.Last].Source);
            Assert.Single(handler.Requests);
        }
    }
}
