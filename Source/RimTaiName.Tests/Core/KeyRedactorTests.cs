using System;
using RimTaiName.Core;
using Xunit;

namespace RimTaiName.Tests.Core
{
    public class KeyRedactorTests
    {
        // Fake keys with the real shapes ("AIza" + 35, "AQ." + 50). Built at runtime so no key-shaped literal
        // appears in the repository (GitHub secret scanning would flag it).
        private static readonly string AizaKey = "AI" + "za" + new string('x', 31) + "90ab";
        private static readonly string AqKey = "AQ" + "." + "Ab8RN6L-fake_key.value" + new string('y', 24) + "789w";

        [Fact]
        public void FakeKeysHaveRealisticShapes()
        {
            Assert.Equal(39, AizaKey.Length);
            Assert.Equal(53, AqKey.Length);
        }

        [Fact]
        public void MasksAizaPattern()
        {
            string result = KeyRedactor.Redact("request failed for key " + AizaKey + " today");
            Assert.Equal("request failed for key AIza…90ab today", result);
        }

        [Fact]
        public void MasksAqPattern()
        {
            string result = KeyRedactor.Redact("x-goog-api-key: " + AqKey);
            Assert.Equal("x-goog-api-key: AQ.…789w", result);
        }

        [Fact]
        public void MasksKnownKeyEvenWithoutAKnownShape()
        {
            const string odd = "custom-secret-value-0001";
            string result = KeyRedactor.Redact("value=" + odd + ";", odd);
            Assert.Equal("value=…0001;", result);
        }

        [Fact]
        public void MasksEveryOccurrenceOfSeveralKeys()
        {
            string text = AizaKey + " " + AqKey + " " + AizaKey;
            string result = KeyRedactor.Redact(text, AqKey);
            Assert.Equal("AIza…90ab AQ.…789w AIza…90ab", result);
        }

        [Fact]
        public void MasksKeysInsideExceptionText()
        {
            var e = new InvalidOperationException("GET https://example.invalid/?key=" + AizaKey + " failed");
            string result = KeyRedactor.Redact(e.ToString(), AizaKey);
            Assert.DoesNotContain(AizaKey, result);
            Assert.Contains("AIza…90ab", result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("nothing secret here: AIza too short, AQ.short")]
        public void LeavesTextWithoutKeysUnchanged(string text)
        {
            Assert.Equal(text, KeyRedactor.Redact(text));
        }

        [Fact]
        public void IgnoresKnownKeysTooShortToBeReal()
        {
            Assert.Equal("a b c", KeyRedactor.Redact("a b c", "b"));
        }

        [Fact]
        public void MaskShowsRecognisedPrefixAndLastFour()
        {
            Assert.Equal("AIza…90ab", KeyRedactor.Mask(AizaKey));
            Assert.Equal("AQ.…789w", KeyRedactor.Mask(AqKey));
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("AIzaShort", "AIza…")]
        [InlineData("plainsecret123", "…t123")]
        public void MaskHandlesShortAndUnknownKeys(string key, string expected)
        {
            Assert.Equal(expected, KeyRedactor.Mask(key));
        }
    }
}
