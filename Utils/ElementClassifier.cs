using System;
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

        internal static bool IsConcreteRectColumn(Element element)
        {
            if (!IsStructuralColumn(element))
            {
                return false;
            }

            if (element is not FamilyInstance fi)
            {
                return false;
            }

            try
            {
                var symbol = fi.Symbol;
                var familyName = symbol?.FamilyName ?? symbol?.Family?.Name ?? string.Empty;
                return familyName.StartsWith("结构_柱_矩形混凝土柱", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static Line GetVerticalColumnAxis(Element col, Document doc)
        {
            if (col == null || doc == null)
            {
                return null;
            }

            if (col.Location is not LocationPoint lp)
            {
                return null;
            }

            try
            {
                var baseLvlId = col.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.AsElementId();
                var baseLvl = (baseLvlId == null || baseLvlId == ElementId.InvalidElementId)
                    ? null
                    : (doc.GetElement(baseLvlId) as Level);

                double baseOff = col.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0.0;
                double zBase = (baseLvl?.ProjectElevation ?? 0.0) + baseOff;

                double zTop;
                var topLvlId = col.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.AsElementId();
                if (topLvlId != null && topLvlId != ElementId.InvalidElementId)
                {
                    var topLvl = doc.GetElement(topLvlId) as Level;
                    double topOff = col.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0.0;
                    zTop = (topLvl?.ProjectElevation ?? zBase) + topOff;
                }
                else
                {
                    double h = col.get_Parameter(BuiltInParameter.INSTANCE_LENGTH_PARAM)?.AsDouble() ?? 0.0;
                    zTop = zBase + h;
                }

                var p0 = new XYZ(lp.Point.X, lp.Point.Y, zBase);
                var p1 = new XYZ(lp.Point.X, lp.Point.Y, zTop);
                return Line.CreateBound(p0, p1);
            }
            catch
            {
                return null;
            }
        }

        internal static bool TryGetColumnPointAtZ(Document doc, Element element, double z, out XYZ point)
        {
            point = null;
            var axis = GetVerticalColumnAxis(element, doc);
            if (axis == null)
            {
                return false;
            }

            double z0 = axis.GetEndPoint(0).Z;
            double z1 = axis.GetEndPoint(1).Z;
            double minZ = Math.Min(z0, z1);
            double maxZ = Math.Max(z0, z1);
            if (z < minZ || z > maxZ)
            {
                return false;
            }

            var p = axis.GetEndPoint(0);
            point = new XYZ(p.X, p.Y, z);
            return true;
        }

        internal static bool TryGetColumnVerticalRange(Document doc, Element element, out double minZ, out double maxZ)
        {
            minZ = 0.0;
            maxZ = 0.0;
            if (doc == null || element == null)
            {
                return false;
            }

            var axis = GetVerticalColumnAxis(element, doc);
            if (axis == null)
            {
                return false;
            }

            double z0 = axis.GetEndPoint(0).Z;
            double z1 = axis.GetEndPoint(1).Z;
            minZ = Math.Min(z0, z1);
            maxZ = Math.Max(z0, z1);
            return maxZ >= minZ;
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

        internal static bool IsCurveElement(Element element)
        {
            if (element is CurveElement ln)
            {
                var gs = (ln.LineStyle as GraphicsStyle);
                int styleId = gs?.Id.IntegerValue ?? -1;

                if (styleId == 7072197)
                    return true;
            }
            return false;
            
        }
    }
}
