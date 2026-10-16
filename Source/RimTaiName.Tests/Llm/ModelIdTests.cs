using RimTaiName.Llm;
using Xunit;

namespace RimTaiName.Tests.Llm
{
    public class ModelIdTests
    {
        [Theory]
        [InlineData("gemini-3.5-flash-lite", "gemini-3.5-flash-lite")]
        [InlineData("  gemini-3.1-flash-lite  ", "gemini-3.1-flash-lite")]
        [InlineData("models/gemini-3.5-flash-lite", "gemini-3.5-flash-lite")]
        [InlineData("a", "a")]
        public void AcceptsValidIds(string input, string expected)
        {
            Assert.True(ModelId.TryNormalize(input, out string id));
            Assert.Equal(expected, id);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("models/")]
        [InlineData("Gemini-3.5-Flash-Lite")]
        [InlineData("gemini..flash")]
        [InlineData("../interactions")]
        [InlineData("gemini/../../v1/files")]
        [InlineData("gemini?key=x")]
        [InlineData("gemini#x")]
        [InlineData("gemini flash")]
        [InlineData("-gemini")]
        [InlineData("gemini-")]
        [InlineData("gemini_flash")]
        [InlineData("gemini%2F..")]
        public void RejectsIdsThatCouldAlterTheRequest(string input)
        {
            Assert.False(ModelId.TryNormalize(input, out string id));
            Assert.Null(id);
        }

        [Fact]
        public void RejectsOverlongIds()
        {
            Assert.True(ModelId.TryNormalize(new string('a', ModelId.MaxLength), out _));
            Assert.False(ModelId.TryNormalize(new string('a', ModelId.MaxLength + 1), out _));
        }
    }
}
