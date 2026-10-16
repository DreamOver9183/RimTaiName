using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RimTaiName.Pipeline;
using Xunit;

namespace RimTaiName.Tests.Pipeline
{
    public sealed class PersonalNameCacheTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "rimtainame_cache_" + Guid.NewGuid().ToString("N"));

        private string CachePath => Path.Combine(dir, "RimTaiName_Cache.xml");

        public void Dispose()
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }

        private PersonalNameCache Loaded()
        {
            var cache = new PersonalNameCache(CachePath);
            cache.Load();
            return cache;
        }

        [Fact]
        public void MissingFileMeansEmptyCache()
        {
            PersonalNameCache cache = Loaded();
            Assert.Equal(0, cache.Count);
            Assert.Null(cache.LastError);
            Assert.False(cache.TryGet(NameSpace.Name, "gisele", out _, out _));
        }

        [Fact]
        public void SaveAndLoadRoundTrip()
        {
            PersonalNameCache cache = Loaded();
            Assert.True(cache.Put(NameSpace.Name, "gisele", "吉賽兒", CacheOrigin.Api));
            Assert.True(cache.Put(NameSpace.Nick, "fox", "狐狸", CacheOrigin.User));
            Assert.True(cache.Put(NameSpace.Name, "o'brien & <co>", "歐布萊恩", CacheOrigin.Api));
            Assert.True(cache.SaveIfDirty());
            Assert.False(cache.IsDirty);

            PersonalNameCache reloaded = Loaded();
            Assert.Equal(3, reloaded.Count);
            Assert.True(reloaded.TryGet(NameSpace.Name, "gisele", out string value, out CacheOrigin origin));
            Assert.Equal("吉賽兒", value);
            Assert.Equal(CacheOrigin.Api, origin);
            Assert.True(reloaded.TryGet(NameSpace.Nick, "fox", out value, out origin));
            Assert.Equal("狐狸", value);
            Assert.Equal(CacheOrigin.User, origin);
            Assert.True(reloaded.TryGet(NameSpace.Name, "o'brien & <co>", out value, out _));
            Assert.Equal("歐布萊恩", value);
        }

        [Fact]
        public void NamespacesAreIsolated()
        {
            PersonalNameCache cache = Loaded();
            cache.Put(NameSpace.Name, "fox", "福克斯", CacheOrigin.Api);
            cache.Put(NameSpace.Nick, "fox", "狐狸", CacheOrigin.Api);
            Assert.True(cache.TryGet(NameSpace.Name, "fox", out string name, out _));
            Assert.True(cache.TryGet(NameSpace.Nick, "fox", out string nick, out _));
            Assert.Equal("福克斯", name);
            Assert.Equal("狐狸", nick);
            Assert.False(cache.TryGet(NameSpace.Nick, "miller", out _, out _));
        }

        [Fact]
        public void UserCorrectionWinsOverApi()
        {
            PersonalNameCache cache = Loaded();
            cache.Put(NameSpace.Name, "miller", "米勒", CacheOrigin.Api);
            Assert.True(cache.Put(NameSpace.Name, "miller", "米樂", CacheOrigin.User));
            Assert.False(cache.Put(NameSpace.Name, "miller", "米勒", CacheOrigin.Api));
            cache.TryGet(NameSpace.Name, "miller", out string value, out CacheOrigin origin);
            Assert.Equal("米樂", value);
            Assert.Equal(CacheOrigin.User, origin);
            Assert.True(cache.Put(NameSpace.Name, "miller", "米勒爾", CacheOrigin.User));
        }

        [Fact]
        public void UnchangedPutDoesNotDirty()
        {
            PersonalNameCache cache = Loaded();
            cache.Put(NameSpace.Name, "anna", "安娜", CacheOrigin.Api);
            cache.SaveIfDirty();
            Assert.False(cache.Put(NameSpace.Name, "anna", "安娜", CacheOrigin.Api));
            Assert.False(cache.IsDirty);
        }

        [Theory]
        [InlineData(null, "x")]
        [InlineData("", "x")]
        [InlineData("   ", "x")]
        [InlineData("key", null)]
        [InlineData("key", "")]
        [InlineData("key", " ")]
        [InlineData("bad\u0001key", "x")]
        [InlineData("key", "bad￿value")]
        [InlineData("tab\tkey", "x")]
        [InlineData("key", "line\nbreak")]
        [InlineData("key", "carriage\rreturn")]
        public void RejectsBlankOrUnstorableText(string key, string value)
        {
            PersonalNameCache cache = Loaded();
            Assert.False(cache.Put(NameSpace.Name, key, value, CacheOrigin.Api));
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void RejectsOverlongText()
        {
            PersonalNameCache cache = Loaded();
            Assert.True(cache.Put(NameSpace.Name, new string('k', 256), "x", CacheOrigin.Api));
            Assert.False(cache.Put(NameSpace.Name, new string('k', 257), "x", CacheOrigin.Api));
            Assert.False(cache.Put(NameSpace.Name, "k", new string('v', 257), CacheOrigin.Api));
        }

        [Fact]
        public void DtdIsProhibitedAndTreatedAsCorrupt()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(CachePath,
                "<?xml version=\"1.0\"?><!DOCTYPE RimTaiNameCache [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;\">]>" +
                "<RimTaiNameCache version=\"1\"><entry ns=\"name\" key=\"x\" value=\"&b;\" /></RimTaiNameCache>");
            PersonalNameCache cache = Loaded();
            Assert.Equal(0, cache.Count);
            Assert.Contains("XmlException", cache.LastError);
            Assert.True(File.Exists(CachePath + ".corrupt"));
        }

        [Fact]
        public void LockedFileIsNotQuarantinedAndIsMergedOnSave()
        {
            PersonalNameCache first = Loaded();
            first.Put(NameSpace.Name, "anna", "安娜", CacheOrigin.Api);
            first.Put(NameSpace.Nick, "fox", "狐狸", CacheOrigin.User);
            first.SaveIfDirty();

            PersonalNameCache cache;
            using (new FileStream(CachePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                cache = Loaded();
                Assert.NotNull(cache.LastError);
                Assert.Equal(0, cache.Count);
                Assert.False(File.Exists(CachePath + ".corrupt"));

                cache.Put(NameSpace.Name, "miller", "米勒", CacheOrigin.Api);
                cache.Put(NameSpace.Nick, "fox", "狐", CacheOrigin.Api);
                Assert.False(cache.SaveIfDirty());
                Assert.True(cache.IsDirty);
            }

            Assert.True(cache.SaveIfDirty());
            PersonalNameCache reloaded = Loaded();
            Assert.Equal(3, reloaded.Count);
            Assert.True(reloaded.TryGet(NameSpace.Name, "anna", out _, out _));
            Assert.True(reloaded.TryGet(NameSpace.Name, "miller", out _, out _));
            Assert.True(reloaded.TryGet(NameSpace.Nick, "fox", out string fox, out CacheOrigin origin));
            Assert.Equal("狐狸", fox);
            Assert.Equal(CacheOrigin.User, origin);
        }

        [Theory]
        [InlineData("user", "api")]
        [InlineData("api", "user")]
        public void DuplicateEntriesKeepThePlayersCorrection(string firstOrigin, string secondOrigin)
        {
            Directory.CreateDirectory(dir);
            string userValue = "米樂", apiValue = "米勒";
            File.WriteAllText(CachePath,
                "<RimTaiNameCache version=\"1\">" +
                $"<entry ns=\"name\" key=\"miller\" value=\"{(firstOrigin == "user" ? userValue : apiValue)}\" origin=\"{firstOrigin}\" />" +
                $"<entry ns=\"name\" key=\"miller\" value=\"{(secondOrigin == "user" ? userValue : apiValue)}\" origin=\"{secondOrigin}\" />" +
                "</RimTaiNameCache>");
            PersonalNameCache cache = Loaded();
            Assert.True(cache.TryGet(NameSpace.Name, "miller", out string value, out CacheOrigin origin));
            Assert.Equal(userValue, value);
            Assert.Equal(CacheOrigin.User, origin);
        }

        [Fact]
        public void CorruptFileStartsEmptyAndIsKeptAside()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(CachePath, "<RimTaiNameCache><entry ns=\"name\"");
            PersonalNameCache cache = Loaded();
            Assert.Equal(0, cache.Count);
            Assert.NotNull(cache.LastError);
            Assert.False(File.Exists(CachePath));
            Assert.True(File.Exists(CachePath + ".corrupt"));

            cache.Put(NameSpace.Name, "anna", "安娜", CacheOrigin.Api);
            Assert.True(cache.SaveIfDirty());
            Assert.Equal(1, Loaded().Count);
        }

        [Fact]
        public void WrongRootIsTreatedAsCorrupt()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(CachePath, "<SomethingElse />");
            PersonalNameCache cache = Loaded();
            Assert.NotNull(cache.LastError);
            Assert.True(File.Exists(CachePath + ".corrupt"));
        }

        [Fact]
        public void SkipsMalformedEntries()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(CachePath,
                "<RimTaiNameCache version=\"1\">" +
                "<entry ns=\"name\" key=\"anna\" value=\"安娜\" origin=\"api\" />" +
                "<entry ns=\"other\" key=\"x\" value=\"y\" />" +
                "<entry ns=\"nick\" key=\"\" value=\"y\" />" +
                "<entry ns=\"nick\" key=\"doc\" />" +
                "</RimTaiNameCache>");
            PersonalNameCache cache = Loaded();
            Assert.Null(cache.LastError);
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void ClearEmptiesTheFileOnSave()
        {
            PersonalNameCache cache = Loaded();
            cache.Put(NameSpace.Name, "anna", "安娜", CacheOrigin.Api);
            cache.SaveIfDirty();
            cache.Clear();
            Assert.True(cache.IsDirty);
            Assert.True(cache.SaveIfDirty());
            Assert.Equal(0, Loaded().Count);
        }

        [Fact]
        public void ConcurrentPutsAreAllKept()
        {
            PersonalNameCache cache = Loaded();
            Parallel.For(0, 500, i => cache.Put(NameSpace.Name, "n" + i, "名" + i, CacheOrigin.Api));
            Assert.Equal(500, cache.Count);
            Assert.True(cache.SaveIfDirty());
            Assert.Equal(500, Loaded().Count);
        }

        [Fact]
        public void SaveWritesSortedStableXml()
        {
            PersonalNameCache cache = Loaded();
            cache.Put(NameSpace.Nick, "b", "乙", CacheOrigin.Api);
            cache.Put(NameSpace.Name, "a", "甲", CacheOrigin.User);
            cache.SaveIfDirty();
            string[] lines = File.ReadAllLines(CachePath).Select(l => l.Trim()).ToArray();
            Assert.Contains("<RimTaiNameCache version=\"1\">", lines);
            int a = Array.FindIndex(lines, l => l.Contains("key=\"a\""));
            int b = Array.FindIndex(lines, l => l.Contains("key=\"b\""));
            Assert.True(a >= 0 && b > a);
            Assert.False(File.Exists(CachePath + ".tmp"));
        }
    }
}
