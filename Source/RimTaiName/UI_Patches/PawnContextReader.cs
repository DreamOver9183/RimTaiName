using RimTaiName.Pipeline;
using RimWorld;
using Verse;

namespace RimTaiName.UI_Patches
{
    /// <summary>
    /// Main thread: copies the few facts phase 2 sends to Gemini (v3 §7.4) out of a pawn. Every lookup tolerates
    /// missing data: no ideoligion, no gene tracker, children without an adulthood backstory.
    /// </summary>
    internal static class PawnContextReader
    {
        public static PawnContext Read(Pawn pawn)
        {
            var context = new PawnContext();
            if (pawn == null)
                return context;
            context.Gender = pawn.gender == Gender.Male ? "male" : pawn.gender == Gender.Female ? "female" : "none";
            context.Childhood = pawn.story?.Childhood?.TitleCapFor(pawn.gender);
            context.Adulthood = pawn.story?.Adulthood?.TitleCapFor(pawn.gender);
            context.Culture = pawn.Ideo?.culture?.defName;
            Pawn_GeneTracker genes = pawn.genes;
            if (genes != null)
                context.Xenotype = genes.UniqueXenotype ? genes.xenotypeName : genes.Xenotype?.defName;
            context.LifeStage = LifeStage(pawn.DevelopmentalStage);
            return context;
        }

        private static string LifeStage(DevelopmentalStage stage)
        {
            if ((stage & DevelopmentalStage.Adult) != 0)
                return "adult";
            if ((stage & DevelopmentalStage.Child) != 0)
                return "child";
            if ((stage & DevelopmentalStage.Baby) != 0)
                return "baby";
            return (stage & DevelopmentalStage.Newborn) != 0 ? "newborn" : null;
        }
    }
}
