using System;
using System.Text.RegularExpressions;

namespace RimTaiName.Llm
{
    /// <summary>Validates Gemini model ids so a model name can never rewrite the request URL or body.</summary>
    public static class ModelId
    {
        public const string Default = "gemini-3.5-flash-lite";

        public const int MaxLength = 64;

        private const string ResourcePrefix = "models/";

        private static readonly Regex Pattern =
            new Regex(@"^[a-z0-9]+(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant);

        /// <summary>
        /// Trims the input and drops a pasted "models/" prefix. Returns false when the result is not a valid id
        /// (empty, too long, upper case, or containing anything other than letters, digits, single "." or "-").
        /// </summary>
        public static bool TryNormalize(string input, out string modelId)
        {
            modelId = null;
            if (input == null)
                return false;
            string candidate = input.Trim();
            if (candidate.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                candidate = candidate.Substring(ResourcePrefix.Length);
            if (candidate.Length == 0 || candidate.Length > MaxLength || !Pattern.IsMatch(candidate))
                return false;
            modelId = candidate;
            return true;
        }
    }
}
