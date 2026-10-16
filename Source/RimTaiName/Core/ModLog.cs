using Verse;

namespace RimTaiName.Core
{
    /// <summary>The only way the mod writes to the game log; every message is redacted first.</summary>
    internal static class ModLog
    {
        private const string Prefix = "[RimTaiName] ";

        public static void Message(string text) => Log.Message(Prefix + ApiKeyStore.Redact(text));

        public static void Warning(string text) => Log.Warning(Prefix + ApiKeyStore.Redact(text));

        public static void Error(string text) => Log.Error(Prefix + ApiKeyStore.Redact(text));

        public static void ErrorOnce(string text, int key) => Log.ErrorOnce(Prefix + ApiKeyStore.Redact(text), key);
    }
}
