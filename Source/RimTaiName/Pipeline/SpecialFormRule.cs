using System.Text;
using System.Text.RegularExpressions;

namespace RimTaiName.Pipeline
{
    /// <summary>
    /// Deterministic translations for initials, mirroring <c>special_form()</c> in Tools/name_rules.py:
    /// after Trim and NFKC, 2–4 capital letters stay as written (MJ), and a single capital stays itself as a
    /// first or last name but becomes "阿" + letter as a nickname (阿B).
    /// </summary>
    public static class SpecialFormRule
    {
        private static readonly Regex Abbreviation = new Regex(@"^[A-Z]{2,4}\z", RegexOptions.CultureInvariant);

        private static readonly Regex SingleLetter = new Regex(@"^[A-Z]\z", RegexOptions.CultureInvariant);

        /// <summary>The fixed translation, or null when the text is not a special form.</summary>
        public static string Apply(string text, NameSpace ns)
        {
            if (text == null)
                return null;
            string folded = TextNormalization.Normalize(text.Trim(), NormalizationForm.FormKC);
            if (Abbreviation.IsMatch(folded))
                return folded;
            if (SingleLetter.IsMatch(folded))
                return ns == NameSpace.Name ? folded : "阿" + folded;
            return null;
        }
    }
}
