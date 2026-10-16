using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimTaiName.Pipeline;
using Xunit;

namespace RimTaiName.Tests.Pipeline
{
    public sealed class BuiltInDictionaryTests : IDisposable
    {
        private static readonly string ShippedPath = Path.Combine(AppContext.BaseDirectory, "Data", "BaseNames_zhTW.tsv");

        private readonly string dir = Path.Combine(Path.GetTempPath(), "rimtainame_dict_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }

        private BuiltInDictionary FromText(string content, Encoding encoding = null)
        {
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "dict.tsv");
            File.WriteAllText(path, content, encoding ?? new UTF8Encoding(false));
            return new BuiltInDictionary(path);
        }

        [Fact]
        public void ShippedDictionaryLoadsCompletely()
        {
            var dictionary = new BuiltInDictionary(ShippedPath);
            Assert.Null(dictionary.LoadError);
            Assert.Equal(0, dictionary.SkippedLines);
            Assert.Equal(24596, dictionary.Count);
        }

        [Fact]
        public void ShippedDictionaryAgreesWithTheFixture()
        {
            var dictionary = new BuiltInDictionary(ShippedPath);
            var mismatches = RuleFixture.All
                .Where(r => !r.IsEdgeCase && r.Zh.Length > 0)
                .Where(r => !dictionary.TryGet(r.Ns, r.Key, out string value) || value != r.Zh)
                .Select(r => r.ToString())
                .ToList();
            Assert.True(mismatches.Count == 0, $"{mismatches.Count} mismatches: " + string.Join(", ", mismatches.Take(10)));
        }

        [Fact]
        public void NamespacesAreSeparateAndNicknamesDoNotFallBack()
        {
            // A word translated differently as a name and as a nickname (Fox → 福克斯 / 狐狸 in the design).
            var dictionary = new BuiltInDictionary(ShippedPath);
            RuleFixture both = RuleFixture.All
                .Where(r => !r.IsEdgeCase && r.Ns == NameSpace.Nick)
                .First(nick => RuleFixture.All.Any(n => !n.IsEdgeCase && n.Ns == NameSpace.Name && n.Key == nick.Key && n.Zh != nick.Zh));
            Assert.True(dictionary.TryGet(NameSpace.Name, both.Key, out string asName));
            Assert.True(dictionary.TryGet(NameSpace.Nick, both.Key, out string asNick));
            Assert.NotEqual(asName, asNick);

            RuleFixture nameOnly = RuleFixture.All
                .Where(r => !r.IsEdgeCase && r.Ns == NameSpace.Name)
                .First(n => !RuleFixture.All.Any(k => !k.IsEdgeCase && k.Ns == NameSpace.Nick && k.Key == n.Key));
            Assert.False(dictionary.TryGet(NameSpace.Nick, nameOnly.Key, out _));
        }

        [Fact]
        public void LoadsLazilyOnFirstLookup()
        {
            BuiltInDictionary dictionary = FromText(BuiltInDictionary.Header + "\nname\tanna\t安娜\n");
            Assert.False(dictionary.IsLoaded);
            Assert.True(dictionary.TryGet(NameSpace.Name, "anna", out string value));
            Assert.True(dictionary.IsLoaded);
            Assert.Equal("安娜", value);
        }

        [Fact]
        public void SkipsMalformedAndDuplicateLinesKeepingTheFirst()
        {
            BuiltInDictionary dictionary = FromText(
                BuiltInDictionary.Header + "\r\n" +
                "name\tanna\t安娜\r\n" +
                "name\tanna\t安娜二\r\n" +       // duplicate
                "other\tx\ty\r\n" +               // unknown namespace
                "nick\tdoc\r\n" +                 // missing column
                "nick\t\t老醫\r\n" +              // empty key
                "nick\tfox\t\r\n" +               // empty value
                "\r\n" +
                "nick\tfox\t狐狸\r\n");
            Assert.Equal(2, dictionary.Count);
            Assert.Equal(5, dictionary.SkippedLines);
            Assert.True(dictionary.TryGet(NameSpace.Name, "anna", out string anna));
            Assert.Equal("安娜", anna);
            Assert.True(dictionary.TryGet(NameSpace.Nick, "fox", out string fox));
            Assert.Equal("狐狸", fox);
        }

        [Fact]
        public void BlankKeysAndValuesDoNotShadowValidEntries()
        {
            BuiltInDictionary dictionary = FromText(
                BuiltInDictionary.Header + "\n" +
                "name\t   \t安娜\n" +          // blank key
                "name\tanna\t   \n" +          // blank value must not hide the next line
                "name\tanna\t 安娜 \n" +        // value is trimmed
                " \t \n" +                     // whitespace-only line counts as empty
                "nick\tfox\t狐狸\n");
            Assert.Equal(2, dictionary.Count);
            Assert.Equal(2, dictionary.SkippedLines);
            Assert.True(dictionary.TryGet(NameSpace.Name, "anna", out string anna));
            Assert.Equal("安娜", anna);
        }

        [Theory]
        [InlineData("")]
        [InlineData("bad\0path.tsv")]
        public void InvalidPathGivesEmptyDictionaryInsteadOfThrowing(string path)
        {
            var dictionary = new BuiltInDictionary(path);
            Assert.False(dictionary.TryGet(NameSpace.Name, "anna", out _));
            Assert.Equal(0, dictionary.Count);
            Assert.Equal(0, dictionary.SkippedLines);
            Assert.NotNull(dictionary.LoadError);
        }

        [Fact]
        public void HandlesBomAndHandEditedKeys()
        {
            BuiltInDictionary dictionary = FromText(BuiltInDictionary.Header + "\nname\t  André \t安德烈\n", new UTF8Encoding(true));
            Assert.Equal(0, dictionary.SkippedLines);
            Assert.True(dictionary.TryGet(NameSpace.Name, NameKey.Normalize("ANDRÉ"), out string value));
            Assert.Equal("安德烈", value);
        }

        [Theory]
        [InlineData(false, "FileNotFoundException")]
        [InlineData(true, "DirectoryNotFoundException")]
        public void MissingFileGivesEmptyDictionaryAndError(bool missingFolder, string expectedError)
        {
            if (!missingFolder)
                Directory.CreateDirectory(dir);
            var dictionary = new BuiltInDictionary(Path.Combine(missingFolder ? Path.Combine(dir, "nope") : dir, "missing.tsv"));
            Assert.False(dictionary.TryGet(NameSpace.Name, "anna", out _));
            Assert.Equal(0, dictionary.Count);
            Assert.StartsWith(expectedError, dictionary.LoadError);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void EmptyKeysNeverMatch(string key)
        {
            var dictionary = new BuiltInDictionary(ShippedPath);
            Assert.False(dictionary.TryGet(NameSpace.Name, key, out string value));
            Assert.Null(value);
        }

        [Fact]
        public void ConcurrentFirstLookupsLoadOnce()
        {
            BuiltInDictionary dictionary = FromText(BuiltInDictionary.Header + "\nname\tanna\t安娜\n");
            bool[] found = new bool[64];
            Parallel.For(0, found.Length, i => found[i] = dictionary.TryGet(NameSpace.Name, "anna", out _));
            Assert.All(found, Assert.True);
            Assert.Equal(1, dictionary.Count);
        }
    }
}
