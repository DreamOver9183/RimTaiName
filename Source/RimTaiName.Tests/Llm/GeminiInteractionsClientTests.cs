using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimTaiName.Llm;
using Xunit;

namespace RimTaiName.Tests.Llm
{
    public class GeminiInteractionsClientTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static readonly string Key = "AQ" + "." + new string('k', 46) + "Wxyz";

        private const string Model = "gemini-3.5-flash-lite";

        private static string Completed(string text) =>
            "{\"id\":\"i1\",\"status\":\"completed\",\"steps\":[" +
            "{\"type\":\"thought\",\"content\":[{\"type\":\"text\",\"text\":\"thinking\"}]}," +
            "{\"type\":\"model_output\",\"content\":[{\"type\":\"text\",\"text\":" + MiniJson.Serialize(text) + "}]}]," +
            "\"usage\":{\"total_tokens\":12}}";

        private static string Error(string code, string message) =>
            "{\"error\":{\"code\":" + MiniJson.Serialize(code) + ",\"message\":" + MiniJson.Serialize(message) + "}}";

        private static (GeminiInteractionsClient Client, FakeHandler Handler) Create(
            Func<string> key = null, TimeSpan? timeout = null, params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            var handler = new FakeHandler(responses);
            var client = new GeminiInteractionsClient(key ?? (() => Key), handler, timeout ?? TimeSpan.FromSeconds(5),
                                                      (span, token) => Task.CompletedTask);
            return (client, handler);
        }

        private static Func<HttpRequestMessage, HttpResponseMessage> Respond(int status, string body) =>
            _ => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        [Fact]
        public async Task SendsKeyOnlyInHeaderAndFixedFields()
        {
            var (client, handler) = Create(responses: Respond(200, Completed("OK")));
            GeminiResult result = await client.CheckAsync(Model, Ct);

            Assert.True(result.Success);
            Assert.Equal("OK", result.Text);
            FakeHandler.Recorded request = Assert.Single(handler.Requests);
            Assert.Equal("POST", request.Method);
            Assert.Equal(GeminiInteractionsClient.Endpoint, request.Url);
            Assert.Equal(Key, request.KeyHeader);
            Assert.DoesNotContain(Key, request.Url);
            Assert.DoesNotContain(Key, request.Body);

            var body = MiniJson.AsObject(MiniJson.Parse(request.Body));
            Assert.Equal(Model, body["model"]);
            Assert.Equal(false, body["store"]);
            Assert.Equal("ping", body["input"]);
            foreach (string banned in new[] { "temperature", "top_p", "top_k", "generation_config", "thinking_budget" })
                Assert.False(body.ContainsKey(banned), banned);
        }

        [Fact]
        public async Task CallerCannotOverrideModelOrStore()
        {
            var (client, handler) = Create(responses: Respond(200, Completed("x")));
            var custom = new Dictionary<string, object> { ["model"] = "other", ["store"] = true, ["input"] = "hi" };
            await client.SendAsync(Model, custom, Ct);
            var body = MiniJson.AsObject(MiniJson.Parse(handler.Requests[0].Body));
            Assert.Equal(Model, body["model"]);
            Assert.Equal(false, body["store"]);
        }

        [Fact]
        public async Task NormalizesPastedModelResourceName()
        {
            var (client, handler) = Create(responses: Respond(200, Completed("x")));
            await client.CheckAsync("models/" + Model, Ct);
            Assert.Equal(Model, MiniJson.AsObject(MiniJson.Parse(handler.Requests[0].Body))["model"]);
        }

        [Theory]
        [InlineData("gemini/../files")]
        [InlineData("gemini?key=1")]
        [InlineData("gemini..x")]
        [InlineData("Gemini")]
        [InlineData("")]
        public async Task RejectsInvalidModelWithoutSending(string model)
        {
            var (client, handler) = Create();
            GeminiResult result = await client.CheckAsync(model, Ct);
            Assert.Equal(GeminiErrorKind.InvalidModelId, result.Error);
            Assert.Empty(handler.Requests);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task MissingKeyFailsWithoutSending(string key)
        {
            var (client, handler) = Create(key: () => key);
            GeminiResult result = await client.CheckAsync(Model, Ct);
            Assert.Equal(GeminiErrorKind.NoKey, result.Error);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task ReadsKeyFreshForEveryRequest()
        {
            int reads = 0;
            var (client, _) = Create(key: () => { reads++; return Key; },
                                     responses: new[] { Respond(200, Completed("a")), Respond(200, Completed("b")) });
            await client.CheckAsync(Model, Ct);
            await client.CheckAsync(Model, Ct);
            Assert.Equal(2, reads);
        }

        [Theory]
        [InlineData(400, "invalid_request", "Unknown field 'foo'", GeminiErrorKind.BadRequest)]
        [InlineData(400, "INVALID_ARGUMENT", "API key not valid. Please pass a valid API key.", GeminiErrorKind.InvalidKey)]
        [InlineData(401, "authentication", "Missing or invalid API key", GeminiErrorKind.InvalidKey)]
        [InlineData(402, "payment_required", "Prepaid credit exhausted", GeminiErrorKind.PaymentRequired)]
        [InlineData(403, "permission_denied", "No access to model", GeminiErrorKind.Forbidden)]
        [InlineData(404, "model_not_found", "Model not found", GeminiErrorKind.ModelNotFound)]
        [InlineData(429, "quota_exceeded", "Daily quota exceeded", GeminiErrorKind.QuotaExceeded)]
        [InlineData(429, "rate_limit_exceeded", "Too many requests per minute", GeminiErrorKind.RateLimited)]
        [InlineData(429, "too_many_requests", "Slow down", GeminiErrorKind.RateLimited)]
        [InlineData(429, "RESOURCE_EXHAUSTED", "Quota GenerateRequestsPerDayPerProjectPerModel-FreeTier exceeded", GeminiErrorKind.QuotaExceeded)]
        [InlineData(429, "QUOTA_EXCEEDED", "Daily quota exceeded", GeminiErrorKind.QuotaExceeded)]
        [InlineData(429, "RESOURCE_EXHAUSTED", "Quota GenerateRequestsPerMinutePerProjectPerModel exceeded", GeminiErrorKind.RateLimited)]
        [InlineData(302, "", "", GeminiErrorKind.UnexpectedStatus)]
        public async Task ClassifiesErrors(int status, string code, string message, GeminiErrorKind expected)
        {
            var (client, handler) = Create(responses: Respond(status, Error(code, message)));
            GeminiResult result = await client.CheckAsync(Model, Ct);
            Assert.Equal(expected, result.Error);
            Assert.Equal(status, result.StatusCode);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task ReadsLegacyNumericErrorCode()
        {
            const string legacy = "{\"error\":{\"code\":400,\"message\":\"API key not valid.\",\"status\":\"INVALID_ARGUMENT\"}}";
            var (client, _) = Create(responses: Respond(400, legacy));
            GeminiResult result = await client.CheckAsync(Model, Ct);
            Assert.Equal(GeminiErrorKind.InvalidKey, result.Error);
            Assert.Equal("API key not valid.", result.Detail);
        }

        [Fact]
        public async Task DoesNotFollowRedirects()
        {
            var (client, handler) = Create(responses: _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri("https://attacker.invalid/collect");
                return response;
            });
            GeminiResult result = await client.CheckAsync(Model, Ct);
            Assert.Equal(GeminiErrorKind.UnexpectedStatus, result.Error);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task RetriesServerErrorOnce()
        {
            var (client, handler) = Create(responses: new[] { Respond(503, Error("unavailable", "busy")), Respond(200, Completed("OK")) });
            GeminiResult result = await client.CheckAsync(Model, Ct);
            Assert.True(result.Success);
            Assert.Equal(2, handler.Requests.Count);
        }

        [Fact]
        public async Task GivesUpAfterSecondServerError()
        {
            var (client, handler) = Create(responses: new[] { Respond(500, "oops"), Respond(502, "<html>bad gateway</html>") });
            GeminiResult result = await client.CheckAsync(Model, Ct);
            Assert.Equal(GeminiErrorKind.ServerError, result.Error);
            Assert.Equal(502, result.StatusCode);
            Assert.Equal(2, handler.Requests.Count);
        }

        [Fact]
        public async Task DoesNotRetryRateLimits()
        {
            var (client, handler) = Create(responses: new[] { Respond(429, Error("rate_limit_exceeded", "slow")), Respond(200, Completed("OK")) });
            GeminiResult result = await client.CheckAsync(Model, Ct);
            Assert.Equal(GeminiErrorKind.RateLimited, result.Error);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task RedactsKeyEchoedInErrorDetail()
        {
            var (client, _) = Create(responses: Respond(400, Error("invalid_request", "bad header value " + Key)));
            GeminiResult result = await client.CheckAsync(Model, Ct);
            Assert.DoesNotContain(Key, result.Detail);
            Assert.Contains("AQ.…Wxyz", result.Detail);
        }

        [Fact]
        public async Task TruncatesLongErrorDetail()
        {
            var (client, _) = Create(responses: Respond(400, Error("invalid_request", new string('x', 500))));
            GeminiResult result = await client.CheckAsync(Model, Ct);
            Assert.Equal(201, result.Detail.Length);
        }

        [Fact]
        public async Task TruncationNeverSplitsASurrogatePair()
        {
            string message = new string('x', 199) + "𠀋" + new string('y', 50);
            var (client, _) = Create(responses: Respond(400, Error("invalid_request", message)));
            GeminiResult result = await client.CheckAsync(Model, Ct);
            Assert.Equal(new string('x', 199) + "…", result.Detail);
        }

        [Theory]
        [InlineData(403, "{\"error\":\"Unauthorized\"}")]
        [InlineData(500, "<html>500 Internal Server Error</html>")]
        public async Task FallsBackToRawBodyWhenErrorHasNoMessage(int status, string body)
        {
            var (client, _) = Create(responses: new[] { Respond(status, body), Respond(status, body) });
            GeminiResult result = await client.CheckAsync(Model, Ct);
            Assert.Equal(body, result.Detail);
        }

        [Fact]
        public async Task ReportsTimeout()
        {
            var (client, handler) = Create(timeout: TimeSpan.FromMilliseconds(100), responses: Respond(200, Completed("late")));
            handler.Delay = TimeSpan.FromSeconds(5);
            GeminiResult result = await client.CheckAsync(Model, Ct);
            Assert.Equal(GeminiErrorKind.Timeout, result.Error);
            Assert.Equal("no response within 0.1 s", result.Detail);
        }

        [Fact]
        public async Task CallerCancellationPropagates()
        {
            var (client, _) = Create(responses: Respond(200, Completed("OK")));
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CheckAsync(Model, cts.Token));
            }
        }

        [Fact]
        public async Task ReportsNetworkFailureWithoutKey()
        {
            var (client, _) = Create(responses: _ => throw new HttpRequestException("connection refused while sending " + Key));
            GeminiResult result = await client.CheckAsync(Model, Ct);
            Assert.Equal(GeminiErrorKind.Network, result.Error);
            Assert.DoesNotContain(Key, result.Detail);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[]")]
        [InlineData("{\"status\":\"failed\",\"steps\":[]}")]
        [InlineData("{\"status\":\"incomplete\",\"steps\":[{\"type\":\"model_output\",\"content\":[{\"type\":\"text\",\"text\":\"partial\"}]}]}")]
        [InlineData("{\"status\":\"completed\",\"steps\":[{\"type\":\"thought\",\"content\":[{\"type\":\"text\",\"text\":\"t\"}]}]}")]
        [InlineData("{\"status\":\"completed\",\"steps\":[{\"type\":\"model_output\",\"content\":[{\"type\":\"image\"}]}]}")]
        [InlineData("{\"status\":\"completed\"}")]
        public async Task RejectsUnusableSuccessResponses(string body)
        {
            var (client, _) = Create(responses: Respond(200, body));
            GeminiResult result = await client.CheckAsync(Model, Ct);
            Assert.Equal(GeminiErrorKind.InvalidResponse, result.Error);
        }

        [Fact]
        public async Task ConcatenatesTextPartsOfModelOutput()
        {
            const string body = "{\"status\":\"completed\",\"steps\":[{\"type\":\"model_output\",\"content\":[" +
                                "{\"type\":\"text\",\"text\":\"{\\\"first\\\":\"},{\"type\":\"text\",\"text\":\"\\\"吉賽兒\\\"}\"}]}]}";
            var (client, _) = Create(responses: Respond(200, body));
            GeminiResult result = await client.CheckAsync(Model, Ct);
            Assert.Equal("{\"first\":\"吉賽兒\"}", result.Text);
        }

        internal sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> responses;

            public FakeHandler(IEnumerable<Func<HttpRequestMessage, HttpResponseMessage>> responses)
            {
                this.responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses ?? Enumerable.Empty<Func<HttpRequestMessage, HttpResponseMessage>>());
            }

            public List<Recorded> Requests { get; } = new List<Recorded>();

            /// <summary>Simulated server latency; honours the cancellation HttpClient raises on timeout.</summary>
            public TimeSpan Delay { get; set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                request.Headers.TryGetValues("x-goog-api-key", out IEnumerable<string> keys);
                Requests.Add(new Recorded(request.Method.Method, request.RequestUri.ToString(), keys?.Single(), body));
                if (responses.Count == 0)
                    throw new InvalidOperationException("unexpected request");
                Func<HttpRequestMessage, HttpResponseMessage> next = responses.Dequeue();
                if (Delay > TimeSpan.Zero)
                    await Task.Delay(Delay, cancellationToken);
                return next(request);
            }

            internal sealed class Recorded
            {
                public Recorded(string method, string url, string keyHeader, string body)
                {
                    Method = method;
                    Url = url;
                    KeyHeader = keyHeader;
                    Body = body;
                }

                public string Method { get; }

                public string Url { get; }

                public string KeyHeader { get; }

                public string Body { get; }
            }
        }
    }
}
