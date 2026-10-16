using System.Threading;
using System.Threading.Tasks;
using RimTaiName.Pipeline;

namespace RimTaiName.Llm
{
    /// <summary>
    /// Translates the fields the local pipeline could not resolve, or (Generate mode) creates new names for them.
    /// </summary>
    public interface ILlmClient
    {
        /// <summary>
        /// Must not throw for API or network errors (return <see cref="LlmResponse.Fail"/>); may throw
        /// <see cref="System.OperationCanceledException"/> when <paramref name="cancellationToken"/> is cancelled.
        /// </summary>
        Task<LlmResponse> TranslateAsync(LlmRequest request, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Translate: all three fields go along as context; only the <see cref="Wanted"/> ones are translated.
    /// Generate: the wanted fields get new names; locked fields are sent as a style reference.
    /// </summary>
    public sealed class LlmRequest
    {
        public ProcessMode Mode { get; set; } = ProcessMode.Translate;

        public string[] Text { get; } = new string[NameFields.Count];

        public bool[] Wanted { get; } = new bool[NameFields.Count];

        public bool[] Locked { get; } = new bool[NameFields.Count];

        public NickSource NickSource { get; set; }

        /// <summary>Generate mode only.</summary>
        public PawnContext Context { get; set; }

        /// <summary>Generate mode only: picks the initial-letter hints.</summary>
        public int Seed { get; set; }
    }

    public sealed class LlmResponse
    {
        private readonly string[] values = new string[NameFields.Count];

        private LlmResponse(GeminiErrorKind error, int statusCode, string detail)
        {
            Error = error;
            StatusCode = statusCode;
            Detail = detail;
        }

        public bool Success => Error == GeminiErrorKind.None;

        public GeminiErrorKind Error { get; }

        public int StatusCode { get; }

        /// <summary>Already redacted by the client.</summary>
        public string Detail { get; }

        /// <summary>The raw (unsanitized) translation for a field, or null when the model returned none.</summary>
        public string this[NameField field] => values[(int)field];

        public static LlmResponse Ok(string first, string nick, string last)
        {
            var response = new LlmResponse(GeminiErrorKind.None, 200, null);
            response.values[(int)NameField.First] = first;
            response.values[(int)NameField.Nick] = nick;
            response.values[(int)NameField.Last] = last;
            return response;
        }

        public static LlmResponse Fail(GeminiErrorKind error, int statusCode = 0, string detail = null) =>
            new LlmResponse(error, statusCode, detail);
    }
}
