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

        private readonly object gate = new object();

        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        private readonly string path;

        private bool dirty;

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
        /// Reads the file. A missing file means an empty cache; an unreadable one is renamed to *.corrupt so the
        /// next save does not silently destroy it, and the cache starts empty.
        /// </summary>
        public void Load()
        {
            lock (gate)
            {
                entries.Clear();
                dirty = false;
                LastError = null;
                if (!File.Exists(path))
                    return;
                try
                {
                    XElement root = XDocument.Load(path).Root;
                    if (root == null || root.Name.LocalName != RootName)
                        throw new XmlException("unexpected root element");
                    foreach (XElement e in root.Elements(EntryName))
                    {
                        string key = (string)e.Attribute("key");
                        string value = (string)e.Attribute("value");
                        if (!NameSpaces.TryParse((string)e.Attribute("ns"), out NameSpace ns) ||
                            string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                            continue;
                        CacheOrigin origin = (string)e.Attribute("origin") == "user" ? CacheOrigin.User : CacheOrigin.Api;
                        entries[Compose(ns, key)] = new Entry(ns, key, value, origin);
                    }
                }
                catch (Exception e) when (e is XmlException || e is IOException || e is UnauthorizedAccessException)
                {
                    entries.Clear();
                    LastError = e.GetType().Name + ": " + e.Message;
                    TryQuarantine();
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
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value) || !IsXmlSafe(key) || !IsXmlSafe(value))
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
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                lock (gate)
                {
                    dirty = true;
                    LastError = e.GetType().Name + ": " + e.Message;
                }
                return false;
            }
            finally
            {
                TryDelete(temp);
            }
        }

        private static string Compose(NameSpace ns, string key) => NameSpaces.Tag(ns) + ":" + key;

        private static bool IsXmlSafe(string text)
        {
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
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // Keep LastError from the load; the next save will overwrite the unreadable file.
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
