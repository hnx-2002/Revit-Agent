using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace RevitAgent.Utils
{
    internal sealed class StructuralColumnSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem?.Category != null && elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
