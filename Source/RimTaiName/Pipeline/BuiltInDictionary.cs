using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace RimTaiName.Pipeline
{
    /// <summary>
    /// The shipped vanilla-name dictionary (Dictionary/BaseNames_zhTW.tsv: <c>namespace \t key \t zh_tw</c>).
    /// Loaded once on first use and read-only afterwards, so lookups are safe from any thread. A missing or
    /// unreadable file yields an empty dictionary and <see cref="LoadError"/>; translation then falls back to
    /// the personal cache and Gemini.
    /// </summary>
    public sealed class BuiltInDictionary
    {
        public const string Header = "namespace\tkey\tzh_tw";

        private readonly string path;

        private readonly Lazy<Dictionary<string, string>> entries;

        private string loadError;

        private int skippedLines;

        public BuiltInDictionary(string path)
        {
            this.path = path ?? throw new ArgumentNullException(nameof(path));
            entries = new Lazy<Dictionary<string, string>>(Load, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public bool IsLoaded => entries.IsValueCreated;

        public int Count => entries.Value.Count;

        /// <summary>Why the file could not be read, or null. Forces the load.</summary>
        public string LoadError
        {
            get
            {
                EnsureLoaded();
                return loadError;
            }
        }

        /// <summary>Malformed or duplicate lines that were ignored. Forces the load.</summary>
        public int SkippedLines
        {
            get
            {
                EnsureLoaded();
                return skippedLines;
            }
        }

        public void EnsureLoaded() => _ = entries.Value;

        /// <summary>Looks up a key already normalized with <see cref="NameKey.Normalize"/>.</summary>
        public bool TryGet(NameSpace ns, string key, out string value)
        {
            value = null;
            return !string.IsNullOrEmpty(key) && entries.Value.TryGetValue(Compose(ns, key), out value);
        }

        private static string Compose(NameSpace ns, string key) => NameSpaces.Tag(ns) + ":" + key;

        private Dictionary<string, string> Load()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using (var reader = new StreamReader(path, new UTF8Encoding(false), true))
                {
                    string line;
                    bool first = true;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (first)
                        {
                            first = false;
                            if (line == Header)
                                continue;
                        }
                        if (line.Length == 0)
                            continue;
                        string[] fields = line.Split('\t');
                        if (fields.Length != 3 || !NameSpaces.TryParse(fields[0], out NameSpace ns) ||
                            fields[1].Length == 0 || fields[2].Length == 0)
                        {
                            skippedLines++;
                            continue;
                        }
                        // Normalize again so a hand-edited key still matches lookups.
                        string composite = Compose(ns, NameKey.Normalize(fields[1]));
                        if (map.ContainsKey(composite))
                            skippedLines++;
                        else
                            map.Add(composite, fields[2]);
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
            {
                loadError = e.GetType().Name + ": " + e.Message;
                map.Clear();
            }
            return map;
        }
    }
}
