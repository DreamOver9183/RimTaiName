namespace RimTaiName.Llm
{
    public enum GeminiErrorKind
    {
        None,

        /// <summary>No API key is stored; nothing was sent.</summary>
        NoKey,

        /// <summary>The model id failed <see cref="ModelId.TryNormalize"/>; nothing was sent.</summary>
        InvalidModelId,

        /// <summary>401, or 400 whose message is about the API key.</summary>
        InvalidKey,

        /// <summary>403: no access to this model, or the key is restricted.</summary>
        Forbidden,

        /// <summary>Any other 400.</summary>
        BadRequest,

        /// <summary>402: paid credit required or exhausted.</summary>
        PaymentRequired,

        /// <summary>404: unknown model id.</summary>
        ModelNotFound,

        /// <summary>429 for the daily quota; it resets at midnight Pacific time.</summary>
        QuotaExceeded,

        /// <summary>Any other 429 (per-minute limits).</summary>
        RateLimited,

        /// <summary>5xx after one retry.</summary>
        ServerError,

        Timeout,

        /// <summary>DNS, TLS or connection failure.</summary>
        Network,

        /// <summary>A redirect or other status the client does not handle (redirects are never followed).</summary>
        UnexpectedStatus,

        /// <summary>The response was not valid JSON, not completed, or had no model text.</summary>
        InvalidResponse,
    }

    /// <summary>Outcome of one Gemini request. <see cref="Detail"/> is already redacted and at most 200 characters.</summary>
    public sealed class GeminiResult
    {
        private GeminiResult(GeminiErrorKind error, string text, int statusCode, string detail)
        {
            Error = error;
            Text = text;
            StatusCode = statusCode;
            Detail = detail;
        }

        public bool Success => Error == GeminiErrorKind.None;

        public GeminiErrorKind Error { get; }

        /// <summary>The concatenated model output text; null on failure.</summary>
        public string Text { get; }

        /// <summary>HTTP status, or 0 when no response was received.</summary>
        public int StatusCode { get; }

        public string Detail { get; }

        public static GeminiResult Ok(string text) => new GeminiResult(GeminiErrorKind.None, text, 200, null);

        public static GeminiResult Fail(GeminiErrorKind error, int statusCode = 0, string detail = null) =>
            new GeminiResult(error, null, statusCode, detail);
    }
}
