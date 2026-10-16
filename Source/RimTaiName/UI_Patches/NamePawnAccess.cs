using System;
using System.Collections;
using HarmonyLib;
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

        public static bool IsSupported(Pawn pawn) => pawn?.RaceProps?.Humanlike == true && pawn.Name is NameTriple;
    }
}
