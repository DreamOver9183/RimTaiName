using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RimTaiName.Llm;
using RimTaiName.Pipeline;
using UnityEngine;
using Verse;

namespace RimTaiName.Core
{
    /// <summary>
    /// The mod settings page. Drawn on the main thread; the connection test runs on a worker thread and only
    /// hands a <see cref="GeminiResult"/> back through a volatile field.
    /// </summary>
    internal static class SettingsPage
    {
        private const float RowHeight = 30f;

        private const float Gap = 6f;

        private const float ButtonWidth = 120f;

        private const int KeyFieldMaxLength = 256;

        /// <summary>Room for a pasted "models/" prefix; anything longer is rejected by the text field.</summary>
        private const int ModelFieldMaxLength = ModelId.MaxLength + 16;

        /// <summary>Model ids are ASCII; refusing everything else also keeps surrogate pairs out of the field.</summary>
        private static readonly Regex PrintableAscii = new Regex(@"^[\x20-\x7E]*$");

        private static Vector2 scroll;

        private static float viewHeight = 1000f;

        /// <summary>What the player typed; cleared when the page closes. Never logged.</summary>
        private static string keyInput = string.Empty;

        private static bool revealKey;

        private static string feedback;

        private static bool feedbackIsError;

        private static string modelBuffer;

        private static volatile bool checking;

        private static volatile GeminiResult checkResult;

        private static bool checkLogged = true;

        /// <summary>Bumped whenever a test starts or is abandoned, so a late result from an old test is dropped.</summary>
        private static int checkGeneration;

        private static CancellationTokenSource checkCancellation;

        public static void Draw(Rect inRect, RimTaiNameSettings settings, PersonalNameCache cache, GeminiInteractionsClient gemini)
        {
            if (modelBuffer == null)
                modelBuffer = settings.modelId;
            var view = new Rect(0f, 0f, inRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(inRect, ref scroll, view);
            var list = new Listing_Standard();
            list.Begin(view);
            DrawKeySection(list, settings, gemini);
            DrawModelSection(list, settings);
            DrawCacheSection(list, cache);
            DrawPrivacySection(list);
            viewHeight = list.CurHeight + 20f;
            list.End();
            Widgets.EndScrollView();
        }

        /// <summary>Called when the settings window closes: forget typed text, re-mask the key, stop a running test.</summary>
        public static void OnClosed()
        {
            keyInput = string.Empty;
            revealKey = false;
            feedback = null;
            modelBuffer = null;
            ResetCheck();
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

        private static void DrawKeySection(Listing_Standard list, RimTaiNameSettings settings, GeminiInteractionsClient gemini)
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
                ResetCheck();
                SetFeedback("RimTaiName_Settings_KeyCleared".Translate(), false);
            }
            if (feedback != null)
                ColoredLabel(list, feedback, feedbackIsError ? ColorLibrary.RedReadable : Color.white);

            list.Gap(4f);
            Rect testRow = list.GetRect(RowHeight);
            bool canTest = !checking && ApiKeyStore.HasKey;
            if (Widgets.ButtonText(new Rect(testRow.x, testRow.y, ButtonWidth * 1.5f, RowHeight),
                    checking ? "RimTaiName_Settings_Testing".Translate() : "RimTaiName_Settings_Test".Translate(),
                    active: canTest))
                StartCheck(gemini, settings.modelId);
            Widgets.Label(new Rect(testRow.x + ButtonWidth * 1.5f + Gap, testRow.y + 4f, testRow.width - ButtonWidth * 1.5f - Gap, RowHeight),
                "RimTaiName_Settings_TestNote".Translate());
            GeminiResult result = checkResult;
            if (!checking && result != null)
            {
                ColoredLabel(list, GeminiMessages.Describe(result), result.Success ? ColorLibrary.Green : ColorLibrary.RedReadable);
                if (!checkLogged)
                {
                    checkLogged = true;
                    ModLog.Message($"connection test: {result.Error} (HTTP {result.StatusCode}) {result.Detail}");
                }
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
                ResetCheck(); // a result for the previous key would be misleading
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
        private static void StartCheck(GeminiInteractionsClient gemini, string modelId)
        {
            ResetCheck();
            int generation = checkGeneration;
            var cancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token;
            checkCancellation = cancellation;
            checking = true;
            checkLogged = false;
            Task.Run(async () =>
            {
                GeminiResult result;
                try
                {
                    result = await gemini.CheckAsync(modelId, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return; // abandoned by ResetCheck
                }
                catch (Exception e)
                {
                    result = GeminiResult.Fail(GeminiErrorKind.Network, 0, e.GetType().Name);
                }
                if (Volatile.Read(ref checkGeneration) != generation)
                    return;
                checkResult = result;
                checking = false;
            });
        }

        /// <summary>Main thread: cancels a running test and forgets its result (key changed, page closed).</summary>
        private static void ResetCheck()
        {
            Interlocked.Increment(ref checkGeneration);
            CancellationTokenSource running = checkCancellation;
            checkCancellation = null;
            if (running != null)
            {
                running.Cancel();
                running.Dispose();
            }
            checking = false;
            checkResult = null;
            checkLogged = true;
        }
    }
}
