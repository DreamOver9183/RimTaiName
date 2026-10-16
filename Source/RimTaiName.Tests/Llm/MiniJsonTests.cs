using System;
using System.Collections.Generic;
using RimTaiName.Llm;
using Xunit;

namespace RimTaiName.Tests.Llm
{
    public class MiniJsonTests
    {
        [Fact]
        public void SerializesNestedValues()
        {
            var value = new Dictionary<string, object>
            {
                ["s"] = "吉賽兒 \"Fox\"\n",
                ["b"] = false,
                ["n"] = null,
                ["i"] = 3,
                ["d"] = 1.5,
                ["a"] = new List<object> { 1L, "x", new Dictionary<string, object>() },
            };
            Assert.Equal("{\"s\":\"吉賽兒 \\\"Fox\\\"\\n\",\"b\":false,\"n\":null,\"i\":3,\"d\":1.5,\"a\":[1,\"x\",{}]}",
                         MiniJson.Serialize(value));
        }

        [Fact]
        public void EscapesControlCharacters()
        {
            Assert.Equal("\"a\\u0001\\\\b\\t\"", MiniJson.Serialize("a\u0001\\b\t"));
        }

        [Fact]
        public void RoundTrips()
        {
            const string json = "{\"steps\":[{\"type\":\"model_output\",\"content\":[{\"type\":\"text\",\"text\":\"{\\\"first\\\":\\\"吉賽兒\\\"}\"}]}],\"n\":-12,\"f\":2.5e3,\"ok\":true,\"none\":null}";
            object parsed = MiniJson.Parse(json);
            Assert.Equal(json.Replace("2.5e3", "2500"), MiniJson.Serialize(parsed));
        }

        [Fact]
        public void ParsesNumbersAsLongOrDouble()
        {
            var obj = MiniJson.AsObject(MiniJson.Parse("{\"a\":42,\"b\":-0.25,\"c\":1E2}"));
            Assert.Equal(42L, obj["a"]);
            Assert.Equal(-0.25, obj["b"]);
            Assert.Equal(100.0, obj["c"]);
        }

        [Fact]
        public void DecodesUnicodeEscapesIncludingSurrogatePairs()
        {
            Assert.Equal("é𠀋", MiniJson.Parse("\"\\u00e9\\ud840\\udc0b\""));
        }

        [Fact]
        public void AccessorsTolerateMissingOrWrongTypes()
        {
            var obj = MiniJson.AsObject(MiniJson.Parse("{\"code\":429,\"message\":\"m\"}"));
            Assert.Null(MiniJson.GetString(obj, "code"));
            Assert.Equal("m", MiniJson.GetString(obj, "message"));
            Assert.Null(MiniJson.GetString(null, "x"));
            Assert.Null(MiniJson.GetString(obj, null));
            Assert.Null(MiniJson.Get(obj, null));
            Assert.Null(MiniJson.Get(obj, "missing"));
            Assert.Null(MiniJson.AsArray(obj));
        }

        [Theory]
        [InlineData("")]
        [InlineData("{")]
        [InlineData("{\"a\":}")]
        [InlineData("[1,]")]
        [InlineData("{\"a\":1} x")]
        [InlineData("\"unterminated")]
        [InlineData("\"bad \\q escape\"")]
        [InlineData("tru")]
        [InlineData("nul")]
        [InlineData("[fals")]
        [InlineData("{\"a\":n")]
        [InlineData("<html>502 Bad Gateway</html>")]
        public void RejectsInvalidJson(string json)
        {
            Assert.Throws<FormatException>(() => MiniJson.Parse(json));
        }

        [Fact]
        public void RejectsExcessiveNesting()
        {
            string json = new string('[', 100) + new string(']', 100);
            Assert.Throws<FormatException>(() => MiniJson.Parse(json));
        }
    }
}
