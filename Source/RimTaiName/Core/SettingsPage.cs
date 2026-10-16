using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RimTaiName.Llm;
using RimTaiName.Pipeline;
using UnityEngine;
using Verse;

namespace RimTaiName.Core
{
    /// <summary>
    /// The mod settings page. Drawn on the main thread; network requests run as <see cref="UiJob{T}"/>s behind the
    /// shared <see cref="RequestGate"/>.
    /// </summary>
    internal static class SettingsPage
    {
        private const float RowHeight = 30f;

        private const float Gap = 6f;

        private const float ButtonWidth = 120f;

        private const int KeyFieldMaxLength = 256;

        private const int DevFieldMaxLength = 40;

        /// <summary>Room for a pasted "models/" prefix; anything longer is rejected by the text field.</summary>
        private const int ModelFieldMaxLength = ModelId.MaxLength + 16;

        /// <summary>Model ids are ASCII; refusing everything else also keeps surrogate pairs out of the field.</summary>
        private static readonly Regex PrintableAscii = new Regex(@"^[\x20-\x7E]*$");

        /// <summary>Connection test.</summary>
        private static readonly UiJob<GeminiResult> Check = new UiJob<GeminiResult>();

        /// <summary>Developer translation test through the whole pipeline.</summary>
        private static readonly UiJob<NameResult> DevTranslate = new UiJob<NameResult>();

        private static Vector2 scroll;

        private static float viewHeight = 1000f;

        /// <summary>What the player typed; cleared when the page closes. Never logged.</summary>
        private static string keyInput = string.Empty;

        private static bool revealKey;

        private static string feedback;

        private static bool feedbackIsError;

        private static string modelBuffer;

        private static string devFeedback;

        // Invented names that are not in the dictionary, so the test reaches Gemini.
        private static readonly string[] DevInput = { "Zorbalith", "Quickfang", "Vexmoor" };

        public static void Draw(Rect inRect)
        {
            RimTaiNameSettings settings = RimTaiNameMod.Settings;
            if (modelBuffer == null)
                modelBuffer = settings.modelId;
            var view = new Rect(0f, 0f, inRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(inRect, ref scroll, view);
            // Listing starts a new column off to the right whenever content outgrows its rect. With the view
            // height taken from the previous frame, one taller frame would collapse the page for good, so the
            // listing must stay a single column and let the scroll view grow instead.
            var list = new Listing_Standard { maxOneColumn = true };
            list.Begin(view);
            try
            {
                // Each section is isolated: one failing section is logged once and the rest still draws.
                DrawSection("key", 0x52544e10, () => DrawKeySection(list, settings));
                DrawSection("model", 0x52544e11, () => DrawModelSection(list, settings));
                DrawSection("cache", 0x52544e12, () => DrawCacheSection(list, RimTaiNameMod.Cache));
                DrawSection("privacy", 0x52544e13, () => DrawPrivacySection(list));
                if (Prefs.DevMode)
                    DrawSection("dev", 0x52544e14, () => DrawDevSection(list, settings));
                viewHeight = Mathf.Max(list.CurHeight + 20f, 100f);
            }
            finally
            {
                // Always unwind the GUI groups, or IMGUI clipping breaks for the whole window.
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                list.End();
                Widgets.EndScrollView();
            }
        }

        /// <summary>Called when the settings window closes: forget typed text, re-mask the key, stop running requests.</summary>
        public static void OnClosed()
        {
            keyInput = string.Empty;
            revealKey = false;
            feedback = null;
            devFeedback = null;
            modelBuffer = null;
            Check.Reset();
            DevTranslate.Reset();
        }

        private static void DrawSection(string name, int logKey, Action draw)
        {
            Color color = GUI.color;
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            try
            {
                draw();
            }
            catch (Exception e)
            {
                ModLog.ErrorOnce($"settings section '{name}' failed: {e}", logKey);
            }
            finally
            {
                // A section that throws mid-way must not leak its colour, font or alignment into the next one.
                GUI.color = color;
                Text.Font = font;
                Text.Anchor = anchor;
            }
        }

        private static void Heading(Listing_Standard list, string key)
        {
            list.Gap(8f);
            Text.Font = GameFont.Medium;
            list.Label(key.Translate());
            Text.Font = GameFont.Small;
            list.GapLine(6f);
        }

        private static void ColoredLabel(Listing_Standard list, string text, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            list.Label(text);
            GUI.color = previous;
        }

        private static void DrawKeySection(Listing_Standard list, RimTaiNameSettings settings)
        {
            Heading(list, "RimTaiName_Settings_KeyHeading");

            if (ApiKeyStore.HasKey)
                list.Label("RimTaiName_Settings_KeySaved".Translate(ApiKeyStore.MaskedHint));
            else
                list.Label("RimTaiName_Settings_KeyNone".Translate());
            ColoredLabel(list, ApiKeyStore.Mode == KeyStorageMode.Dpapi
                    ? "RimTaiName_Settings_StorageDpapi".Translate()
                    : "RimTaiName_Settings_StorageSession".Translate(),
                Color.gray);
            if (ApiKeyStore.LastError != null)
                ColoredLabel(list, "RimTaiName_Settings_KeyError".Translate(ApiKeyStore.LastError), ColorLibrary.RedReadable);

            Rect row = list.GetRect(RowHeight);
            float fieldWidth = row.width - 4f * (ButtonWidth + Gap);
            var field = new Rect(row.x, row.y, fieldWidth, RowHeight);
            keyInput = revealKey
                ? GUI.TextField(field, keyInput ?? string.Empty, KeyFieldMaxLength, Text.CurTextFieldStyle)
                : GUI.PasswordField(field, keyInput ?? string.Empty, '*', KeyFieldMaxLength, Text.CurTextFieldStyle);
            float x = field.xMax + Gap;
            if (Widgets.ButtonText(new Rect(x, row.y, ButtonWidth, RowHeight), "RimTaiName_Settings_KeySave".Translate()))
                ReportSave(ApiKeyStore.Save(keyInput), clearInput: true);
            x += ButtonWidth + Gap;
            if (Widgets.ButtonText(new Rect(x, row.y, ButtonWidth, RowHeight), "RimTaiName_Settings_KeyPaste".Translate()))
                ReportSave(ApiKeyStore.Save(GUIUtility.systemCopyBuffer), clearInput: false);
            x += ButtonWidth + Gap;
            if (Widgets.ButtonText(new Rect(x, row.y, ButtonWidth, RowHeight),
                    (revealKey ? "RimTaiName_Settings_KeyHide" : "RimTaiName_Settings_KeyShow").Translate()))
                ToggleReveal();
            x += ButtonWidth + Gap;
            if (Widgets.ButtonText(new Rect(x, row.y, ButtonWidth, RowHeight), "RimTaiName_Settings_KeyClear".Translate()))
            {
                ApiKeyStore.Clear();
                keyInput = string.Empty;
                Check.Reset();
                SetFeedback("RimTaiName_Settings_KeyCleared".Translate(), false);
            }
            if (feedback != null)
                ColoredLabel(list, feedback, feedbackIsError ? ColorLibrary.RedReadable : Color.white);

            list.Gap(4f);
            Rect testRow = list.GetRect(RowHeight);
            bool canTest = !Check.Running && ApiKeyStore.HasKey;
            if (Widgets.ButtonText(new Rect(testRow.x, testRow.y, ButtonWidth * 1.5f, RowHeight),
                    Check.Running ? "RimTaiName_Settings_Testing".Translate() : "RimTaiName_Settings_Test".Translate(),
                    active: canTest))
                StartCheck(settings.modelId);
            Widgets.Label(new Rect(testRow.x + ButtonWidth * 1.5f + Gap, testRow.y + 4f, testRow.width - ButtonWidth * 1.5f - Gap, RowHeight),
                "RimTaiName_Settings_TestNote".Translate());
            GeminiResult result = Check.Result;
            if (result != null)
            {
                ColoredLabel(list, GeminiMessages.Describe(result), result.Success ? ColorLibrary.Green : ColorLibrary.RedReadable);
                if (Check.TakeNewResult())
                    ModLog.Message($"connection test: {result.Error} (HTTP {result.StatusCode}) {result.Detail}");
            }
        }

        private static void DrawModelSection(Listing_Standard list, RimTaiNameSettings settings)
        {
            Heading(list, "RimTaiName_Settings_ModelHeading");
            Rect row = list.GetRect(RowHeight);
            const float labelWidth = 160f;
            Widgets.Label(new Rect(row.x, row.y + 4f, labelWidth, RowHeight), "RimTaiName_Settings_ModelId".Translate());
            var field = new Rect(row.x + labelWidth, row.y, row.width - labelWidth - ButtonWidth - Gap, RowHeight);
            modelBuffer = Widgets.TextField(field, modelBuffer ?? string.Empty, ModelFieldMaxLength, PrintableAscii);
            if (Widgets.ButtonText(new Rect(field.xMax + Gap, row.y, ButtonWidth, RowHeight), "RimTaiName_Settings_ModelDefault".Translate()))
                modelBuffer = ModelId.Default;
            if (ModelId.TryNormalize(modelBuffer, out string normalized))
                settings.modelId = normalized;
            else
                ColoredLabel(list, "RimTaiName_Settings_ModelInvalid".Translate(settings.modelId), ColorLibrary.RedReadable);

            list.Gap(4f);
            float interval = list.SliderLabeled(
                "RimTaiName_Settings_MinInterval".Translate(settings.minIntervalSeconds.ToString("0")),
                settings.minIntervalSeconds, RimTaiNameSettings.MinInterval, RimTaiNameSettings.MaxInterval, 0.5f,
                "RimTaiName_Settings_MinIntervalTip".Translate());
            settings.minIntervalSeconds = Mathf.Round(interval);
            list.CheckboxLabeled("RimTaiName_Settings_KeepAddress".Translate(), ref settings.keepVanillaAddress,
                "RimTaiName_Settings_KeepAddressTip".Translate());
        }

        private static void DrawCacheSection(Listing_Standard list, PersonalNameCache cache)
        {
            Heading(list, "RimTaiName_Settings_CacheHeading");
            list.Label("RimTaiName_Settings_CacheCount".Translate(cache.Count));
            if (cache.LastError != null)
                ColoredLabel(list, "RimTaiName_Settings_CacheError".Translate(cache.LastError), ColorLibrary.RedReadable);
            Rect row = list.GetRect(RowHeight);
            if (Widgets.ButtonText(new Rect(row.x, row.y, ButtonWidth * 1.5f, RowHeight), "RimTaiName_Settings_CacheClear".Translate(),
                    active: cache.Count > 0))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "RimTaiName_Settings_CacheClearConfirm".Translate(cache.Count),
                    () =>
                    {
                        cache.Clear();
                        if (!cache.SaveIfDirty())
                            ModLog.Warning("personal cache could not be saved after clearing: " + cache.LastError);
                    },
                    destructive: true));
            }
            if (Widgets.ButtonText(new Rect(row.x + ButtonWidth * 1.5f + Gap, row.y, ButtonWidth * 1.5f, RowHeight),
                    "RimTaiName_Settings_CacheOpenFolder".Translate()))
                Application.OpenURL(GenFilePaths.ConfigFolderPath);
        }

        private static void DrawPrivacySection(Listing_Standard list)
        {
            Heading(list, "RimTaiName_Settings_PrivacyHeading");
            list.Label("RimTaiName_Settings_PrivacyText".Translate());
        }

        /// <summary>Developer mode only: run three names through the whole pipeline (Step 3 verification).</summary>
        private static void DrawDevSection(Listing_Standard list, RimTaiNameSettings settings)
        {
            Heading(list, "RimTaiName_Settings_DevHeading");
            BuiltInDictionary dictionary = RimTaiNameMod.Dictionary;
            list.Label("RimTaiName_Settings_DevStatus".Translate(
                dictionary.Count,
                (RimTaiNameMod.Prompts.HasSharedRules ? "RimTaiName_Settings_DevRulesOk" : "RimTaiName_Settings_DevRulesMissing").Translate()));
            if (dictionary.LoadError != null)
                ColoredLabel(list, "RimTaiName_Settings_DevDictError".Translate(dictionary.LoadError), ColorLibrary.RedReadable);

            Rect row = list.GetRect(RowHeight);
            float fieldWidth = (row.width - ButtonWidth * 1.5f - 3f * Gap) / 3f;
            for (int i = 0; i < NameFields.Count; i++)
            {
                var field = new Rect(row.x + i * (fieldWidth + Gap), row.y, fieldWidth, RowHeight);
                DevInput[i] = Widgets.TextField(field, DevInput[i] ?? string.Empty, DevFieldMaxLength);
            }
            if (Widgets.ButtonText(new Rect(row.xMax - ButtonWidth * 1.5f, row.y, ButtonWidth * 1.5f, RowHeight),
                    DevTranslate.Running ? "RimTaiName_Settings_Testing".Translate() : "RimTaiName_Settings_DevTranslate".Translate(),
                    active: !DevTranslate.Running))
                StartDevTranslate(settings);
            ColoredLabel(list, "RimTaiName_Settings_DevNote".Translate(), Color.gray);
            if (devFeedback != null)
                ColoredLabel(list, devFeedback, ColorLibrary.RedReadable);

            NameResult result = DevTranslate.Result;
            if (result == null)
                return;
            foreach (NameField field in NameFields.All)
            {
                FieldResult f = result[field];
                string shown = f.HasNewValue ? f.Value : "-";
                list.Label("RimTaiName_Settings_DevField".Translate(
                    ("RimTaiName_Field_" + field).Translate(), f.SourceText ?? string.Empty, shown, ("RimTaiName_Source_" + f.Source).Translate()));
            }
            if (result.RemoteError != GeminiErrorKind.None)
                ColoredLabel(list, GeminiMessages.Describe(result.RemoteError, result.RemoteStatusCode, result.RemoteDetail), ColorLibrary.RedReadable);
            if (DevTranslate.TakeNewResult())
            {
                if (!RimTaiNameMod.Cache.SaveIfDirty())
                    ModLog.Warning("personal cache could not be saved: " + RimTaiNameMod.Cache.LastError);
                ModLog.Message($"dev translate: {result[NameField.First].Source}/{result[NameField.Nick].Source}/{result[NameField.Last].Source}, " +
                               $"remote {result.RemoteError} (HTTP {result.RemoteStatusCode})");
            }
        }

        private static void ToggleReveal()
        {
            revealKey = !revealKey;
            if (revealKey && string.IsNullOrEmpty(keyInput) && ApiKeyStore.HasKey)
                keyInput = ApiKeyStore.Read(); // the only place the UI shows the plain key, and only on request
            else if (!revealKey && keyInput == ApiKeyStore.Read())
                keyInput = string.Empty;
        }

        private static void ReportSave(KeySaveResult result, bool clearInput)
        {
            if (result == KeySaveResult.Ok)
            {
                if (clearInput)
                    keyInput = string.Empty;
                revealKey = false;
                Check.Reset(); // a result for the previous key would be misleading
                SetFeedback("RimTaiName_Settings_KeySavedNow".Translate(ApiKeyStore.MaskedHint), false);
            }
            else
            {
                SetFeedback(("RimTaiName_Settings_KeySave" + result).Translate(), true);
            }
        }

        private static void SetFeedback(string text, bool isError)
        {
            feedback = text;
            feedbackIsError = isError;
        }

        /// <summary>Main thread. Logging happens when the result is first drawn, because Verse.Log is a game API.</summary>
        private static void StartCheck(string modelId)
        {
            IDisposable lease = RimTaiNameMod.TryEnterGate(out string refusal);
            if (lease == null)
            {
                SetFeedback(refusal, true);
                return;
            }
            GeminiInteractionsClient gemini = RimTaiNameMod.Gemini;
            Check.Start(token => gemini.CheckAsync(modelId, token), lease,
                        e => GeminiResult.Fail(GeminiErrorKind.Network, 0, e.GetType().Name));
        }

        /// <summary>Main thread: local resolution now, then (if needed and allowed) one Gemini request.</summary>
        private static void StartDevTranslate(RimTaiNameSettings settings)
        {
            devFeedback = null;
            string first = DevInput[0], nick = DevInput[1], last = DevInput[2];
            var payload = new NameRequestPayload(first, nick, last)
            {
                KeepVanillaAddress = settings.keepVanillaAddress,
                NickSource = NickAliasResolver.Determine(first, nick, last, !string.IsNullOrWhiteSpace(nick), nick),
            };
            NameProcessorService processor = RimTaiNameMod.Processor;
            NameResult local = processor.ResolveLocal(payload);
            IDisposable lease = null;
            if (local.HasPending)
            {
                if (!ApiKeyStore.HasKey)
                {
                    DevTranslate.Start(_ => Task.FromResult(WithError(local, GeminiErrorKind.NoKey)), null, _ => local);
                    return;
                }
                lease = RimTaiNameMod.TryEnterGate(out string refusal);
                if (lease == null)
                {
                    devFeedback = refusal;
                    return;
                }
            }
            GeminiNameTranslator translator = RimTaiNameMod.Translator;
            DevTranslate.Start(token => processor.ResolveRemoteAsync(payload, local, translator, token), lease,
                               e => WithError(local, GeminiErrorKind.InvalidResponse, e.GetType().Name));
        }

        private static NameResult WithError(NameResult local, GeminiErrorKind error, string detail = null) =>
            new NameResult(local[NameField.First], local[NameField.Nick], local[NameField.Last], error, 0, detail);
    }
}
