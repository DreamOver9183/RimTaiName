using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimTaiName.Core;
using RimTaiName.Pipeline;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTaiName.UI_Patches
{
    /// <summary>Unlocks first/last names for humanlike pawns, widens the window and records the names as opened.</summary>
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
            try
            {
                var name = (NameTriple)pawn.Name;
                string[] opened = NamePawnAccess.ReadFields(NamePawnAccess.Rows(__instance), out _);
                // The nickname box is blank when no nickname is set; NameTriple.Nick then falls back to first or last.
                NickSource nickSource = NickAliasResolver.Determine(opened[0], opened[1], opened[2], name.NickSet, name.Nick);
                DialogStateRegistry.Create(__instance, new DialogState(pawn, new NameDialogSession(opened[0], opened[1], opened[2], nickSource)));
                NamePawnAccess.Size(__instance).x += LockColumnWidth;
                DialogTranslation.SaveCache();
            }
            catch (Exception e)
            {
                ModLog.ErrorOnce($"naming dialog setup failed: {e}", 0x52544e03);
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
                if (state == null || index < 0 || index >= NameFields.Count || !NamePawnAccess.Editable(__instance))
                    return;
                // Right-justified: a newborn's last-name row draws no button when no parent has a surname.
                Rect cell = divider.NewCol(24f, HorizontalJustification.Right);
                Widgets.Checkbox(new Vector2(cell.x, cell.y + (cell.height - 24f) / 2f), ref state.Session.Locked[index]);
                TooltipHandler.TipRegion(cell, "RimTaiName_LockTip".Translate());
            }
            catch (Exception e)
            {
                ModLog.ErrorOnce($"lock column failed: {e}", 0x52544e01);
            }
        }
    }

    /// <summary>Applies results and pending writes, injects the toolbar row and notices the accept.</summary>
    [HarmonyPatch(typeof(Dialog_NamePawn), nameof(Dialog_NamePawn.DoWindowContents))]
    internal static class Patch_Dialog_NamePawn_DoWindowContents
    {
        private static void Prefix(Dialog_NamePawn __instance)
        {
            DialogStateRegistry.Drawing = __instance;
            var state = DialogStateRegistry.Get(__instance);
            if (state == null || Event.current.type != EventType.Layout)
                return;
            try
            {
                // Writes to NameContext.current happen only here, before the text boxes are laid out.
                DialogTranslation.OnLayout(__instance, state);
            }
            catch (Exception e)
            {
                ModLog.ErrorOnce($"naming dialog update failed: {e}", 0x52544e04);
            }
        }

        /// <summary>Accepting builds a new NameTriple, assigns it and removes the window inside DoWindowContents.</summary>
        private static void Postfix(Dialog_NamePawn __instance)
        {
            var state = DialogStateRegistry.Get(__instance);
            if (state == null || state.Accepted || ReferenceEquals(state.Pawn.Name, state.OriginalName) || __instance.IsOpen)
                return;
            state.Accepted = true;
            try
            {
                DialogTranslation.OnAccepted(__instance, state);
            }
            catch (Exception e)
            {
                ModLog.ErrorOnce($"learning from the naming dialog failed: {e}", 0x52544e05);
            }
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
    }

    internal static class ToolbarDrawer
    {
        private const float RowHeight = 35f;

        private const float ColumnGap = 17f;

        public static void Draw(Dialog_NamePawn dialog, ref RectAggregator aggregator)
        {
            try
            {
                var state = DialogStateRegistry.Get(dialog);
                if (state == null)
                    return;
                RectDivider row = aggregator.NewRow(RowHeight);
                float width = Mathf.Floor((row.Rect.width - 2 * ColumnGap) / 3f);
                bool running = state.Job.Running;
                bool generating = running && state.LastMode == ProcessMode.Generate;
                Rect translate = row.NewCol(width);
                if (Widgets.ButtonText(translate, (running && !generating ? "RimTaiName_Translating" : "RimTaiName_Translate").Translate(), active: !running))
                    DialogTranslation.Translate(dialog, state);
                TooltipHandler.TipRegion(translate, "RimTaiName_TranslateTip".Translate());
                Rect generate = row.NewCol(width);
                if (Widgets.ButtonText(generate, (generating ? "RimTaiName_Translating" : "RimTaiName_Generate").Translate(), active: !running))
                    DialogTranslation.Generate(dialog, state);
                TooltipHandler.TipRegion(generate, "RimTaiName_GenerateTip".Translate());
                Rect restore = row.NewCol(width);
                if (Widgets.ButtonText(restore, "RimTaiName_Restore".Translate()))
                    DialogTranslation.Restore(state);
                TooltipHandler.TipRegion(restore, "RimTaiName_RestoreTip".Translate());
            }
            catch (Exception e)
            {
                ModLog.ErrorOnce($"toolbar failed: {e}", 0x52544e02);
            }
        }
    }
}
