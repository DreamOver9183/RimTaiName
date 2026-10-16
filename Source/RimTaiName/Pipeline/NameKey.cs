using System.Text;

namespace RimTaiName.Pipeline
{
    /// <summary>
    /// Lookup key for the dictionary and the personal cache: Trim → NFC → lower case, accents kept
    /// ("  André " → "andré"). Must match Tools/merge_dictionary.py, which wrote the dictionary keys.
    /// </summary>
    public static class NameKey
    {
        public static string Normalize(string text)
        {
            if (text == null)
                return null;
            return TextNormalization.Normalize(text.Trim(), NormalizationForm.FormC).ToLowerInvariant();
        }
    }
}
