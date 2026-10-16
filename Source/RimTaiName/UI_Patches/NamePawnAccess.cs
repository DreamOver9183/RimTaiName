using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using RimTaiName.Pipeline;
using UnityEngine;
using Verse;

namespace RimTaiName.UI_Patches
{
    /// <summary>Field accessors for Dialog_NamePawn and its private NameContext, built once at startup.</summary>
    internal static class NamePawnAccess
    {
        public static readonly Type NameContextType = AccessTools.Inner(typeof(Dialog_NamePawn), "NameContext");

        public static readonly AccessTools.FieldRef<Dialog_NamePawn, IList> Names =
            AccessTools.FieldRefAccess<Dialog_NamePawn, IList>("names");

        public static readonly AccessTools.FieldRef<Dialog_NamePawn, Vector2> Size =
            AccessTools.FieldRefAccess<Dialog_NamePawn, Vector2>("size");

        public static readonly AccessTools.FieldRef<object, string> Current =
            AccessTools.FieldRefAccess<string>(NameContextType, "current");

        public static readonly AccessTools.FieldRef<object, bool> Editable =
            AccessTools.FieldRefAccess<bool>(NameContextType, "editable");

        public static readonly AccessTools.FieldRef<object, int> NameIndex =
            AccessTools.FieldRefAccess<int>(NameContextType, "nameIndex");

        /// <summary>The newborn surname row's "Suggested..." list (parents' surnames); null on other rows.</summary>
        public static readonly AccessTools.FieldRef<object, List<string>> SuggestedNames =
            AccessTools.FieldRefAccess<List<string>>(NameContextType, "suggestedNames");

        public static bool IsSupported(Pawn pawn) => pawn?.RaceProps?.Humanlike == true && pawn.Name is NameTriple;

        /// <summary>
        /// The first/nick/last rows, indexed by <see cref="NameField"/>; null for a row the dialog does not show
        /// (and the title row, nameIndex -1, is never included).
        /// </summary>
        public static object[] Rows(Dialog_NamePawn dialog)
        {
            var rows = new object[NameFields.Count];
            foreach (object context in Names(dialog))
            {
                int index = NameIndex(context);
                if (index >= 0 && index < NameFields.Count)
                    rows[index] = context;
            }
            return rows;
        }

        /// <summary>Current text of each row (null when absent) and whether the player may edit it.</summary>
        public static string[] ReadFields(object[] rows, out bool[] editable)
        {
            var values = new string[NameFields.Count];
            editable = new bool[NameFields.Count];
            for (int i = 0; i < NameFields.Count; i++)
            {
                if (rows[i] == null)
                    continue;
                values[i] = Current(rows[i]);
                editable[i] = Editable(rows[i]);
            }
            return values;
        }
    }
}
