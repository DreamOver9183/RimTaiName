using System;
using System.Collections.Generic;
using RimTaiName.Pipeline;
using Xunit;

namespace RimTaiName.Tests.Pipeline
{
    public sealed class NameDialogSessionTests
    {
        private static readonly bool[] AllEditable = { true, true, true };

        private static NameDialogSession Session(NickSource source = NickSource.Explicit) =>
            new NameDialogSession("Gisele", "Fox", "Miller", source);

        private static FieldResult Field(FieldSource source, string value, string sourceText, NameField? aliasOf = null) =>
            new FieldResult(source, value, sourceText, aliasOf);

        private static FieldResult Pending(string sourceText, NameField? aliasOf = null) =>
            new FieldResult(FieldSource.Pending, null, sourceText, aliasOf);

        private static NameResult Result(FieldResult first, FieldResult nick, FieldResult last) => new NameResult(first, nick, last);

        private static string[] Values(FieldWrite[] writes) => Array.ConvertAll(writes, w => w?.Value);

        /// <summary>The dialog applies every write, as it does when no field is locked or read-only.</summary>
        private static void Apply(NameDialogSession session, FieldWrite[] writes, string[] fields)
        {
            for (int i = 0; i < writes.Length; i++)
            {
                if (writes[i] == null)
                    continue;
                fields[i] = writes[i].Value;
                session.MarkWritten((NameField)i, writes[i]);
            }
        }

        // ---- payload ----

        [Fact]
        public void PayloadCarriesOpenedTextLocksAndNickSource()
        {
            var session = new NameDialogSession("Gisele", "", "Miller", NickSource.AliasOfFirst);
            session.Locked[(int)NameField.Last] = true;
            NameRequestPayload payload = session.CreatePayload(new[] { "Anna", "", "Miller" }, new[] { true, false, true }, keepVanillaAddress: false);

            Assert.Equal(new[] { "Anna", "", "Miller" }, payload.Text);
            Assert.Equal(new[] { "Gisele", "", "Miller" }, payload.OriginalText);
            Assert.Equal(new[] { false, true, true }, payload.Locked); // read-only nick counts as locked
            Assert.Equal(NickSource.AliasOfFirst, payload.NickSource);
            Assert.False(payload.KeepVanillaAddress);
        }

        [Fact]
        public void WrongFieldCountIsRejected()
        {
            NameDialogSession session = Session();
            Assert.Throws<ArgumentException>(() => session.CreatePayload(new[] { "a", "b" }, AllEditable, true));
            Assert.Throws<ArgumentNullException>(() => session.Observe(null));
        }

        // ---- local and remote writes ----

        [Fact]
        public void LocalValuesAreWrittenAndPendingFieldsWait()
        {
            NameDialogSession session = Session();
            string[] fields = { "Zzyzx", "Fox", "Miller" };
            NameResult local = Result(Pending("Zzyzx"), Field(FieldSource.Dictionary, "狐狸", "Fox"), Field(FieldSource.Dictionary, "米勒", "Miller"));

            FieldWrite[] writes = session.BeginTranslation(local, fields, awaitRemote: true);

            Assert.Equal(new[] { null, "狐狸", "米勒" }, Values(writes));
            Assert.True(writes[1].Learnable);
            Assert.True(session.AwaitingRemote);
        }

        [Fact]
        public void RemoteResultFillsOnlyFieldsThePlayerLeftAlone()
        {
            NameDialogSession session = Session();
            string[] fields = { "Zzyzx", "Mad Dog", "Quill" };
            session.BeginTranslation(Result(Pending("Zzyzx"), Pending("Mad Dog"), Pending("Quill")), fields, awaitRemote: true);

            fields[1] = "Doc"; // retyped while waiting
            NameResult remote = Result(Field(FieldSource.Api, "佐吉克斯", "Zzyzx"), Field(FieldSource.Api, "瘋狗", "Mad Dog"), Field(FieldSource.Api, "奎爾", "Quill"));
            FieldWrite[] writes = session.CompleteTranslation(remote, fields);

            Assert.Equal(new[] { "佐吉克斯", null, "奎爾" }, Values(writes));
            Assert.False(session.AwaitingRemote);
            Assert.Equal(new string[3], Values(session.CompleteTranslation(remote, fields))); // applied once only
        }

        [Fact]
        public void RemoteResultSkipsFieldsLockedWhileWaiting()
        {
            NameDialogSession session = Session();
            string[] fields = { "Zzyzx", "Fox", "Quill" };
            session.BeginTranslation(Result(Pending("Zzyzx"), Field(FieldSource.Dictionary, "狐狸", "Fox"), Pending("Quill")), fields, true);

            session.Locked[(int)NameField.Last] = true;
            NameResult remote = Result(Field(FieldSource.Api, "佐吉克斯", "Zzyzx"), Field(FieldSource.Dictionary, "狐狸", "Fox"), Field(FieldSource.Api, "奎爾", "Quill"));
            fields[1] = "狐狸"; // the local write was applied
            FieldWrite[] writes = session.CompleteTranslation(remote, fields);

            Assert.Equal(new[] { "佐吉克斯", null, null }, Values(writes));
        }

        [Fact]
        public void PendingAliasNicknameIsFilledByTheRemoteResult()
        {
            NameDialogSession session = Session(NickSource.AliasOfFirst);
            string[] fields = { "Zzyzx", "", "Miller" };
            session.BeginTranslation(Result(Pending("Zzyzx"), Pending("", NameField.First), Field(FieldSource.Dictionary, "米勒", "Miller")), fields, true);
            fields[2] = "米勒";

            NameResult remote = Result(Field(FieldSource.Api, "佐吉克斯", "Zzyzx"), Field(FieldSource.Alias, "佐吉克斯", "", NameField.First),
                                       Field(FieldSource.Dictionary, "米勒", "Miller"));
            FieldWrite[] writes = session.CompleteTranslation(remote, fields);

            Assert.Equal(new[] { "佐吉克斯", "佐吉克斯", null }, Values(writes));
            Assert.False(writes[1].Learnable);
        }

        [Fact]
        public void RestoreDiscardsALateResult()
        {
            NameDialogSession session = Session();
            string[] fields = { "Zzyzx", "Fox", "Miller" };
            session.BeginTranslation(Result(Pending("Zzyzx"), Field(FieldSource.Unchanged, null, "Fox"), Field(FieldSource.Unchanged, null, "Miller")), fields, true);

            session.Restore();
            FieldWrite[] writes = session.CompleteTranslation(Result(Field(FieldSource.Api, "佐吉克斯", "Zzyzx"), Field(FieldSource.Unchanged, null, "Fox"),
                                                                     Field(FieldSource.Unchanged, null, "Miller")), fields);

            Assert.Equal(new string[3], Values(writes));
            Assert.False(session.AwaitingRemote);
        }

        [Fact]
        public void ResultIsIgnoredWhenNoRequestWasSent()
        {
            NameDialogSession session = Session();
            string[] fields = { "Zzyzx", "Fox", "Miller" };
            session.BeginTranslation(Result(Pending("Zzyzx"), Field(FieldSource.Unchanged, null, "Fox"), Field(FieldSource.Unchanged, null, "Miller")), fields,
                                     awaitRemote: false); // no key or the gate refused

            Assert.False(session.AwaitingRemote);
            Assert.Equal(new string[3], Values(session.CompleteTranslation(Result(Field(FieldSource.Api, "佐吉克斯", "Zzyzx"),
                                                                                     Field(FieldSource.Unchanged, null, "Fox"),
                                                                                     Field(FieldSource.Unchanged, null, "Miller")), fields)));
        }

        [Fact]
        public void ValueAlreadyInTheFieldIsNotWrittenAgain()
        {
            NameDialogSession session = Session();
            FieldWrite[] writes = session.BeginTranslation(Result(Field(FieldSource.SpecialForm, "MJ", "MJ"), Field(FieldSource.Unchanged, null, "Fox"),
                                                                  Field(FieldSource.Unchanged, null, "Miller")), new[] { "MJ", "Fox", "Miller" }, false);
            Assert.Null(writes[0]);
        }

        [Fact]
        public void RestoreWritesOpenedValuesForUnlockedFields()
        {
            NameDialogSession session = Session();
            session.Locked[(int)NameField.Nick] = true;
            FieldWrite[] writes = session.Restore();

            Assert.Equal(new[] { "Gisele", null, "Miller" }, Values(writes));
            Assert.False(writes[0].Learnable);
        }

        // ---- learning ----

        [Fact]
        public void PlayerEditOfATranslationIsACorrection()
        {
            NameDialogSession session = Session();
            string[] fields = { "Gisele", "Fox", "Miller" };
            Apply(session, session.BeginTranslation(Result(Field(FieldSource.Api, "吉賽兒", "Gisele"), Field(FieldSource.Dictionary, "狐狸", "Fox"),
                                                           Field(FieldSource.Dictionary, "米勒", "Miller")), fields, false), fields);

            fields[1] = "老狐狸";
            fields[2] = " 米樂 ";
            IList<NameCorrection> corrections = session.Corrections(fields);

            Assert.Equal(2, corrections.Count);
            Assert.Equal(NameSpace.Nick, corrections[0].Space);
            Assert.Equal("fox", corrections[0].Key);
            Assert.Equal("老狐狸", corrections[0].Value);
            Assert.Equal(NameSpace.Name, corrections[1].Space);
            Assert.Equal("miller", corrections[1].Key);
            Assert.Equal("米樂", corrections[1].Value);
        }

        [Theory]
        [InlineData("吉賽兒")]        // kept as translated
        [InlineData("Gisele")]       // put back to English
        [InlineData("")]             // cleared
        [InlineData("吉賽兒Z")]       // a capital the source lacks: fails the sanitizer
        public void NonCorrectionsAreNotLearned(string final)
        {
            NameDialogSession session = Session();
            string[] fields = { "Gisele", "Fox", "Miller" };
            Apply(session, session.BeginTranslation(Result(Field(FieldSource.Api, "吉賽兒", "Gisele"), Field(FieldSource.Unchanged, null, "Fox"),
                                                           Field(FieldSource.Unchanged, null, "Miller")), fields, false), fields);
            fields[0] = final;
            Assert.Empty(session.Corrections(fields));
        }

        [Theory]
        [InlineData(FieldSource.SpecialForm, "阿B", "B")]
        [InlineData(FieldSource.Alias, "米勒", "Miller")]
        public void SpecialFormsAndAliasesAreNotLearned(FieldSource source, string value, string sourceText)
        {
            NameDialogSession session = Session();
            string[] fields = { "Gisele", sourceText, "Miller" };
            Apply(session, session.BeginTranslation(Result(Field(FieldSource.Unchanged, null, "Gisele"), Field(source, value, sourceText),
                                                           Field(FieldSource.Unchanged, null, "Miller")), fields, false), fields);
            fields[1] = "麥克";
            Assert.Empty(session.Corrections(fields));
        }

        [Fact]
        public void RerolledFieldIsNoLongerACorrection()
        {
            NameDialogSession session = Session();
            string[] fields = { "Gisele", "Fox", "Miller" };
            Apply(session, session.BeginTranslation(Result(Field(FieldSource.Unchanged, null, "Gisele"), Field(FieldSource.Unchanged, null, "Fox"),
                                                           Field(FieldSource.Dictionary, "米勒", "Miller")), fields, false), fields);

            fields[2] = "Smith"; // randomized
            session.Observe(fields);
            fields[2] = "史密斯"; // then typed by hand: says nothing about Miller
            Assert.Empty(session.Corrections(fields));
        }

        [Fact]
        public void RestoreForgetsTheTranslation()
        {
            NameDialogSession session = Session();
            string[] fields = { "Gisele", "Fox", "Miller" };
            Apply(session, session.BeginTranslation(Result(Field(FieldSource.Unchanged, null, "Gisele"), Field(FieldSource.Unchanged, null, "Fox"),
                                                           Field(FieldSource.Dictionary, "米勒", "Miller")), fields, false), fields);
            Apply(session, session.Restore(), fields);

            fields[2] = "米樂";
            Assert.Empty(session.Corrections(fields));
        }

        [Fact]
        public void SuggestedNameIsAChoiceNotACorrection()
        {
            NameDialogSession session = Session();
            string[] fields = { "Gisele", "Fox", "Miller" };
            Apply(session, session.BeginTranslation(Result(Field(FieldSource.Unchanged, null, "Gisele"), Field(FieldSource.Unchanged, null, "Fox"),
                                                           Field(FieldSource.Dictionary, "米勒", "Miller")), fields, false), fields);

            fields[2] = "史密斯";
            IList<NameCorrection> corrections = session.Corrections(fields, f => f == NameField.Last ? new List<string> { "史密斯" } : null);
            Assert.Empty(corrections);
        }

        [Fact]
        public void CorrectionIsSanitizedAndKeyedByTheNormalizedSource()
        {
            var session = new NameDialogSession("  JEAN-LUC ", "", "Miller", NickSource.Explicit);
            string[] fields = { "  JEAN-LUC ", "", "Miller" };
            Apply(session, session.BeginTranslation(Result(Field(FieldSource.Api, "尚呂克", "JEAN-LUC"), Field(FieldSource.Unchanged, null, ""),
                                                           Field(FieldSource.Unchanged, null, "Miller")), fields, false), fields);

            fields[0] = "尚-路克"; // punctuation is dropped, as for any other value
            NameCorrection correction = Assert.Single(session.Corrections(fields));
            Assert.Equal("jean-luc", correction.Key);
            Assert.Equal("尚路克", correction.Value);
        }

        [Fact]
        public void WriteWithoutSourceTextIsNotLearnable()
        {
            Assert.False(new FieldWrite("吉賽兒", "  ", learnable: true).Learnable);
            Assert.Throws<ArgumentNullException>(() => new FieldWrite(null, "Gisele", true));
        }
    }
}
