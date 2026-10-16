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
            // A nickname still equal to the English first/last name (now or as opened) is that name.
            if (Same(nick, payload[NameField.First]) || Same(nick, payload.OriginalText[(int)NameField.First]))
                return NameField.First;
            if (Same(nick, payload[NameField.Last]) || Same(nick, payload.OriginalText[(int)NameField.Last]))
                return NameField.Last;
            return null;
        }

        private static bool IsBlank(string text) => string.IsNullOrWhiteSpace(text);

        private static bool Same(string a, string b) =>
            !IsBlank(a) && !IsBlank(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
