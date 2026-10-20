using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimTaiName.Core;

namespace RimTaiName.Llm
{
    /// <summary>
    /// Transport for the Gemini Interactions API. The endpoint is fixed, redirects are never followed, the key is
    /// read from the injected provider only while a request is being built and only goes into the
    /// <c>x-goog-api-key</c> header, and every detail returned to callers is redacted.
    /// </summary>
    public sealed class GeminiInteractionsClient
    {
        public const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/interactions";

        private const string KeyHeader = "x-goog-api-key";

        private const int MaxDetailLength = 200;

        /// <summary>
        /// Generous on purpose: an overloaded free-tier model answers slowly rather than failing (2026-10-21:
        /// a one-word reply from flash-lite took 17 s, and 15 s timed out every request).
        /// </summary>
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

        private readonly HttpClient http;

        private readonly Func<string> keyProvider;

        private readonly Func<TimeSpan, CancellationToken, Task> delay;

        public GeminiInteractionsClient(Func<string> keyProvider)
            : this(keyProvider, new HttpClientHandler { AllowAutoRedirect = false }, DefaultTimeout, null)
        {
        }

        /// <summary>For tests: a fake handler, a short timeout and an instant retry delay.</summary>
        internal GeminiInteractionsClient(Func<string> keyProvider, HttpMessageHandler handler, TimeSpan timeout,
                                          Func<TimeSpan, CancellationToken, Task> delay)
        {
            this.keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
            http = new HttpClient(handler) { Timeout = timeout };
            this.delay = delay ?? ((span, token) => Task.Delay(span, token));
        }

        /// <summary>
        /// "Test connection": one tiny request that proves the key, model, permission and quota in one go.
        /// It counts against the daily quota like any other request.
        /// </summary>
        public Task<GeminiResult> CheckAsync(string modelId, CancellationToken cancellationToken = default(CancellationToken))
        {
            var body = new Dictionary<string, object>
            {
                ["system_instruction"] = "Reply with the single word OK.",
                ["input"] = "ping",
            };
            return SendAsync(modelId, body, cancellationToken);
        }

        /// <summary>
        /// Sends one interaction. <paramref name="body"/> holds the request fields except <c>model</c> and
        /// <c>store</c>, which are always set here (<c>store: false</c>).
        /// </summary>
        public async Task<GeminiResult> SendAsync(string modelId, Dictionary<string, object> body,
                                                  CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!ModelId.TryNormalize(modelId, out string model))
                return GeminiResult.Fail(GeminiErrorKind.InvalidModelId);
            var payload = new Dictionary<string, object>(body ?? new Dictionary<string, object>(), StringComparer.Ordinal)
            {
                ["model"] = model,
                ["store"] = false,
            };
            string json = MiniJson.Serialize(payload);

            for (int attempt = 1; ; attempt++)
            {
                GeminiResult result = await SendOnceAsync(json, cancellationToken).ConfigureAwait(false);
                if (result.Error != GeminiErrorKind.ServerError || attempt >= 2)
                    return result;
                await delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<GeminiResult> SendOnceAsync(string json, CancellationToken cancellationToken)
        {
            string key = keyProvider();
            if (string.IsNullOrEmpty(key))
                return GeminiResult.Fail(GeminiErrorKind.NoKey);
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, Endpoint))
                {
                    request.Headers.TryAddWithoutValidation(KeyHeader, key);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        string text = response.Content == null
                            ? string.Empty
                            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        int status = (int)response.StatusCode;
                        return status >= 200 && status < 300
                            ? ParseSuccess(text, key)
                            : ParseError(status, text, key);
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient reports its own timeout as a cancellation the caller did not ask for.
                return GeminiResult.Fail(GeminiErrorKind.Timeout, 0,
                    "no response within " + http.Timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s");
            }
            catch (HttpRequestException e)
            {
                return GeminiResult.Fail(GeminiErrorKind.Network, 0, Shorten(e.GetBaseException().Message, key));
            }
        }

        /// <summary>
        /// Concatenates the text parts of the <c>model_output</c> step. A <c>thought</c> step may precede it, and the
        /// interaction must be <c>completed</c> when a status is given.
        /// </summary>
        internal static GeminiResult ParseSuccess(string text, string key)
        {
            Dictionary<string, object> root;
            try
            {
                root = MiniJson.AsObject(MiniJson.Parse(text));
            }
            catch (FormatException e)
            {
                return GeminiResult.Fail(GeminiErrorKind.InvalidResponse, 200, Shorten(e.Message, key));
            }
            if (root == null)
                return GeminiResult.Fail(GeminiErrorKind.InvalidResponse, 200, "response is not a JSON object");
            string status = MiniJson.GetString(root, "status");
            if (status != null && status != "completed")
                return GeminiResult.Fail(GeminiErrorKind.InvalidResponse, 200, Shorten("interaction status " + status, key));

            var output = new StringBuilder();
            bool found = false;
            foreach (object step in MiniJson.AsArray(MiniJson.Get(root, "steps")) ?? new List<object>())
            {
                var stepObj = MiniJson.AsObject(step);
                if (MiniJson.GetString(stepObj, "type") != "model_output")
                    continue;
                foreach (object part in MiniJson.AsArray(MiniJson.Get(stepObj, "content")) ?? new List<object>())
                {
                    var partObj = MiniJson.AsObject(part);
                    if (MiniJson.GetString(partObj, "type") == "text")
                    {
                        output.Append(MiniJson.GetString(partObj, "text"));
                        found = true;
                    }
                }
            }
            return found && output.Length > 0
                ? GeminiResult.Ok(output.ToString())
                : GeminiResult.Fail(GeminiErrorKind.InvalidResponse, 200, "no model_output text in response");
        }

        /// <summary>
        /// Interactions errors are <c>{"error":{"code":"snake_case","message":…}}</c>; older endpoints use a numeric
        /// code plus <c>status</c>. Both are read leniently.
        /// </summary>
        internal static GeminiResult ParseError(int status, string text, string key)
        {
            string code = null;
            string message = null;
            try
            {
                var error = MiniJson.AsObject(MiniJson.Get(MiniJson.AsObject(MiniJson.Parse(text)), "error"));
                object rawCode = MiniJson.Get(error, "code");
                code = rawCode as string ?? MiniJson.GetString(error, "status");
                message = MiniJson.GetString(error, "message");
            }
            catch (FormatException)
            {
                // Not JSON (e.g. an HTML page from a proxy): the raw text is the only detail there is.
            }
            if (string.IsNullOrEmpty(message))
                message = text;
            return GeminiResult.Fail(Classify(status, code, message), status, Shorten(message, key));
        }

        internal static GeminiErrorKind Classify(int status, string code, string message)
        {
            switch (status)
            {
                case 400:
                    return Mentions(message, "api key") || Mentions(code, "api_key") ? GeminiErrorKind.InvalidKey : GeminiErrorKind.BadRequest;
                case 401:
                    return GeminiErrorKind.InvalidKey;
                case 402:
                    return GeminiErrorKind.PaymentRequired;
                case 403:
                    return GeminiErrorKind.Forbidden;
                case 404:
                    return GeminiErrorKind.ModelNotFound;
                case 429:
                    // Not RESOURCE_EXHAUSTED: the legacy API uses it for per-minute limits as well.
                    return string.Equals(code, "quota_exceeded", StringComparison.OrdinalIgnoreCase) ||
                           Mentions(message, "per day") || Mentions(message, "perday")
                        ? GeminiErrorKind.QuotaExceeded
                        : GeminiErrorKind.RateLimited;
                default:
                    return status >= 500 ? GeminiErrorKind.ServerError : GeminiErrorKind.UnexpectedStatus;
            }
        }

        private static bool Mentions(string text, string fragment) =>
            text != null && text.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string Shorten(string text, string key)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            string redacted = KeyRedactor.Redact(text, key).Trim();
            if (redacted.Length <= MaxDetailLength)
                return redacted;
            int cut = char.IsHighSurrogate(redacted[MaxDetailLength - 1]) ? MaxDetailLength - 1 : MaxDetailLength;
            return redacted.Substring(0, cut) + "…";
        }
    }
}
