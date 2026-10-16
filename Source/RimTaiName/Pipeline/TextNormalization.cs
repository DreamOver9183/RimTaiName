using System;
using System.Text;

namespace RimTaiName.Pipeline
{
    internal static class TextNormalization
    {
        /// <summary>
        /// <see cref="string.Normalize(NormalizationForm)"/> throws on ill-formed UTF-16 (a lone surrogate);
        /// such text is returned unchanged instead, and later checks reject it.
        /// </summary>
        public static string Normalize(string text, NormalizationForm form)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            try
            {
                return text.Normalize(form);
            }
            catch (ArgumentException)
            {
                return text;
            }
        }
    }
}
