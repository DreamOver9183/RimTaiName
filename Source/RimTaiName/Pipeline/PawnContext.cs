namespace RimTaiName.Pipeline
{
    /// <summary>
    /// What phase 2 (Generate) tells Gemini about the pawn (v3 §5.4, §7.4); nothing else about the pawn is sent.
    /// Plain data read on the main thread; every field may be null or empty.
    /// </summary>
    public sealed class PawnContext
    {
        /// <summary>"male", "female" or "none".</summary>
        public string Gender { get; set; }

        /// <summary>Childhood backstory title, in the game's language.</summary>
        public string Childhood { get; set; }

        /// <summary>Adulthood backstory title; children have none.</summary>
        public string Adulthood { get; set; }

        /// <summary>Culture def name of the pawn's ideoligion (e.g. Astropolitan).</summary>
        public string Culture { get; set; }

        /// <summary>Xenotype def name (e.g. Baseliner, Pigskin) or the name of a custom xenotype.</summary>
        public string Xenotype { get; set; }

        /// <summary>"newborn", "baby", "child" or "adult".</summary>
        public string LifeStage { get; set; }
    }
}
