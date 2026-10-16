using System;
using System.Collections.Generic;
using RimTaiName.Llm;

namespace RimTaiName.Pipeline
{
    /// <summary>Index into the three name fields; matches the dialog's nameIndex (0 first, 1 nick, 2 last).</summary>
    public enum NameField
    {
        First = 0,
        Nick = 1,
        Last = 2,
    }

    /// <summary>How the nickname relates to the other fields, decided when the dialog opens (v3 §6.3).</summary>
    public enum NickSource
    {
        /// <summary>A nickname of its own (Fox): translated by meaning in the <c>nick:</c> namespace.</summary>
        Explicit,

        /// <summary>The pawn is addressed by its first name: the nickname mirrors the first name.</summary>
        AliasOfFirst,

        /// <summary>The pawn is addressed by its last name: the nickname mirrors the last name.</summary>
        AliasOfLast,
    }

    public enum ProcessMode
    {
        Translate,

        /// <summary>Phase 2 (Step 6); not handled by the pipeline yet.</summary>
        Generate,
    }

    /// <summary>Where a field's result came from (v3 §5.4).</summary>
    public enum FieldSource
    {
        /// <summary>Left as it is: locked, blank, already Chinese, or nothing to mirror.</summary>
        Unchanged,
        SpecialForm,
        Alias,
        PersonalCache,
        Dictionary,
        Api,
        Failed,

        /// <summary>Needs Gemini (or, for an alias, the field it mirrors still needs Gemini).</summary>
        Pending,
    }

    public static class NameFields
    {
        public const int Count = 3;

        public static readonly NameField[] All = { NameField.First, NameField.Nick, NameField.Last };

        public static NameSpace SpaceOf(NameField field) => field == NameField.Nick ? NameSpace.Nick : NameSpace.Name;
    }

    /// <summary>What the dialog hands to the pipeline (v3 §5.4). Plain data, filled on the main thread.</summary>
    public sealed class NameRequestPayload
    {
        public NameRequestPayload(string first, string nick, string last)
        {
            Text[(int)NameField.First] = first;
            Text[(int)NameField.Nick] = nick;
            Text[(int)NameField.Last] = last;
            Array.Copy(Text, OriginalText, NameFields.Count);
        }

        public ProcessMode Mode { get; set; } = ProcessMode.Translate;

        /// <summary>Field values now (indexed by <see cref="NameField"/>).</summary>
        public string[] Text { get; } = new string[NameFields.Count];

        /// <summary>Field values when the dialog opened; defaults to <see cref="Text"/>.</summary>
        public string[] OriginalText { get; } = new string[NameFields.Count];

        public bool[] Locked { get; } = new bool[NameFields.Count];

        public NickSource NickSource { get; set; } = NickSource.Explicit;

        /// <summary>Setting "keep vanilla form of address": fill a blank alias nickname with the translation.</summary>
        public bool KeepVanillaAddress { get; set; } = true;

        public string this[NameField field] => Text[(int)field];
    }

    /// <summary>The outcome for one field.</summary>
    public sealed class FieldResult
    {
        public FieldResult(FieldSource source, string value, string sourceText,
                           NameField? aliasOf = null, GeminiErrorKind error = GeminiErrorKind.None)
        {
            Source = source;
            Value = value;
            SourceText = sourceText;
            AliasOf = aliasOf;
            Error = error;
        }

        public FieldSource Source { get; }

        /// <summary>The text to write into the field; null when the field keeps its current text.</summary>
        public string Value { get; }

        /// <summary>The trimmed original that was translated: the cache key when the player later corrects it.</summary>
        public string SourceText { get; }

        /// <summary>For a nickname that mirrors another field.</summary>
        public NameField? AliasOf { get; }

        /// <summary>Why a <see cref="FieldSource.Failed"/> field failed.</summary>
        public GeminiErrorKind Error { get; }

        public bool HasNewValue => Value != null;
    }

    /// <summary>The pipeline's answer (v3 §5.4).</summary>
    public sealed class NameResult
    {
        private readonly FieldResult[] fields;

        public NameResult(FieldResult first, FieldResult nick, FieldResult last,
                          GeminiErrorKind remoteError = GeminiErrorKind.None, int remoteStatusCode = 0, string remoteDetail = null)
        {
            fields = new[]
            {
                first ?? throw new ArgumentNullException(nameof(first)),
                nick ?? throw new ArgumentNullException(nameof(nick)),
                last ?? throw new ArgumentNullException(nameof(last)),
            };
            RemoteError = remoteError;
            RemoteStatusCode = remoteStatusCode;
            RemoteDetail = remoteDetail;
        }

        public FieldResult this[NameField field] => fields[(int)field];

        public IReadOnlyList<FieldResult> Fields => fields;

        public bool HasPending => Array.Exists(fields, f => f.Source == FieldSource.Pending);

        /// <summary>None unless the Gemini call itself failed.</summary>
        public GeminiErrorKind RemoteError { get; }

        public int RemoteStatusCode { get; }

        /// <summary>Redacted error detail from the client.</summary>
        public string RemoteDetail { get; }
    }
}
