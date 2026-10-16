using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimTaiName.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTaiName.UI_Patches
{
    /// <summary>Unlocks first/last names for humanlike pawns, widens the window and snapshots the names.</summary>
    [HarmonyPatch(typeof(Dialog_NamePawn), MethodType.Constructor,
        new[] { typeof(Pawn), typeof(NameFilter), typeof(NameFilter), typeof(Dictionary<NameFilter, List<string>>),
                typeof(string), typeof(string), typeof(string), typeof(string) })]
    internal static class Patch_Dialog_NamePawn_Ctor
    {
        /// <summary>17 px column gap + 24 px checkbox.</summary>
        private const float LockColumnWidth = 41f;

        private static void Prefix(Pawn pawn, NameFilter visibleNames, ref NameFilter editableNames)
        {
            if (NamePawnAccess.IsSupported(pawn))
                editableNames |= visibleNames & (NameFilter.First | NameFilter.Last);
        }

        private static void Postfix(Dialog_NamePawn __instance, Pawn pawn)
        {
            if (!NamePawnAccess.IsSupported(pawn))
                return;
            var state = DialogStateRegistry.Create(__instance);
            NamePawnAccess.Size(__instance).x += LockColumnWidth;
            foreach (object context in NamePawnAccess.Names(__instance))
            {
                int index = NamePawnAccess.NameIndex(context);
                if (index >= 0 && index <= 2)
                    state.Snapshot[index] = NamePawnAccess.Current(context);
            }
        }
    }

    /// <summary>Draws a lock checkbox at the right end of each first/nick/last row.</summary>
    [HarmonyPatch]
    internal static class Patch_NameContext_MakeRow
    {
        private static MethodBase TargetMethod() => AccessTools.Method(NamePawnAccess.NameContextType, "MakeRow");

        private static void Postfix(object __instance, ref RectDivider divider)
        {
            try
            {
                var state = DialogStateRegistry.Get(DialogStateRegistry.Drawing);
                int index = NamePawnAccess.NameIndex(__instance);
                if (state == null || index < 0 || index > 2 || !NamePawnAccess.Editable(__instance))
                    return;
                // Right-justified: a newborn's last-name row draws no button when no parent has a surname.
                Rect cell = divider.NewCol(24f, HorizontalJustification.Right);
                Widgets.Checkbox(new Vector2(cell.x, cell.y + (cell.height - 24f) / 2f), ref state.Locked[index]);
                TooltipHandler.TipRegion(cell, "RimTaiName_LockTip".Translate());
            }
            catch (Exception e)
            {
                ModLog.ErrorOnce($"lock column failed: {e}", 0x52544e01);
            }
        }
    }

    /// <summary>Applies pending writes, tracks the drawing dialog and injects the toolbar row.</summary>
    [HarmonyPatch(typeof(Dialog_NamePawn), nameof(Dialog_NamePawn.DoWindowContents))]
    internal static class Patch_Dialog_NamePawn_DoWindowContents
    {
        private static void Prefix(Dialog_NamePawn __instance)
        {
            DialogStateRegistry.Drawing = __instance;
            var state = DialogStateRegistry.Get(__instance);
            if (state?.PendingWrite != null && Event.current.type == EventType.Layout)
                ApplyPending(__instance, state);
        }

        private static Exception Finalizer(Exception __exception)
        {
            DialogStateRegistry.Drawing = null;
            return __exception;
        }

        /// <summary>Inserts ToolbarDrawer.Draw(this, ref rectAggregator) before rectAggregator.NewRow(17.5f).</summary>
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = instructions.ToList();
            var matcher = new CodeMatcher(original).MatchStartForward(
                new CodeMatch(i => i.opcode == OpCodes.Ldloca_S || i.opcode == OpCodes.Ldloca),
                new CodeMatch(OpCodes.Ldc_R4, 17.5f));
            if (matcher.IsInvalid)
            {
                ModLog.Warning("toolbar injection point not found; toolbar disabled");
                return original;
            }
            var loadAggregator = matcher.Instruction;
            matcher.Insert(
                new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(loadAggregator),
                new CodeInstruction(loadAggregator.opcode, loadAggregator.operand),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ToolbarDrawer), nameof(ToolbarDrawer.Draw))));
            ModLog.Message("toolbar row injected into Dialog_NamePawn.DoWindowContents");
            return matcher.InstructionEnumeration();
        }

        private static void ApplyPending(Dialog_NamePawn dialog, DialogState state)
        {
            if (state.PendingUnfocus)
                UI.UnfocusCurrentTextField();
            foreach (object context in NamePawnAccess.Names(dialog))
            {
                int index = NamePawnAccess.NameIndex(context);
                if (index < 0 || index > 2 || !NamePawnAccess.Editable(context) || state.Locked[index])
                    continue;
                if (state.PendingWrite[index] != null)
                    NamePawnAccess.Current(context) = state.PendingWrite[index];
            }
            state.PendingWrite = null;
        }
    }

    internal static class ToolbarDrawer
    {
        private const float RowHeight = 35f;

        private const float ColumnGap = 17f;

        /// <summary>Step 0 spike buttons; the real toolbar replaces them with Translate / Restore.</summary>
        private static readonly string[] SpikeValues = { "測試名", "測試暱稱", "測試姓" };

        public static void Draw(Dialog_NamePawn dialog, ref RectAggregator aggregator)
        {
            try
            {
                var state = DialogStateRegistry.Get(dialog);
                if (state == null)
                    return;
                RectDivider row = aggregator.NewRow(RowHeight);
                float width = Mathf.Floor((row.Rect.width - 2 * ColumnGap) / 3f);
                if (Widgets.ButtonText(row.NewCol(width), "RimTaiName_Spike_WriteUnfocus".Translate()))
                    Queue(state, SpikeValues, unfocus: true);
                if (Widgets.ButtonText(row.NewCol(width), "RimTaiName_Spike_WriteKeepFocus".Translate()))
                    Queue(state, SpikeValues, unfocus: false);
                if (Widgets.ButtonText(row.NewCol(width), "RimTaiName_Restore".Translate()))
                    Queue(state, state.Snapshot, unfocus: true);
            }
            catch (Exception e)
            {
                ModLog.ErrorOnce($"toolbar failed: {e}", 0x52544e02);
            }
        }

        private static void Queue(DialogState state, string[] values, bool unfocus)
        {
            state.PendingWrite = (string[])values.Clone();
            state.PendingUnfocus = unfocus;
        }
    }
}
