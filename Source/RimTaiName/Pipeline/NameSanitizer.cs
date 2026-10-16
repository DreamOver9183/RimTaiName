using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RimTaiName.Pipeline
{
    /// <summary>The failing results of <c>check()</c> in Tools/name_rules.py.</summary>
    [Flags]
    public enum NameCheck
    {
        None = 0,

        /// <summary>空白: the translation is empty.</summary>
        Blank = 1,

        /// <summary>含非中文字元: a character that is neither CJK nor a capital letter of the source.</summary>
        NonChinese = 2,

        /// <summary>超過長度上限: longer than 12 (name) or 16 (nickname).</summary>
        TooLong = 4,
    }

    /// <summary>
    /// Validates and cleans translations before they are written into a name field (v3 §8.4). Every value
    /// from Gemini, the dictionary or the personal cache goes through <see cref="Sanitize"/> with the real
    /// source text, so a case-insensitive lookup can never smuggle in a capital the source lacks.
    /// </summary>
    public static class NameSanitizer
    {
        public const int MaxNameLength = 12;

        public const int MaxNickLength = 16;

        /// <summary>Copy of RimWorld's CharacterCardUtility.ValidNameRegex; text failing it is silently dropped by the game.</summary>
        private static readonly Regex ValidNameRegex = new Regex(@"^[\p{L}0-9 '\-.]*$", RegexOptions.CultureInvariant);

        public static int MaxLength(NameSpace ns) => ns == NameSpace.Nick ? MaxNickLength : MaxNameLength;

        /// <summary>
        /// Mirrors name_rules.check(): Blank, NonChinese (any character outside U+3400–U+9FFF that is not a
        /// capital letter of the NFKC source) and TooLong. Kept identical to Python for the fixture test.
        /// </summary>
        public static NameCheck Check(string source, string translation, NameSpace ns)
        {
            if (string.IsNullOrEmpty(translation))
                return NameCheck.Blank;
            NameCheck result = NameCheck.None;
            HashSet<char> capitals = SourceCapitals(source);
            foreach (char c in translation)
            {
                if (!(c >= '㐀' && c <= '鿿') && !capitals.Contains(c))
                {
                    result |= NameCheck.NonChinese;
                    break;
                }
            }
            if (translation.Length > MaxLength(ns))
                result |= NameCheck.TooLong;
            return result;
        }

        /// <summary>
        /// NFKC → Trim → drop whitespace, punctuation and symbols (e.g. "·" in 尚·呂克) → only CJK ideographs
        /// (U+3400–U+4DBF, U+4E00–U+9FFF) and capitals that appear in the source may remain → length limit →
        /// the game's name regex. Returns null instead of truncating or guessing.
        /// </summary>
        public static string Sanitize(string source, string candidate, NameSpace ns)
        {
            if (candidate == null)
                return null;
            string folded = TextNormalization.Normalize(candidate, NormalizationForm.FormKC).Trim();
            var kept = new StringBuilder(folded.Length);
            foreach (char c in folded)
            {
                if (!IsRemovable(c))
                    kept.Append(c);
            }
            string text = kept.ToString();
            if (text.Length == 0 || text.Length > MaxLength(ns))
                return null;
            HashSet<char> capitals = SourceCapitals(source);
            foreach (char c in text)
            {
                if (!IsCjkIdeograph(c) && !capitals.Contains(c))
                    return null;
            }
            return ValidNameRegex.IsMatch(text) ? text : null;
        }

        /// <summary>True when the text already contains a Chinese character (the field counts as translated).</summary>
        public static bool ContainsChinese(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (IsCjkIdeograph(c) || (c >= '豈' && c <= '﫿'))
                    return true;
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    int codePoint = char.ConvertToUtf32(c, text[i + 1]);
                    if (codePoint >= 0x20000 && codePoint <= 0x3134F)
                        return true;
                    i++;
                }
            }
            return false;
        }

        internal static bool IsCjkIdeograph(char c) => (c >= '㐀' && c <= '䶿') || (c >= '一' && c <= '鿿');

        private static bool IsRemovable(char c)
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsControl(c))
                return true;
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            return category == UnicodeCategory.Format || category == UnicodeCategory.SpaceSeparator;
        }

        private static HashSet<char> SourceCapitals(string source)
        {
            var capitals = new HashSet<char>();
            if (source == null)
                return capitals;
            foreach (char c in TextNormalization.Normalize(source, NormalizationForm.FormKC))
            {
                if (c >= 'A' && c <= 'Z')
                    capitals.Add(c);
            }
            return capitals;
        }
    }
}
