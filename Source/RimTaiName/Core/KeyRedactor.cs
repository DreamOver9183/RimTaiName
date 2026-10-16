using System;
using System.Text.RegularExpressions;

namespace RimTaiName.Core
{
    /// <summary>
    /// Masks Gemini API keys in text before it is logged or shown. Pure C#: also compiled into the test project.
    /// </summary>
    public static class KeyRedactor
    {
        /// <summary>
        /// Legacy "AIza" keys and the "AQ." auth keys AI Studio has issued since 2026-05. An "AQ." match may not end
        /// with "." so a full stop after a key in a message is not swallowed.
        /// </summary>
        private static readonly Regex KeyPattern =
            new Regex(@"AIza[0-9A-Za-z_\-]{35}|AQ\.[0-9A-Za-z_.\-]{19,}[0-9A-Za-z_\-]", RegexOptions.CultureInvariant);

        /// <summary>Shorter "known keys" are not real keys; replacing them would only mangle ordinary text.</summary>
        private const int MinKnownKeyLength = 8;

        /// <summary>Replaces <paramref name="knownKey"/> and anything shaped like a Gemini key with its masked form.</summary>
        public static string Redact(string text, string knownKey = null)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            if (knownKey != null && knownKey.Length >= MinKnownKeyLength)
                text = text.Replace(knownKey, Mask(knownKey));
            return KeyPattern.Replace(text, m => Mask(m.Value));
        }

        /// <summary>"AIza…wxyz" / "AQ.…wxyz": the recognised prefix plus the last four characters.</summary>
        public static string Mask(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;
            string prefix = key.StartsWith("AIza", StringComparison.Ordinal) ? "AIza"
                : key.StartsWith("AQ.", StringComparison.Ordinal) ? "AQ."
                : string.Empty;
            string last4 = key.Length >= prefix.Length + 8 ? key.Substring(key.Length - 4) : string.Empty;
            return prefix + "…" + last4;
        }
    }
}
