using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace RevitAgent.Utils
{
    internal sealed class AllowAllSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return true;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }
}

