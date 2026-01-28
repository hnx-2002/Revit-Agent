using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace AILayoutAgent.Utils
{
    internal sealed class AllowAllSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => true;
        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
