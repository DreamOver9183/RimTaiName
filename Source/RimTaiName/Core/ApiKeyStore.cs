using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RimTaiName.Core
{
    public enum KeyStorageMode
    {
        /// <summary>Saved in the config folder, encrypted with Windows DPAPI for the current user.</summary>
        Dpapi,

        /// <summary>Kept in memory only and forgotten when the game closes.</summary>
        SessionOnly,
    }

    public enum KeySaveResult
    {
        Ok,
        Empty,
        InvalidCharacters,
        TooShort,
        TooLong,
        WriteFailed,
    }

    /// <summary>
    /// Holds the player's Gemini API key outside the mod settings XML. The plain key is readable only through
    /// <see cref="Read"/> (passed to the Gemini client) and <see cref="Redact"/>; see CLAUDE.md "API Key 規則".
    /// </summary>
    public static class ApiKeyStore
    {
        private const string FileName = "RimTaiName_ApiKey.dat";

        /// <summary>Real keys are 39 ("AIza…") or 53 ("AQ.…") characters; anything this short is a typo.</summary>
        private const int MinKeyLength = 20;

        private const int MaxKeyLength = 256;

        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RimTaiName.ApiKey.v1");

        private static readonly object Gate = new object();

        private static string filePath;

        private static volatile string key;

        /// <summary>DPAPI on Windows; elsewhere Mono silently falls back to a weaker managed scheme, so never persist.</summary>
        public static KeyStorageMode Mode { get; private set; } = KeyStorageMode.SessionOnly;

        /// <summary>Why the saved key could not be loaded or saved (exception type and message), or null.</summary>
        public static string LastError { get; private set; }

        public static bool HasKey => key != null;

        /// <summary>Masked form for display, e.g. "AQ.…wxyz"; empty when no key is set.</summary>
        public static string MaskedHint => KeyRedactor.Mask(key);

        public static void Initialize(string configFolder)
        {
            lock (Gate)
            {
                LastError = null;
                key = null;
                if (string.IsNullOrEmpty(configFolder))
                {
                    filePath = null;
                    Mode = KeyStorageMode.SessionOnly;
                    LastError = "config folder unavailable";
                    return;
                }
                filePath = Path.Combine(configFolder, FileName);
                Mode = Environment.OSVersion.Platform == PlatformID.Win32NT ? KeyStorageMode.Dpapi : KeyStorageMode.SessionOnly;
                if (Mode == KeyStorageMode.Dpapi && File.Exists(filePath))
                {
                    try
                    {
                        byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(filePath), Entropy, DataProtectionScope.CurrentUser);
                        string loaded = Encoding.UTF8.GetString(plain);
                        key = Validate(loaded) == KeySaveResult.Ok ? loaded : null;
                    }
                    catch (Exception e)
                    {
                        // e.g. CryptographicException when the file was copied from another PC or user account.
                        LastError = Describe(e, null);
                    }
                }
            }
        }

        public static KeySaveResult Save(string input)
        {
            string candidate = input?.Trim();
            KeySaveResult validation = Validate(candidate);
            if (validation != KeySaveResult.Ok)
                return validation;
            lock (Gate)
            {
                if (Mode == KeyStorageMode.Dpapi && filePath != null)
                {
                    try
                    {
                        byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(candidate), Entropy, DataProtectionScope.CurrentUser);
                        WriteAtomically(filePath, cipher);
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        LastError = Describe(e, candidate);
                        return KeySaveResult.WriteFailed;
                    }
                    catch (Exception e)
                    {
                        // DPAPI itself is unusable in this runtime: keep the key for this session only.
                        Mode = KeyStorageMode.SessionOnly;
                        LastError = Describe(e, candidate);
                        key = candidate;
                        return KeySaveResult.Ok;
                    }
                }
                key = candidate;
                LastError = null;
                return KeySaveResult.Ok;
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                key = null;
                LastError = null;
                if (filePath == null)
                    return;
                try
                {
                    DeleteIfExists(filePath);
                    DeleteIfExists(filePath + ".tmp");
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    LastError = Describe(e, null);
                }
            }
        }

        /// <summary>Masks the current key and any key-shaped text. Used by <see cref="ModLog"/> for every message.</summary>
        public static string Redact(string text) => KeyRedactor.Redact(text, key);

        /// <summary>The plain key, or null. Only the Gemini client (via an injected delegate) may call this.</summary>
        internal static string Read() => key;

        /// <summary>
        /// Printable ASCII only (keys go into an HTTP header), 20–256 characters. No prefix check: Google has
        /// changed the key format before.
        /// </summary>
        private static KeySaveResult Validate(string candidate)
        {
            if (string.IsNullOrEmpty(candidate))
                return KeySaveResult.Empty;
            foreach (char c in candidate)
            {
                if (c < '!' || c > '~')
                    return KeySaveResult.InvalidCharacters;
            }
            if (candidate.Length < MinKeyLength)
                return KeySaveResult.TooShort;
            return candidate.Length > MaxKeyLength ? KeySaveResult.TooLong : KeySaveResult.Ok;
        }

        /// <summary>Writes a temp file, then swaps it in with File.Replace so the old key survives a failed write.</summary>
        private static void WriteAtomically(string path, byte[] data)
        {
            string temp = path + ".tmp";
            try
            {
                File.WriteAllBytes(temp, data);
                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);
            }
            finally
            {
                DeleteIfExists(temp);
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        private static string Describe(Exception e, string candidate) =>
            e.GetType().Name + ": " + KeyRedactor.Redact(KeyRedactor.Redact(e.Message, candidate), key);
    }
}
