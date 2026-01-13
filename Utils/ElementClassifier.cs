using Autodesk.Revit.DB;

namespace RevitAgent.Utils
{
    internal static class ElementClassifier
    {
        internal static bool IsStructuralColumn(Element element)
        {
            return element?.Category != null &&
                   element.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns;
        }

        internal static bool IsFloor(Element element)
        {
            if (element == null)
            {
                return false;
            }

            if (element is Floor)
            {
                return true;
            }

            return element.Category != null &&
                   element.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Floors;
        }
    }
}

