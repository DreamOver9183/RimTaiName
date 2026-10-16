using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace RimTaiName.Pipeline
{
    public enum CacheOrigin
    {
        /// <summary>Learned from a Gemini response.</summary>
        Api,

        /// <summary>The player's own correction; never overwritten by <see cref="Api"/>.</summary>
        User,
    }

    /// <summary>
    /// The player's translation cache (Config/RimTaiName_Cache.xml). Background threads may call
    /// <see cref="Put"/>; only the main thread should call <see cref="SaveIfDirty"/>. Keys must already be
    /// normalized by the caller.
    /// </summary>
    public sealed class PersonalNameCache
    {
        private const string RootName = "RimTaiNameCache";

        private const string EntryName = "entry";

        private const int FormatVersion = 1;

        private const int MaxTextLength = 256;

        private readonly object gate = new object();

        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        private readonly string path;

        private bool dirty;

        /// <summary>
        /// The file exists but could not be read (e.g. locked by a virus scanner). Saving must merge it first
        /// instead of overwriting it, or the player's earlier translations would be lost.
        /// </summary>
        private bool readBlocked;

        public PersonalNameCache(string path)
        {
            this.path = path ?? throw new ArgumentNullException(nameof(path));
        }

        /// <summary>Why the last load or save failed, or null.</summary>
        public string LastError { get; private set; }

        public int Count
        {
            get
            {
                lock (gate)
                    return entries.Count;
            }
        }

        public bool IsDirty
        {
            get
            {
                lock (gate)
                    return dirty;
            }
        }

        /// <summary>
        /// Reads the file. A missing file means an empty cache. A corrupt one is renamed to *.corrupt and the
        /// cache starts empty. A file that exists but cannot be read right now (locked, no access) is left alone
        /// and merged on the next save instead of being overwritten.
        /// </summary>
        public void Load()
        {
            lock (gate)
            {
                entries.Clear();
                dirty = false;
                LastError = null;
                readBlocked = false;
                if (!File.Exists(path))
                    return;
                try
                {
                    foreach (Entry entry in ReadFile())
                        Merge(entry);
                }
                catch (XmlException e)
                {
                    entries.Clear();
                    LastError = Describe(e);
                    TryQuarantine();
                }
                catch (Exception e) when (IsAccessProblem(e))
                {
                    entries.Clear();
                    LastError = Describe(e);
                    readBlocked = true;
                }
            }
        }

        public bool TryGet(NameSpace ns, string key, out string value, out CacheOrigin origin)
        {
            lock (gate)
            {
                if (key != null && entries.TryGetValue(Compose(ns, key), out Entry entry))
                {
                    value = entry.Value;
                    origin = entry.Origin;
                    return true;
                }
            }
            value = null;
            origin = CacheOrigin.Api;
            return false;
        }

        /// <summary>
        /// Stores a translation. An <see cref="CacheOrigin.Api"/> result never replaces a player's correction.
        /// Returns false when nothing changed or the text cannot be stored in XML.
        /// </summary>
        public bool Put(NameSpace ns, string key, string value, CacheOrigin origin)
        {
            if (!IsStorable(key) || !IsStorable(value))
                return false;
            lock (gate)
            {
                string composite = Compose(ns, key);
                if (entries.TryGetValue(composite, out Entry existing))
                {
                    if (existing.Origin == CacheOrigin.User && origin == CacheOrigin.Api)
                        return false;
                    if (existing.Value == value && existing.Origin == origin)
                        return false;
                }
                entries[composite] = new Entry(ns, key, value, origin);
                dirty = true;
                return true;
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                // An explicit clear may overwrite a file that could not be read.
                readBlocked = false;
                if (entries.Count == 0)
                    return;
                entries.Clear();
                dirty = true;
            }
        }

        /// <summary>Writes the file when something changed. Call from the main thread. Returns false on failure.</summary>
        public bool SaveIfDirty()
        {
            XDocument doc;
            lock (gate)
            {
                if (!dirty)
                    return true;
                if (readBlocked && !TryMergeBlockedFile())
                    return false;
                var root = new XElement(RootName, new XAttribute("version", FormatVersion));
                var sorted = new List<Entry>(entries.Values);
                sorted.Sort((a, b) => string.CompareOrdinal(Compose(a.Ns, a.Key), Compose(b.Ns, b.Key)));
                foreach (Entry entry in sorted)
                {
                    root.Add(new XElement(EntryName,
                        new XAttribute("ns", NameSpaces.Tag(entry.Ns)),
                        new XAttribute("key", entry.Key),
                        new XAttribute("value", entry.Value),
                        new XAttribute("origin", entry.Origin == CacheOrigin.User ? "user" : "api")));
                }
                doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
                dirty = false;
            }
            string temp = path + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                doc.Save(temp);
                // File.Replace keeps the previous cache intact if the swap fails.
                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);
                lock (gate)
                    LastError = null;
                return true;
            }
            catch (Exception e)
            {
                // Any failure keeps the changes pending; errors are reported, never thrown to the caller.
                lock (gate)
                {
                    dirty = true;
                    LastError = Describe(e);
                }
                return false;
            }
            finally
            {
                TryDelete(temp);
            }
        }

        private static string Compose(NameSpace ns, string key) => NameSpaces.Tag(ns) + ":" + key;

        private static string Describe(Exception e) => e.GetType().Name + ": " + e.Message;

        private static bool IsAccessProblem(Exception e) =>
            e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException;

        /// <summary>
        /// Non-blank, at most 256 characters, valid XML, and no tab/CR/LF: XML attribute normalization turns those
        /// into spaces on reload, so the key would never match again.
        /// </summary>
        private static bool IsStorable(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength || text.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0)
                return false;
            try
            {
                XmlConvert.VerifyXmlChars(text);
                return true;
            }
            catch (XmlException)
            {
                return false;
            }
        }

        /// <summary>Parses the cache file with DTDs prohibited (no entity expansion, no external resolution).</summary>
        /// <exception cref="XmlException">The file is corrupt or not a cache file.</exception>
        private List<Entry> ReadFile()
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            XElement root;
            using (XmlReader reader = XmlReader.Create(path, settings))
                root = XDocument.Load(reader).Root;
            if (root == null || root.Name.LocalName != RootName)
                throw new XmlException("unexpected root element");
            var result = new List<Entry>();
            foreach (XElement e in root.Elements(EntryName))
            {
                string key = (string)e.Attribute("key");
                string value = (string)e.Attribute("value");
                if (!NameSpaces.TryParse((string)e.Attribute("ns"), out NameSpace ns) || !IsStorable(key) || !IsStorable(value))
                    continue;
                CacheOrigin origin = (string)e.Attribute("origin") == "user" ? CacheOrigin.User : CacheOrigin.Api;
                result.Add(new Entry(ns, key, value, origin));
            }
            return result;
        }

        /// <summary>Adds an entry unless that would let an API result replace a player's correction.</summary>
        private void Merge(Entry entry)
        {
            string composite = Compose(entry.Ns, entry.Key);
            if (entries.TryGetValue(composite, out Entry existing) && existing.Origin == CacheOrigin.User && entry.Origin == CacheOrigin.Api)
                return;
            entries[composite] = entry;
        }

        /// <summary>Called under the lock before saving: folds in the file that was unreadable at load time.</summary>
        private bool TryMergeBlockedFile()
        {
            try
            {
                if (File.Exists(path))
                {
                    foreach (Entry onDisk in ReadFile())
                    {
                        string composite = Compose(onDisk.Ns, onDisk.Key);
                        // Entries changed this session win, except that a player's correction is never lost.
                        if (!entries.TryGetValue(composite, out Entry current) ||
                            (current.Origin == CacheOrigin.Api && onDisk.Origin == CacheOrigin.User))
                            entries[composite] = onDisk;
                    }
                }
                readBlocked = false;
                return true;
            }
            catch (XmlException e)
            {
                // It turned out to be corrupt after all: set it aside, then save what this session has.
                LastError = Describe(e);
                TryQuarantine();
                readBlocked = false;
                return true;
            }
            catch (Exception e) when (IsAccessProblem(e))
            {
                LastError = "existing cache file still unreadable, not overwriting it: " + Describe(e);
                return false;
            }
        }

        private static void TryDelete(string file)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // A stale temp file is harmless; the next save overwrites it.
            }
        }

        private void TryQuarantine()
        {
            try
            {
                string corrupt = path + ".corrupt";
                if (File.Exists(corrupt))
                    File.Delete(corrupt);
                File.Move(path, corrupt);
            }
            catch (Exception e) when (IsAccessProblem(e))
            {
                // Keep LastError from the load; the next save will overwrite the corrupt file.
            }
        }

        private sealed class Entry
        {
            public Entry(NameSpace ns, string key, string value, CacheOrigin origin)
            {
                Ns = ns;
                Key = key;
                Value = value;
                Origin = origin;
            }

            public NameSpace Ns { get; }

            public string Key { get; }

            public string Value { get; }

            public CacheOrigin Origin { get; }
        }
    }
}
