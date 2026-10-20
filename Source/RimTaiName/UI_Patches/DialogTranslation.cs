using System;
using System.Collections.Generic;
using System.Linq;
using RimTaiName.Core;
using RimTaiName.Llm;
using RimTaiName.Pipeline;
using RimWorld;
using Verse;

namespace RimTaiName.UI_Patches
{
    /// <summary>
    /// The naming dialog's translate / generate / restore / learn flow (v3 §5.3, §7.4). Main thread only; the
    /// Gemini request runs in <see cref="DialogState.Job"/> and its result is picked up on a later Layout event.
    /// </summary>
    internal static class DialogTranslation
    {
        /// <summary>Only varies the prompt hints; not the game's Verse.Rand, so the colony's random sequence is untouched.</summary>
        private static readonly Random Seeds = new Random();

        /// <summary>Toolbar button: write what the pipeline resolves locally, send the rest to Gemini in one request.</summary>
        public static void Translate(Dialog_NamePawn dialog, DialogState state) => Run(dialog, state, ProcessMode.Translate);

        /// <summary>Toolbar button: new Chinese names for the unlocked fields from the pawn's background (phase 2).</summary>
        public static void Generate(Dialog_NamePawn dialog, DialogState state) => Run(dialog, state, ProcessMode.Generate);

        private static void Run(Dialog_NamePawn dialog, DialogState state, ProcessMode mode)
        {
            if (state.Job.Running)
                return;
            string[] current = NamePawnAccess.ReadFields(NamePawnAccess.Rows(dialog), out bool[] editable);
            NameDialogSession session = state.Session;
            NameRequestPayload payload = session.CreatePayload(current, editable, RimTaiNameMod.Settings.keepVanillaAddress, mode);
            NameProcessorService processor = RimTaiNameMod.Processor;
            NameResult local;
            if (mode == ProcessMode.Generate)
            {
                payload.Context = PawnContextReader.Read(state.Pawn);
                payload.Seed = Seeds.Next();
                local = processor.PlanGeneration(payload);
            }
            else
            {
                local = processor.ResolveLocal(payload);
            }
            state.LastMode = mode;

            List<NameField> wanted = Fields(local, f => f.Source == FieldSource.Pending && !f.AliasOf.HasValue);
            IDisposable lease = null;
            string problem = null;
            if (wanted.Count > 0)
            {
                if (!ApiKeyStore.HasKey)
                    problem = mode == ProcessMode.Generate
                        ? "RimTaiName_Dialog_GenerateNoKey".Translate()
                        : "RimTaiName_Dialog_NoKey".Translate(FieldList(wanted));
                else
                    lease = RimTaiNameMod.TryEnterGate(out problem);
            }

            bool queued = Queue(state, session.BeginTranslation(local, current, awaitRemote: lease != null));
            if (lease != null)
            {
                SuspendClickOutside(dialog, state);
                GeminiNameTranslator translator = RimTaiNameMod.Translator;
                state.Job.Start(token => processor.ResolveRemoteAsync(payload, local, translator, token), lease,
                                e => Failed(local, e));
                return;
            }
            Log(mode, local);
            if (problem != null)
                Reject(problem);
            else if (!queued)
                Reject((mode == ProcessMode.Generate ? "RimTaiName_Dialog_NothingToGenerate" : "RimTaiName_Dialog_NothingToTranslate").Translate());
        }

        /// <summary>Toolbar button: unlocked fields back to their values at opening; a pending result is no longer applied.</summary>
        public static void Restore(DialogState state) => Queue(state, state.Session.Restore());

        /// <summary>Every Layout event: track edits, apply a finished request, then write what is queued.</summary>
        public static void OnLayout(Dialog_NamePawn dialog, DialogState state)
        {
            object[] rows = NamePawnAccess.Rows(dialog);
            string[] current = NamePawnAccess.ReadFields(rows, out _);
            state.Session.Observe(current);
            if (state.ClickOutsideSuspended && !state.Job.Running)
            {
                dialog.closeOnClickedOutside = state.SavedCloseOnClickedOutside;
                state.ClickOutsideSuspended = false;
            }
            if (state.Job.TakeNewResult())
            {
                NameResult result = state.Job.Result;
                bool applies = state.Session.AwaitingRemote; // false after a restore: the result only feeds the cache
                Queue(state, state.Session.CompleteTranslation(result, current));
                Log(state.LastMode, result);
                if (applies)
                    ReportFailures(state.LastMode, result);
                SaveCache();
            }
            if (state.PendingWrite != null)
                ApplyPending(rows, state);
        }

        /// <summary>After the player accepted: remember every translation they changed to other Chinese text.</summary>
        public static void OnAccepted(Dialog_NamePawn dialog, DialogState state)
        {
            if (!(state.Pawn.Name is NameTriple name))
                return;
            object[] rows = NamePawnAccess.Rows(dialog);
            string[] final = { name.First, name.NickSet ? name.Nick : string.Empty, name.Last };
            IList<NameCorrection> corrections = state.Session.Corrections(final, field =>
                rows[(int)field] == null ? null : NamePawnAccess.SuggestedNames(rows[(int)field]));
            if (corrections.Count == 0)
                return;
            PersonalNameCache cache = RimTaiNameMod.Cache;
            foreach (NameCorrection correction in corrections)
                cache.Put(correction.Space, correction.Key, correction.Value, CacheOrigin.User);
            ModLog.Message($"learned {corrections.Count} correction(s) from the naming dialog");
            SaveCache();
        }

        /// <summary>
        /// Main thread. Also called when a dialog opens, for results that arrived after the previous dialog closed.
        /// </summary>
        public static void SaveCache()
        {
            PersonalNameCache cache = RimTaiNameMod.Cache;
            if (!cache.SaveIfDirty())
                ModLog.Warning("personal cache could not be saved: " + cache.LastError);
        }

        private static void ApplyPending(object[] rows, DialogState state)
        {
            FieldWrite[] writes = state.PendingWrite;
            state.PendingWrite = null;
            bool unfocused = false;
            for (int i = 0; i < NameFields.Count; i++)
            {
                FieldWrite write = writes[i];
                if (write == null || rows[i] == null || !NamePawnAccess.Editable(rows[i]) || state.Session.Locked[i])
                    continue;
                if (!unfocused)
                {
                    // A focused text box keeps its own edit buffer and would overwrite the new value.
                    UI.UnfocusCurrentTextField();
                    unfocused = true;
                }
                NamePawnAccess.Current(rows[i]) = write.Value;
                state.Session.MarkWritten((NameField)i, write);
            }
        }

        /// <summary>Merges <paramref name="writes"/> into the pending writes; false when there was nothing to write.</summary>
        private static bool Queue(DialogState state, FieldWrite[] writes)
        {
            bool any = false;
            for (int i = 0; i < NameFields.Count; i++)
            {
                if (writes[i] == null)
                    continue;
                if (state.PendingWrite == null)
                    state.PendingWrite = new FieldWrite[NameFields.Count];
                state.PendingWrite[i] = writes[i];
                any = true;
            }
            return any;
        }

        private static void SuspendClickOutside(Dialog_NamePawn dialog, DialogState state)
        {
            if (state.ClickOutsideSuspended)
                return;
            state.SavedCloseOnClickedOutside = dialog.closeOnClickedOutside;
            dialog.closeOnClickedOutside = false;
            state.ClickOutsideSuspended = true;
        }

        private static void ReportFailures(ProcessMode mode, NameResult result)
        {
            List<NameField> failed = Fields(result, f => f.Source == FieldSource.Failed);
            if (failed.Count == 0)
                return;
            bool generate = mode == ProcessMode.Generate;
            if (result.RemoteError != GeminiErrorKind.None)
                Reject((generate ? "RimTaiName_Dialog_GenerateFailed" : "RimTaiName_Dialog_RemoteFailed").Translate(FieldList(failed),
                    GeminiMessages.Describe(result.RemoteError, result.RemoteStatusCode, result.RemoteDetail)));
            else
                Reject((generate ? "RimTaiName_Dialog_GenerateRejected" : "RimTaiName_Dialog_Rejected").Translate(FieldList(failed)));
        }

        /// <summary>Only for an exception the pipeline itself did not turn into a result.</summary>
        private static NameResult Failed(NameResult local, Exception e)
        {
            FieldResult Fail(FieldResult f) => f.Source == FieldSource.Pending
                ? new FieldResult(FieldSource.Failed, null, f.SourceText, f.AliasOf, GeminiErrorKind.InvalidResponse)
                : f;
            return new NameResult(Fail(local[NameField.First]), Fail(local[NameField.Nick]), Fail(local[NameField.Last]),
                                  GeminiErrorKind.InvalidResponse, 0, e.GetType().Name);
        }

        private static void Log(ProcessMode mode, NameResult result)
        {
            if (result == null)
                return;
            ModLog.Message($"{(mode == ProcessMode.Generate ? "generate" : "translate")}: {result[NameField.First].Source}/{result[NameField.Nick].Source}/{result[NameField.Last].Source}" +
                           (result.RemoteStatusCode != 0 || result.RemoteError != GeminiErrorKind.None
                               ? $", remote {result.RemoteError} (HTTP {result.RemoteStatusCode})" +
                                 (string.IsNullOrEmpty(result.RemoteDetail) ? string.Empty : " " + result.RemoteDetail)
                               : string.Empty));
        }

        private static List<NameField> Fields(NameResult result, Func<FieldResult, bool> match) =>
            NameFields.All.Where(f => match(result[f])).ToList();

        private static string FieldList(IEnumerable<NameField> fields) =>
            fields.Select(f => ("RimTaiName_Field_" + f).Translate().RawText).ToCommaList(useAnd: true);

        private static void Reject(string text) => Messages.Message(text, MessageTypeDefOf.RejectInput, historical: false);
    }
}
