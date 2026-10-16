using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RimTaiName.Llm;
using RimTaiName.Pipeline;
using Xunit;

namespace RimTaiName.Tests.Pipeline
{
    public sealed class NameProcessorServiceTests : IDisposable
    {
        private const string DictionaryText =
            BuiltInDictionary.Header + "\n" +
            "name\tgisele\t吉賽兒\n" +
            "name\tmiller\t米勒\n" +
            "name\tfox\t福克斯\n" +
            "name\tad\tAD\n" +
            "name\tanna\t安娜\n" +
            "nick\tfox\t狐狸\n" +
            "nick\tdoc\t老醫\n";

        private readonly string dir = Path.Combine(Path.GetTempPath(), "rimtainame_pipeline_" + Guid.NewGuid().ToString("N"));

        private readonly PersonalNameCache cache;

        private readonly NameProcessorService service;

        public NameProcessorServiceTests()
        {
            Directory.CreateDirectory(dir);
            string dictionaryPath = Path.Combine(dir, "dict.tsv");
            File.WriteAllText(dictionaryPath, DictionaryText);
            cache = new PersonalNameCache(Path.Combine(dir, "cache.xml"));
            cache.Load();
            service = new NameProcessorService(new BuiltInDictionary(dictionaryPath), cache);
        }

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        public void Dispose()
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }

        private static NameRequestPayload Payload(string first, string nick, string last, NickSource source = NickSource.Explicit)
        {
            return new NameRequestPayload(first, nick, last) { NickSource = source };
        }

        private static void AssertField(NameResult result, NameField field, FieldSource source, string value)
        {
            Assert.Equal(source, result[field].Source);
            Assert.Equal(value, result[field].Value);
        }

        // ---- local resolution ----

        [Fact]
        public void DictionaryResolvesAllThreeFields()
        {
            NameResult result = service.ResolveLocal(Payload("Gisele", "Fox", "Miller"));
            AssertField(result, NameField.First, FieldSource.Dictionary, "吉賽兒");
            AssertField(result, NameField.Nick, FieldSource.Dictionary, "狐狸");
            AssertField(result, NameField.Last, FieldSource.Dictionary, "米勒");
            Assert.False(result.HasPending);
            Assert.Equal("Fox", result[NameField.Nick].SourceText);
        }

        [Fact]
        public void LookupIgnoresCaseAndSpaces()
        {
            NameResult result = service.ResolveLocal(Payload("  GISELE ", "", "miller"));
            AssertField(result, NameField.First, FieldSource.Dictionary, "吉賽兒");
            Assert.Equal("GISELE", result[NameField.First].SourceText);
        }

        [Fact]
        public void NamespacesDoNotFallBack()
        {
            NameResult result = service.ResolveLocal(Payload("Fox", "Miller", "Fox"));
            AssertField(result, NameField.First, FieldSource.Dictionary, "福克斯");
            AssertField(result, NameField.Last, FieldSource.Dictionary, "福克斯");
            // "Miller" exists only as a name: a nickname must not borrow it.
            AssertField(result, NameField.Nick, FieldSource.Pending, null);
        }

        [Theory]
        [InlineData("", FieldSource.Unchanged)]
        [InlineData("   ", FieldSource.Unchanged)]
        [InlineData(null, FieldSource.Unchanged)]
        [InlineData("吉賽兒", FieldSource.Unchanged)]
        [InlineData("Gisele 疤痕", FieldSource.Unchanged)]
        [InlineData("Zzyzx", FieldSource.Pending)]
        public void BlankChineseAndUnknownFields(string first, FieldSource expected)
        {
            NameResult result = service.ResolveLocal(Payload(first, "Doc", "Miller"));
            Assert.Equal(expected, result[NameField.First].Source);
            Assert.Null(result[NameField.First].Value);
        }

        [Fact]
        public void LockedFieldsAreNeverTranslated()
        {
            NameRequestPayload payload = Payload("Gisele", "Fox", "Miller");
            payload.Locked[(int)NameField.First] = true;
            payload.Locked[(int)NameField.Nick] = true;
            NameResult result = service.ResolveLocal(payload);
            AssertField(result, NameField.First, FieldSource.Unchanged, null);
            AssertField(result, NameField.Nick, FieldSource.Unchanged, null);
            AssertField(result, NameField.Last, FieldSource.Dictionary, "米勒");
        }

        [Fact]
        public void SpecialFormsComeFirst()
        {
            NameResult result = service.ResolveLocal(Payload("MJ", "B", "ＴＪ"));
            AssertField(result, NameField.First, FieldSource.SpecialForm, "MJ");
            AssertField(result, NameField.Nick, FieldSource.SpecialForm, "阿B");
            AssertField(result, NameField.Last, FieldSource.SpecialForm, "TJ");
        }

        [Fact]
        public void CaseInsensitiveHitWithForeignCapitalIsRejected()
        {
            // The dictionary maps "ad" to "AD", but the source "Ad" has no capital D.
            AssertField(service.ResolveLocal(Payload("Ad", "", "Miller")), NameField.First, FieldSource.Pending, null);
            AssertField(service.ResolveLocal(Payload("AD", "", "Miller")), NameField.First, FieldSource.SpecialForm, "AD");
        }

        [Fact]
        public void PersonalCacheWinsOverDictionary()
        {
            cache.Put(NameSpace.Name, "miller", "米樂", CacheOrigin.User);
            AssertField(service.ResolveLocal(Payload("Gisele", "", "Miller")), NameField.Last, FieldSource.PersonalCache, "米樂");
        }

        [Fact]
        public void UnusableCacheEntryFallsThroughToDictionary()
        {
            cache.Put(NameSpace.Name, "miller", "Miller", CacheOrigin.Api);
            AssertField(service.ResolveLocal(Payload("Gisele", "", "Miller")), NameField.Last, FieldSource.Dictionary, "米勒");
        }

        [Fact]
        public void GenerateModeIsNotImplementedYet()
        {
            NameRequestPayload payload = Payload("Gisele", "", "Miller");
            payload.Mode = ProcessMode.Generate;
            Assert.Throws<NotSupportedException>(() => service.ResolveLocal(payload));
        }

        // ---- nickname aliases (v3 §6.3) ----

        [Theory]
        [InlineData(NickSource.AliasOfFirst, true, FieldSource.Alias, "吉賽兒")]
        [InlineData(NickSource.AliasOfLast, true, FieldSource.Alias, "米勒")]
        [InlineData(NickSource.AliasOfFirst, false, FieldSource.Unchanged, null)]
        [InlineData(NickSource.Explicit, true, FieldSource.Unchanged, null)]
        public void BlankNicknameFollowsVanillaAddress(NickSource source, bool keep, FieldSource expected, string value)
        {
            NameRequestPayload payload = Payload("Gisele", "", "Miller", source);
            payload.KeepVanillaAddress = keep;
            AssertField(service.ResolveLocal(payload), NameField.Nick, expected, value);
        }

        [Fact]
        public void NicknameEqualToTheFirstNameMirrorsIt()
        {
            NameResult result = service.ResolveLocal(Payload("Gisele", "gisele", "Miller", NickSource.AliasOfFirst));
            AssertField(result, NameField.Nick, FieldSource.Alias, "吉賽兒");
            Assert.Equal(NameField.First, result[NameField.Nick].AliasOf);
        }

        [Fact]
        public void NicknameEqualToTheOriginalNameMirrorsTheTranslatedField()
        {
            // The first name was translated earlier in the same dialog; the nickname still holds the English.
            var payload = new NameRequestPayload("Gisele", "Gisele", "Miller");
            payload.Text[(int)NameField.First] = "吉賽兒";
            AssertField(service.ResolveLocal(payload), NameField.Nick, FieldSource.Alias, "吉賽兒");
        }

        [Fact]
        public void RerolledEnglishNameMakesTheOldNicknameIndependent()
        {
            // Opened as Gisele "Gisele" Miller, then the first name was rerolled to another English name.
            var payload = new NameRequestPayload("Gisele", "Gisele", "Miller") { NickSource = NickSource.AliasOfFirst };
            payload.Text[(int)NameField.First] = "Anna";
            NameResult result = service.ResolveLocal(payload);
            AssertField(result, NameField.First, FieldSource.Dictionary, "安娜");
            AssertField(result, NameField.Nick, FieldSource.Pending, null);
            Assert.Null(result[NameField.Nick].AliasOf);
        }

        [Fact]
        public void AliasComparisonIgnoresUnicodeNormalizationForm()
        {
            NameResult result = service.ResolveLocal(Payload("Zoë", "Zoë", "Miller"));
            Assert.Equal(NameField.First, result[NameField.Nick].AliasOf);
        }

        [Fact]
        public void NullArgumentsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => NickAliasResolver.Target(null));
            var ok = new FieldResult(FieldSource.Unchanged, null, "x");
            Assert.Throws<ArgumentNullException>(() => new NameResult(ok, null, ok));
        }

        [Fact]
        public void AliasMirrorsALockedChineseField()
        {
            var payload = new NameRequestPayload("Gisele", "Gisele", "Miller");
            payload.Text[(int)NameField.First] = "姬瑟";
            payload.Locked[(int)NameField.First] = true;
            NameResult result = service.ResolveLocal(payload);
            AssertField(result, NameField.First, FieldSource.Unchanged, null);
            AssertField(result, NameField.Nick, FieldSource.Alias, "姬瑟");
        }

        [Fact]
        public void AliasOfAnUntranslatableFieldLeavesTheNicknameAlone()
        {
            NameRequestPayload payload = Payload("Gisele", "", "MJ", NickSource.AliasOfLast);
            AssertField(service.ResolveLocal(payload), NameField.Nick, FieldSource.Unchanged, null);
        }

        [Fact]
        public void LockedNicknameIsNeverAnAlias()
        {
            NameRequestPayload payload = Payload("Gisele", "Gisele", "Miller", NickSource.AliasOfFirst);
            payload.Locked[(int)NameField.Nick] = true;
            AssertField(service.ResolveLocal(payload), NameField.Nick, FieldSource.Unchanged, null);
        }

        [Fact]
        public void AliasWaitsForAPendingField()
        {
            NameResult result = service.ResolveLocal(Payload("Zzyzx", "", "Miller", NickSource.AliasOfFirst));
            AssertField(result, NameField.Nick, FieldSource.Pending, null);
            Assert.Equal(NameField.First, result[NameField.Nick].AliasOf);
        }

        [Theory]
        [InlineData("Gisele", "", "Miller", false, "Gisele", NickSource.AliasOfFirst)]
        [InlineData("Gisele", "", "Miller", false, "Miller", NickSource.AliasOfLast)]
        [InlineData("Gisele", "  GISELE ", "Miller", true, "GISELE", NickSource.AliasOfFirst)]
        [InlineData("Gisele", "miller", "Miller", true, "miller", NickSource.AliasOfLast)]
        [InlineData("Gisele", "Fox", "Miller", true, "Fox", NickSource.Explicit)]
        [InlineData("Ann", "Ann", "Ann", true, "Ann", NickSource.AliasOfFirst)]
        public void DetermineNickSourceAtDialogOpen(string first, string nickField, string last, bool nickSet, string resolved, NickSource expected)
        {
            Assert.Equal(expected, NickAliasResolver.Determine(first, nickField, last, nickSet, resolved));
        }

        // ---- remote resolution ----

        [Fact]
        public async Task SendsOnlyPendingFieldsAndCachesSanitizedResults()
        {
            var client = new FakeClient(LlmResponse.Ok("佐吉克斯", "瘋狗", "這不該被使用"));
            NameRequestPayload payload = Payload("Zzyzx", "Mad Dog", "Miller");
            NameResult local = service.ResolveLocal(payload);
            NameResult result = await service.ResolveRemoteAsync(payload, local, client, Ct);

            LlmRequest request = Assert.Single(client.Requests);
            Assert.Equal(new[] { true, true, false }, request.Wanted);
            Assert.Equal(new[] { "Zzyzx", "Mad Dog", "Miller" }, request.Text);
            AssertField(result, NameField.First, FieldSource.Api, "佐吉克斯");
            AssertField(result, NameField.Nick, FieldSource.Api, "瘋狗");
            AssertField(result, NameField.Last, FieldSource.Dictionary, "米勒");
            Assert.Equal(GeminiErrorKind.None, result.RemoteError);

            Assert.True(cache.TryGet(NameSpace.Name, "zzyzx", out string first, out CacheOrigin origin));
            Assert.Equal("佐吉克斯", first);
            Assert.Equal(CacheOrigin.Api, origin);
            Assert.True(cache.TryGet(NameSpace.Nick, "mad dog", out string nick, out _));
            Assert.Equal("瘋狗", nick);
        }

        [Theory]
        [InlineData("", NickSource.AliasOfFirst)]         // blank alias nickname waits for the first name
        [InlineData("Mad Dog", NickSource.Explicit)]      // retyped by the player: translated on its own
        public async Task RequestDescribesTheNicknameAsResolvedNow(string nick, NickSource expected)
        {
            var client = new FakeClient(LlmResponse.Ok("佐吉克斯", "瘋狗", null));
            NameRequestPayload payload = Payload("Zzyzx", nick, "Miller", NickSource.AliasOfFirst);
            await service.ResolveRemoteAsync(payload, service.ResolveLocal(payload), client, Ct);
            Assert.Equal(expected, Assert.Single(client.Requests).NickSource);
        }

        [Fact]
        public async Task NothingPendingMeansNoRequest()
        {
            var client = new FakeClient(LlmResponse.Ok("x", "y", "z"));
            NameRequestPayload payload = Payload("Gisele", "Fox", "Miller");
            NameResult local = service.ResolveLocal(payload);
            Assert.Same(local, await service.ResolveRemoteAsync(payload, local, client, Ct));
            Assert.Empty(client.Requests);
        }

        [Fact]
        public async Task PendingAliasIsFilledAfterTheRemoteResult()
        {
            var client = new FakeClient(LlmResponse.Ok("佐吉克斯", "別理我", null));
            NameRequestPayload payload = Payload("Zzyzx", "", "Miller", NickSource.AliasOfFirst);
            NameResult result = await service.ResolveRemoteAsync(payload, service.ResolveLocal(payload), client, Ct);
            Assert.Equal(new[] { true, false, false }, Assert.Single(client.Requests).Wanted);
            AssertField(result, NameField.Nick, FieldSource.Alias, "佐吉克斯");
        }

        [Fact]
        public async Task FailedCallMarksPendingFieldsAndCachesNothing()
        {
            var client = new FakeClient(LlmResponse.Fail(GeminiErrorKind.QuotaExceeded, 429, "daily quota"));
            NameRequestPayload payload = Payload("Zzyzx", "", "Miller", NickSource.AliasOfFirst);
            NameResult result = await service.ResolveRemoteAsync(payload, service.ResolveLocal(payload), client, Ct);
            AssertField(result, NameField.First, FieldSource.Failed, null);
            Assert.Equal(GeminiErrorKind.QuotaExceeded, result[NameField.First].Error);
            AssertField(result, NameField.Nick, FieldSource.Unchanged, null); // nothing Chinese to mirror
            AssertField(result, NameField.Last, FieldSource.Dictionary, "米勒");
            Assert.Equal(GeminiErrorKind.QuotaExceeded, result.RemoteError);
            Assert.Equal(429, result.RemoteStatusCode);
            Assert.Equal("daily quota", result.RemoteDetail);
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public async Task MissingOrUnusableFieldsFailIndividually()
        {
            var client = new FakeClient(LlmResponse.Ok("佐吉克斯（Zzyzx）", null, "奎爾"));
            NameRequestPayload payload = Payload("Zzyzx", "Mad Dog", "Quill");
            NameResult result = await service.ResolveRemoteAsync(payload, service.ResolveLocal(payload), client, Ct);
            AssertField(result, NameField.First, FieldSource.Failed, null);
            Assert.Equal(GeminiErrorKind.InvalidResponse, result[NameField.First].Error);
            AssertField(result, NameField.Nick, FieldSource.Failed, null);
            AssertField(result, NameField.Last, FieldSource.Api, "奎爾");
            Assert.Equal(GeminiErrorKind.None, result.RemoteError);
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public async Task ApiResultIsSanitized()
        {
            var client = new FakeClient(LlmResponse.Ok("尚·呂克", null, null));
            NameRequestPayload payload = Payload("Jean-Luc", "", "Miller");
            NameResult result = await service.ResolveRemoteAsync(payload, service.ResolveLocal(payload), client, Ct);
            AssertField(result, NameField.First, FieldSource.Api, "尚呂克");
        }

        [Fact]
        public async Task ClientExceptionBecomesAFailure()
        {
            var client = new FakeClient(new InvalidOperationException("boom"));
            NameRequestPayload payload = Payload("Zzyzx", "", "Miller");
            NameResult result = await service.ResolveRemoteAsync(payload, service.ResolveLocal(payload), client, Ct);
            AssertField(result, NameField.First, FieldSource.Failed, null);
            Assert.Equal(GeminiErrorKind.InvalidResponse, result.RemoteError);
            Assert.Equal("InvalidOperationException", result.RemoteDetail);
        }

        [Fact]
        public async Task UnrequestedCancellationIsATimeout()
        {
            var client = new FakeClient(new TaskCanceledException("HttpClient.Timeout"));
            NameRequestPayload payload = Payload("Zzyzx", "", "Miller");
            NameResult result = await service.ResolveRemoteAsync(payload, service.ResolveLocal(payload), client, Ct);
            Assert.Equal(GeminiErrorKind.Timeout, result.RemoteError);
            Assert.Equal(GeminiErrorKind.Timeout, result[NameField.First].Error);
        }

        [Fact]
        public async Task CancellationPropagates()
        {
            var client = new FakeClient(LlmResponse.Ok("x", null, null));
            NameRequestPayload payload = Payload("Zzyzx", "", "Miller");
            NameResult local = service.ResolveLocal(payload);
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ResolveRemoteAsync(payload, local, client, cts.Token));
            }
        }

        [Fact]
        public async Task LockedFieldIgnoresAnyRemoteValue()
        {
            var client = new FakeClient(LlmResponse.Ok("佐吉克斯", "瘋狗", "奎爾"));
            NameRequestPayload payload = Payload("Zzyzx", "Mad Dog", "Quill");
            payload.Locked[(int)NameField.Last] = true;
            NameResult result = await service.ResolveRemoteAsync(payload, service.ResolveLocal(payload), client, Ct);
            Assert.Equal(new[] { false, false, true }, Assert.Single(client.Requests).Locked);
            Assert.False(Assert.Single(client.Requests).Wanted[(int)NameField.Last]);
            AssertField(result, NameField.Last, FieldSource.Unchanged, null);
        }

        private sealed class FakeClient : ILlmClient
        {
            private readonly LlmResponse response;

            private readonly Exception exception;

            public FakeClient(LlmResponse response)
            {
                this.response = response;
            }

            public FakeClient(Exception exception)
            {
                this.exception = exception;
            }

            public List<LlmRequest> Requests { get; } = new List<LlmRequest>();

            public Task<LlmResponse> TranslateAsync(LlmRequest request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Requests.Add(request);
                if (exception != null)
                    throw exception;
                return Task.FromResult(response);
            }
        }
    }
}
