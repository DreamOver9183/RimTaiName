using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimTaiName.Pipeline;

namespace RimTaiName.Llm
{
    /// <summary>
    /// <see cref="ILlmClient"/> on top of the Gemini transport: builds the prompt, sends one interaction and reads
    /// the structured JSON answer. Raw values are returned unsanitized; the pipeline sanitizes them per field.
    /// </summary>
    public sealed class GeminiNameTranslator : ILlmClient
    {
        private readonly GeminiInteractionsClient transport;

        private readonly PromptBuilder prompts;

        private readonly Func<string> modelProvider;

        /// <param name="modelProvider">Read for every request, so a model changed in the settings applies at once.</param>
        public GeminiNameTranslator(GeminiInteractionsClient transport, PromptBuilder prompts, Func<string> modelProvider)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.prompts = prompts ?? throw new ArgumentNullException(nameof(prompts));
            this.modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
        }

        public async Task<LlmResponse> TranslateAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            GeminiResult result = await transport
                .SendAsync(modelProvider(), prompts.BuildBody(request), cancellationToken)
                .ConfigureAwait(false);
            return result.Success ? ParseOutput(result.Text) : LlmResponse.Fail(result.Error, result.StatusCode, result.Detail);
        }

        /// <summary>
        /// Reads <c>{"first":…,"nick":…,"last":…}</c>. A field that is missing or not a string comes back as null;
        /// output that is not a JSON object is an <see cref="GeminiErrorKind.InvalidResponse"/>.
        /// </summary>
        internal static LlmResponse ParseOutput(string text)
        {
            Dictionary<string, object> obj;
            try
            {
                obj = MiniJson.AsObject(MiniJson.Parse(StripCodeFence(text)));
            }
            catch (FormatException)
            {
                return LlmResponse.Fail(GeminiErrorKind.InvalidResponse, 200, "model output is not JSON");
            }
            if (obj == null)
                return LlmResponse.Fail(GeminiErrorKind.InvalidResponse, 200, "model output is not a JSON object");
            return LlmResponse.Ok(
                MiniJson.GetString(obj, PromptBuilder.Id(NameField.First)),
                MiniJson.GetString(obj, PromptBuilder.Id(NameField.Nick)),
                MiniJson.GetString(obj, PromptBuilder.Id(NameField.Last)));
        }

        /// <summary>Structured output should be bare JSON, but tolerate a ```json … ``` wrapper.</summary>
        private static string StripCodeFence(string text)
        {
            string trimmed = (text ?? string.Empty).Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
                return trimmed;
            int firstNewline = trimmed.IndexOf('\n');
            int closing = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline < 0 || closing <= firstNewline)
                return trimmed;
            return trimmed.Substring(firstNewline + 1, closing - firstNewline - 1).Trim();
        }
    }
}
