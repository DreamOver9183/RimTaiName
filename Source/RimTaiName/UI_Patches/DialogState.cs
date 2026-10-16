using System.Runtime.CompilerServices;
using RimTaiName.Core;
using RimTaiName.Pipeline;
using Verse;

namespace RimTaiName.UI_Patches
{
    /// <summary>Per-dialog state; nameIndex 0/1/2 = first/nick/last.</summary>
    internal sealed class DialogState
    {
        public DialogState(Pawn pawn, NameDialogSession session)
        {
            Pawn = pawn;
            OriginalName = pawn.Name;
            Session = session;
        }

        public Pawn Pawn { get; }

        /// <summary>pawn.Name when the dialog opened. Accepting always assigns a new NameTriple (BuildName).</summary>
        public Name OriginalName { get; }

        public NameDialogSession Session { get; }

        /// <summary>The Gemini request of the latest translation; kept running after the dialog closes so the result still reaches the cache.</summary>
        public UiJob<NameResult> Job { get; } = new UiJob<NameResult>();

        /// <summary>Values to write on the next Layout event; null entries are left untouched.</summary>
        public FieldWrite[] PendingWrite;

        /// <summary>closeOnClickedOutside is switched off while a request is in flight.</summary>
        public bool ClickOutsideSuspended;

        public bool SavedCloseOnClickedOutside;

        /// <summary>Set once the accept was seen, so corrections are learned only once.</summary>
        public bool Accepted;
    }

    internal static class DialogStateRegistry
    {
        private static readonly ConditionalWeakTable<Dialog_NamePawn, DialogState> States =
            new ConditionalWeakTable<Dialog_NamePawn, DialogState>();

        /// <summary>The dialog whose DoWindowContents is running (UI is single-threaded).</summary>
        public static Dialog_NamePawn Drawing;

        public static DialogState Create(Dialog_NamePawn dialog, DialogState state) => States.GetValue(dialog, _ => state);

        public static DialogState Get(Dialog_NamePawn dialog) =>
            dialog != null && States.TryGetValue(dialog, out var state) ? state : null;
    }
}
