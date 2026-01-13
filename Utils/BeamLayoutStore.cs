using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitAgent.Utils
{
    internal sealed class BeamLayoutData
    {
        public List<ElementId> MainBeamCurveIds { get; } = new List<ElementId>();
        public List<ElementId> SecondaryBeamCurveIds { get; } = new List<ElementId>();
    }

    internal static class BeamLayoutStore
    {
        private static readonly Dictionary<string, BeamLayoutData> _byDocumentUniqueId =
            new Dictionary<string, BeamLayoutData>();

        internal static BeamLayoutData GetOrCreate(Document doc)
        {
            if (doc == null)
            {
                return new BeamLayoutData();
            }

            var key = GetDocumentKey(doc);
            if (!_byDocumentUniqueId.TryGetValue(key, out var data))
            {
                data = new BeamLayoutData();
                _byDocumentUniqueId[key] = data;
            }

            return data;
        }

        internal static void Clear(Document doc)
        {
            if (doc == null)
            {
                return;
            }

            var key = GetDocumentKey(doc);
            _byDocumentUniqueId.Remove(key);
        }

        private static string GetDocumentKey(Document doc)
        {
            // Document has no stable UniqueId across all Revit versions; PathName can be empty for unsaved files.
            return string.IsNullOrWhiteSpace(doc.PathName) ? doc.GetHashCode().ToString() : doc.PathName;
        }
    }
}
