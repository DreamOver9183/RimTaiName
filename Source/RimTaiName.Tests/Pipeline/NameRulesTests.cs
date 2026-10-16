using System.Collections.Generic;
using System.Linq;
using RimTaiName.Pipeline;
using Xunit;

namespace RimTaiName.Tests.Pipeline
{
    public class NameRulesTests
    {
        private static void AssertNoMismatches(List<string> mismatches, string what)
        {
            Assert.True(mismatches.Count == 0,
                $"{mismatches.Count} {what} mismatches, first ones:\n" + string.Join("\n", mismatches.Take(20)));
        }

        [Fact]
        public void FixtureCoversTheWholeDictionary()
        {
            Assert.Equal(24596, RuleFixture.All.Count(r => !r.IsEdgeCase));
            Assert.True(RuleFixture.All.Count(r => r.IsEdgeCase) >= 20);
        }

        [Fact]
        public void NameKeyMatchesPython()
        {
            var mismatches = RuleFixture.All
                .Where(r => NameKey.Normalize(r.Text) != r.Key)
                .Select(r => $"{r}: C# '{NameKey.Normalize(r.Text)}' vs Python '{r.Key}'")
                .ToList();
            AssertNoMismatches(mismatches, "NameKey");
        }

        [Fact]
        public void SpecialFormMatchesPython()
        {
            var mismatches = RuleFixture.All
                .Where(r => (SpecialFormRule.Apply(r.Text, r.Ns) ?? "") != r.Special)
                .Select(r => $"{r}: C# '{SpecialFormRule.Apply(r.Text, r.Ns)}' vs Python '{r.Special}'")
                .ToList();
            AssertNoMismatches(mismatches, "special form");
        }

        [Fact]
        public void CheckMatchesPython()
        {
            var mismatches = RuleFixture.All
                .Where(r => (NameSanitizer.Check(r.Text, r.Zh, r.Ns) == NameCheck.None) != r.Valid)
                .Select(r => $"{r}: C# {NameSanitizer.Check(r.Text, r.Zh, r.Ns)} vs Python valid={r.Valid}")
                .ToList();
            AssertNoMismatches(mismatches, "check");
        }

        [Fact]
        public void EveryDictionaryTranslationSurvivesSanitizing()
        {
            // Otherwise the game would silently refuse a dictionary hit.
            var mismatches = RuleFixture.All
                .Where(r => !r.IsEdgeCase && r.Zh.Length > 0 && NameSanitizer.Sanitize(r.Text, r.Zh, r.Ns) != r.Zh)
                .Select(r => $"{r}: sanitized to '{NameSanitizer.Sanitize(r.Text, r.Zh, r.Ns)}'")
                .ToList();
            AssertNoMismatches(mismatches, "sanitize");
        }

        [Fact]
        public void ValidEdgeCasesSurviveSanitizingAndInvalidOnesAreRejectedOrCleaned()
        {
            foreach (RuleFixture r in RuleFixture.All.Where(r => r.IsEdgeCase))
            {
                string sanitized = NameSanitizer.Sanitize(r.Text, r.Zh, r.Ns);
                if (r.Valid)
                    Assert.True(sanitized == r.Zh, $"{r} sanitized to '{sanitized}'");
                else
                    Assert.True(sanitized == null || NameSanitizer.Check(r.Text, sanitized, r.Ns) == NameCheck.None, $"{r} sanitized to '{sanitized}'");
            }
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("  André ", "andré")]
        [InlineData("André", "andré")] // decomposed input is composed (NFC)
        [InlineData("O'Brien", "o'brien")]
        [InlineData("Van Dyck", "van dyck")]
        [InlineData("\uD800abc", "\ud800abc")] // a lone surrogate must not throw
        public void NameKeyNormalizes(string text, string expected)
        {
            Assert.Equal(expected, NameKey.Normalize(text));
        }

        [Theory]
        [InlineData("MJ", NameSpace.Name, "MJ")]
        [InlineData("  MJ  ", NameSpace.Nick, "MJ")]
        [InlineData("ＢＪ", NameSpace.Name, "BJ")]
        [InlineData("ABCD", NameSpace.Name, "ABCD")]
        [InlineData("B", NameSpace.Name, "B")]
        [InlineData("B", NameSpace.Nick, "阿B")]
        [InlineData("Ｂ", NameSpace.Nick, "阿B")]
        [InlineData("Mj", NameSpace.Name, null)]
        [InlineData("b", NameSpace.Nick, null)]
        [InlineData("ABCDE", NameSpace.Name, null)]
        [InlineData("M J", NameSpace.Name, null)]
        [InlineData("", NameSpace.Name, null)]
        [InlineData(null, NameSpace.Name, null)]
        public void SpecialFormRuleApplies(string text, NameSpace ns, string expected)
        {
            Assert.Equal(expected, SpecialFormRule.Apply(text, ns));
        }

        [Theory]
        [InlineData("Jean-Luc", "尚·呂克", NameSpace.Name, "尚呂克")]
        [InlineData("Jean-Luc", "尚・呂克", NameSpace.Name, "尚呂克")]
        [InlineData("Gisele", "  吉賽兒 ", NameSpace.Name, "吉賽兒")]
        [InlineData("Gisele", "吉​賽兒", NameSpace.Name, "吉賽兒")]
        [InlineData("Gisele", "吉賽兒。", NameSpace.Name, "吉賽兒")]
        [InlineData("MJ", "ＭＪ", NameSpace.Name, "MJ")]
        [InlineData("Big D", "大Ｄ", NameSpace.Nick, "大D")]
        [InlineData("Big D", "大d", NameSpace.Nick, null)]
        [InlineData("Ad", "AD", NameSpace.Name, null)]
        [InlineData("Gisele", "吉賽兒（Gisele）", NameSpace.Name, null)]
        [InlineData("Kim", "金1", NameSpace.Name, null)]
        [InlineData("Haruka", "はるか", NameSpace.Name, null)]
        [InlineData("Kim", "\U0002000B", NameSpace.Name, null)]
        [InlineData("Lee", "李李李李李李李李李李李李李", NameSpace.Name, null)]
        [InlineData("Doc", "老老老老老老老老老老老老老", NameSpace.Nick, "老老老老老老老老老老老老老")]
        [InlineData("x", "", NameSpace.Name, null)]
        [InlineData("x", "  ·  ", NameSpace.Name, null)]
        [InlineData("x", null, NameSpace.Name, null)]
        public void SanitizeCleansOrRejects(string source, string candidate, NameSpace ns, string expected)
        {
            Assert.Equal(expected, NameSanitizer.Sanitize(source, candidate, ns));
        }

        [Theory]
        [InlineData("Gisele 疤痕", true)]
        [InlineData("疤痕", true)]
        [InlineData("豈", true)]          // U+F900 compatibility ideograph
        [InlineData("\U0002000B", true)] // CJK Extension B
        [InlineData("Gisele", false)]
        [InlineData("はるか", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void ContainsChineseDetectsTranslatedFields(string text, bool expected)
        {
            Assert.Equal(expected, NameSanitizer.ContainsChinese(text));
        }
    }
}
