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
                filePath = Path.Combine(configFolder, FileName);
                Mode = Environment.OSVersion.Platform == PlatformID.Win32NT ? KeyStorageMode.Dpapi : KeyStorageMode.SessionOnly;
                LastError = null;
                key = null;
                if (Mode == KeyStorageMode.Dpapi && File.Exists(filePath))
                {
                    try
                    {
                        byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(filePath), Entropy, DataProtectionScope.CurrentUser);
                        key = Encoding.UTF8.GetString(plain);
                    }
                    catch (Exception e)
                    {
                        // e.g. CryptographicException when the file was copied from another PC or user account.
                        LastError = Describe(e);
                    }
                }
            }
        }

        public static KeySaveResult Save(string input)
        {
            string candidate = input?.Trim();
            if (string.IsNullOrEmpty(candidate))
                return KeySaveResult.Empty;
            if (candidate.Length > MaxKeyLength)
                return KeySaveResult.TooLong;
            foreach (char c in candidate)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                    return KeySaveResult.InvalidCharacters;
            }
            lock (Gate)
            {
                if (Mode == KeyStorageMode.Dpapi && filePath != null)
                {
                    try
                    {
                        byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(candidate), Entropy, DataProtectionScope.CurrentUser);
                        WriteAtomically(filePath, cipher);
                    }
                    catch (IOException e)
                    {
                        LastError = Describe(e);
                        return KeySaveResult.WriteFailed;
                    }
                    catch (UnauthorizedAccessException e)
                    {
                        LastError = Describe(e);
                        return KeySaveResult.WriteFailed;
                    }
                    catch (Exception e)
                    {
                        // DPAPI itself is unusable in this runtime: keep the key for this session only.
                        Mode = KeyStorageMode.SessionOnly;
                        LastError = Describe(e);
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
                try
                {
                    if (filePath != null && File.Exists(filePath))
                        File.Delete(filePath);
                }
                catch (Exception e)
                {
                    LastError = Describe(e);
                }
            }
        }

        /// <summary>Masks the current key and any key-shaped text. Used by <see cref="ModLog"/> for every message.</summary>
        public static string Redact(string text) => KeyRedactor.Redact(text, key);

        /// <summary>The plain key, or null. Only the Gemini client (via an injected delegate) may call this.</summary>
        internal static string Read() => key;

        private static void WriteAtomically(string path, byte[] data)
        {
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, data);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
        }

        private static string Describe(Exception e) => e.GetType().Name + ": " + Redact(e.Message);
    }
}
