using System.Runtime.CompilerServices;
using Verse;

namespace RimTaiName.UI_Patches
{
    /// <summary>Per-dialog state; nameIndex 0/1/2 = first/nick/last.</summary>
    internal sealed class DialogState
    {
        public readonly bool[] Locked = new bool[3];

        public readonly string[] Snapshot = new string[3];

        /// <summary>Values to write on the next Layout event; null entries are left untouched.</summary>
        public string[] PendingWrite;

        public bool PendingUnfocus;
    }

    internal static class DialogStateRegistry
    {
        private static readonly ConditionalWeakTable<Dialog_NamePawn, DialogState> States =
            new ConditionalWeakTable<Dialog_NamePawn, DialogState>();

        /// <summary>The dialog whose DoWindowContents is running (UI is single-threaded).</summary>
        public static Dialog_NamePawn Drawing;

        public static DialogState Create(Dialog_NamePawn dialog) => States.GetValue(dialog, _ => new DialogState());

        public static DialogState Get(Dialog_NamePawn dialog) =>
            dialog != null && States.TryGetValue(dialog, out var state) ? state : null;
    }
}
