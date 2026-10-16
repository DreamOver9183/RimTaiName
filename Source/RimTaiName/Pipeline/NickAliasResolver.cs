using System;

namespace RimTaiName.Pipeline
{
    /// <summary>The nickname rules of v3 §6.3.</summary>
    public static class NickAliasResolver
    {
        /// <summary>
        /// Decides at dialog open how the nickname relates to the other fields.
        /// <paramref name="nickField"/> is the nickname box (blank when the name has no nickname set) and
        /// <paramref name="resolvedNick"/> is <c>NameTriple.Nick</c>, which falls back to the first or last name.
        /// </summary>
        public static NickSource Determine(string first, string nickField, string last, bool nickSet, string resolvedNick)
        {
            string nick = !nickSet || IsBlank(nickField) ? resolvedNick : nickField;
            if (Same(nick, first))
                return NickSource.AliasOfFirst;
            if (Same(nick, last))
                return NickSource.AliasOfLast;
            return NickSource.Explicit;
        }

        /// <summary>
        /// The field the nickname must take its translation from for this request, or null for an
        /// independent nickname (or a locked one, or a blank one the player wants left blank).
        /// </summary>
        public static NameField? Target(NameRequestPayload payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            if (payload.Locked[(int)NameField.Nick])
                return null;
            string nick = payload[NameField.Nick];
            if (IsBlank(nick))
            {
                if (!payload.KeepVanillaAddress)
                    return null;
                if (payload.NickSource == NickSource.AliasOfFirst)
                    return NameField.First;
                if (payload.NickSource == NickSource.AliasOfLast)
                    return NameField.Last;
                return null;
            }
            if (Mirrors(payload, nick, NameField.First))
                return NameField.First;
            if (Mirrors(payload, nick, NameField.Last))
                return NameField.Last;
            return null;
        }

        /// <summary>
        /// The nickname stands for <paramref name="field"/> when it equals that field's English text now, or
        /// equals the text as opened while the field has since been translated to Chinese. A field rerolled to
        /// another English name no longer counts: the old nickname is then a nickname of its own.
        /// </summary>
        private static bool Mirrors(NameRequestPayload payload, string nick, NameField field)
        {
            string current = payload[field];
            if (Same(nick, current))
                return true;
            return NameSanitizer.ContainsChinese(current) && Same(nick, payload.OriginalText[(int)field]);
        }

        private static bool IsBlank(string text) => string.IsNullOrWhiteSpace(text);

        /// <summary>Same name after Trim, NFC and lower case (NameKey), so "André" in NFD still matches.</summary>
        private static bool Same(string a, string b) =>
            !IsBlank(a) && !IsBlank(b) && string.Equals(NameKey.Normalize(a), NameKey.Normalize(b), StringComparison.Ordinal);
    }
}
