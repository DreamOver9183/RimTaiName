namespace RimTaiName.Pipeline
{
    /// <summary>
    /// Dictionary and cache namespace. First and last names share <see cref="Name"/> (transliteration);
    /// nicknames use <see cref="Nick"/> (meaning first) and never fall back to <see cref="Name"/>.
    /// </summary>
    public enum NameSpace
    {
        Name,
        Nick,
    }

    public static class NameSpaces
    {
        /// <summary>The tag used in Dictionary/BaseNames_zhTW.tsv and the cache file.</summary>
        public static string Tag(NameSpace ns) => ns == NameSpace.Nick ? "nick" : "name";

        public static bool TryParse(string tag, out NameSpace ns)
        {
            switch (tag)
            {
                case "name":
                    ns = NameSpace.Name;
                    return true;
                case "nick":
                    ns = NameSpace.Nick;
                    return true;
                default:
                    ns = NameSpace.Name;
                    return false;
            }
        }
    }
}
