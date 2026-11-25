using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace KosPlay.VisualStudio.Editors.Integration
{
    public sealed class IntegrationLog : IReadOnlyList<IntegrationLogEntry>
    {
        private readonly List<IntegrationLogEntry> entries = new List<IntegrationLogEntry>();

        public int Count => entries.Count;
        public IntegrationLogEntry this[int index] => entries[index];
        public bool Succeeded => entries.All(entry => entry.Result);

        internal void Add(string path, PatchResult result) =>
            entries.Add(new IntegrationLogEntry(path, result));

        public static implicit operator bool(IntegrationLog log) => log != null && log.Succeeded;

        public IEnumerator<IntegrationLogEntry> GetEnumerator() => entries.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => string.Join(Environment.NewLine,
            entries.Select(entry => entry.FilePath + ": " + entry.Result));
    }

    public sealed class IntegrationLogEntry
    {
        public string FilePath { get; }
        public PatchResult Result { get; }

        public IntegrationLogEntry(string filePath, PatchResult result)
        {
            FilePath = filePath;
            Result = result;
        }
    }
}
