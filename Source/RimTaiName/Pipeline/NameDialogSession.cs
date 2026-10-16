using System;
using System.Collections.Generic;
using System.Globalization;

namespace RimTaiName.Pipeline
{
    /// <summary>A value the naming dialog should write into one field on the next Layout event.</summary>
    public sealed class FieldWrite
    {
        public FieldWrite(string value, string sourceText, bool learnable)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
            SourceText = sourceText;
            Learnable = learnable && !string.IsNullOrWhiteSpace(sourceText);
        }

        public string Value { get; }

        /// <summary>The English text the value translates; null for a restore.</summary>
        public string SourceText { get; }

        /// <summary>
        /// A later edit by the player counts as a correction of this translation (personal cache, dictionary or
        /// Gemini results). Special forms and aliases are not: special forms are applied before the cache is read,
        /// and an alias only copies another field.
        /// </summary>
        public bool Learnable { get; }
    }

    /// <summary>A player's correction, stored in the personal cache with origin user.</summary>
    public sealed class NameCorrection
    {
        public NameCorrection(NameSpace space, string key, string value)
        {
            Space = space;
            Key = key;
            Value = value;
        }

        public NameSpace Space { get; }

        /// <summary>Already normalized with <see cref="NameKey.Normalize"/>.</summary>
        public string Key { get; }

        public string Value { get; }
    }

    /// <summary>
    /// The naming dialog's translation bookkeeping (v3 §5.3), kept free of game types so it can be unit tested:
    /// what a request should contain, which fields a late Gemini result may still overwrite, and which edits
    /// count as corrections when the dialog is accepted. Main thread only.
    /// </summary>
    public sealed class NameDialogSession
    {
        private readonly string[] opened = new string[NameFields.Count];

        /// <summary>The learnable value the dialog last wrote into each field, until the player replaces it with non-Chinese text.</summary>
        private readonly string[] written = new string[NameFields.Count];

        private readonly string[] writtenSource = new string[NameFields.Count];

        /// <summary>What each field held right after the last translation's local writes.</summary>
        private readonly string[] expected = new string[NameFields.Count];

        /// <summary>Bumped by every translation and restore, so a result that arrives after either is dropped.</summary>
        private int generation;

        private int remoteGeneration = -1;

        public NameDialogSession(string first, string nick, string last, NickSource nickSource)
        {
            opened[(int)NameField.First] = first;
            opened[(int)NameField.Nick] = nick;
            opened[(int)NameField.Last] = last;
            NickSource = nickSource;
        }

        /// <summary>The lock checkboxes, indexed by <see cref="NameField"/>; the dialog toggles them directly.</summary>
        public bool[] Locked { get; } = new bool[NameFields.Count];

        /// <summary>How the nickname related to the other fields when the dialog opened.</summary>
        public NickSource NickSource { get; }

        /// <summary>A remote result is expected and has not been superseded by a restore.</summary>
        public bool AwaitingRemote => remoteGeneration == generation;

        public string Opened(NameField field) => opened[Index(field)];

        /// <summary>
        /// The request for the fields as they are now. A field the dialog does not let the player edit counts as
        /// locked, so the pipeline never plans a write the dialog would refuse.
        /// </summary>
        public NameRequestPayload CreatePayload(IReadOnlyList<string> current, IReadOnlyList<bool> editable, bool keepVanillaAddress)
        {
            CheckCount(current, nameof(current));
            CheckCount(editable, nameof(editable));
            var payload = new NameRequestPayload(current[0], current[1], current[2])
            {
                NickSource = NickSource,
                KeepVanillaAddress = keepVanillaAddress,
            };
            for (int i = 0; i < NameFields.Count; i++)
            {
                payload.OriginalText[i] = opened[i];
                payload.Locked[i] = Locked[i] || !editable[i];
            }
            return payload;
        }

        /// <summary>
        /// Starts a translation: returns the locally resolved values to write now (null entries are left alone)
        /// and remembers what every field will hold, so <see cref="CompleteTranslation"/> can tell which fields the
        /// player changed while waiting.
        /// </summary>
        public FieldWrite[] BeginTranslation(NameResult local, IReadOnlyList<string> current, bool awaitRemote)
        {
            if (local == null)
                throw new ArgumentNullException(nameof(local));
            CheckCount(current, nameof(current));
            generation++;
            remoteGeneration = awaitRemote ? generation : -1;
            var writes = new FieldWrite[NameFields.Count];
            for (int i = 0; i < NameFields.Count; i++)
            {
                FieldResult result = local.Fields[i];
                if (result.HasNewValue && !Locked[i] && !SameText(result.Value, current[i]))
                    writes[i] = ToWrite(result);
                expected[i] = writes[i]?.Value ?? current[i];
            }
            return writes;
        }

        /// <summary>
        /// The Gemini result for the latest translation: writes only fields that are unlocked and still hold what
        /// they held when the request started. Returns no writes after a restore (the result only feeds the cache).
        /// </summary>
        public FieldWrite[] CompleteTranslation(NameResult remote, IReadOnlyList<string> current)
        {
            CheckCount(current, nameof(current));
            var writes = new FieldWrite[NameFields.Count];
            if (!AwaitingRemote)
                return writes;
            remoteGeneration = -1; // also when the request ended without a result
            if (remote == null)
                return writes;
            for (int i = 0; i < NameFields.Count; i++)
            {
                FieldResult result = remote.Fields[i];
                if (result.HasNewValue && !Locked[i] && SameText(current[i], expected[i]) && !SameText(result.Value, current[i]))
                    writes[i] = ToWrite(result);
            }
            return writes;
        }

        /// <summary>Unlocked fields go back to their values at opening; a pending result will no longer be applied.</summary>
        public FieldWrite[] Restore()
        {
            generation++;
            remoteGeneration = -1;
            var writes = new FieldWrite[NameFields.Count];
            for (int i = 0; i < NameFields.Count; i++)
            {
                if (!Locked[i])
                    writes[i] = new FieldWrite(opened[i] ?? string.Empty, null, learnable: false);
            }
            return writes;
        }

        /// <summary>Main thread, once the dialog has actually put <paramref name="write"/> into the field.</summary>
        public void MarkWritten(NameField field, FieldWrite write)
        {
            if (write == null)
                throw new ArgumentNullException(nameof(write));
            int i = Index(field);
            written[i] = write.Learnable ? write.Value : null;
            writtenSource[i] = write.Learnable ? write.SourceText : null;
        }

        /// <summary>
        /// Called every frame: once the player replaces a translation with another (English) name, by rerolling or
        /// typing, a later Chinese value is no longer a correction of it. A box that is only emptied or holds
        /// Bopomofo while the player types Chinese keeps the link.
        /// </summary>
        public void Observe(IReadOnlyList<string> current)
        {
            CheckCount(current, nameof(current));
            for (int i = 0; i < NameFields.Count; i++)
            {
                if (written[i] != null && !NameSanitizer.ContainsChinese(current[i]) && HasNonCjkLetter(current[i]))
                {
                    written[i] = null;
                    writtenSource[i] = null;
                }
            }
        }

        /// <summary>
        /// On accept: every field whose translation the player changed to other Chinese text, keyed by the English
        /// it translated. A value picked from the dialog's suggestions (a newborn's parent surname) is a choice,
        /// not a correction.
        /// </summary>
        public IList<NameCorrection> Corrections(IReadOnlyList<string> final, Func<NameField, ICollection<string>> suggestions = null)
        {
            CheckCount(final, nameof(final));
            var corrections = new List<NameCorrection>();
            foreach (NameField field in NameFields.All)
            {
                int i = (int)field;
                string value = final[i]?.Trim();
                if (written[i] == null || string.IsNullOrEmpty(value) || value == written[i] || !NameSanitizer.ContainsChinese(value))
                    continue;
                if (suggestions?.Invoke(field)?.Contains(value) == true)
                    continue;
                NameSpace ns = NameFields.SpaceOf(field);
                string key = NameKey.Normalize(writtenSource[i]);
                string clean = NameSanitizer.Sanitize(writtenSource[i], value, ns);
                if (string.IsNullOrEmpty(key) || clean == null || clean == written[i])
                    continue;
                corrections.Add(new NameCorrection(ns, key, clean));
            }
            return corrections;
        }

        private static FieldWrite ToWrite(FieldResult result) =>
            new FieldWrite(result.Value, result.SourceText, IsLearnable(result.Source));

        private static bool IsLearnable(FieldSource source) =>
            source == FieldSource.PersonalCache || source == FieldSource.Dictionary || source == FieldSource.Api;

        /// <summary>
        /// A letter below the CJK blocks (U+2E80): Latin, accented Latin, Greek, Cyrillic and the like. Modifier
        /// letters are excluded: the Bopomofo tone marks ˇ ˊ ˋ live there.
        /// </summary>
        private static bool HasNonCjkLetter(string text)
        {
            if (text == null)
                return false;
            foreach (char c in text)
            {
                if (c < '⺀' && char.IsLetter(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.ModifierLetter)
                    return true;
            }
            return false;
        }

        private static int Index(NameField field)
        {
            int i = (int)field;
            if (i < 0 || i >= NameFields.Count)
                throw new ArgumentOutOfRangeException(nameof(field));
            return i;
        }

        private static bool SameText(string a, string b) => string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);

        private static void CheckCount<T>(IReadOnlyList<T> values, string name)
        {
            if (values == null)
                throw new ArgumentNullException(name);
            if (values.Count != NameFields.Count)
                throw new ArgumentException("Expected one entry per name field.", name);
        }
    }
}
