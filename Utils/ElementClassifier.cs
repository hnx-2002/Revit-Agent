using System;
using Autodesk.Revit.DB;

namespace AILayoutAgent.Utils
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

        internal static bool IsOpeningGuideCurveElement(Element element)
        {
            return HasLineStyleName(element, "00 方案布置-洞口边线");
        }

        internal static bool IsLoadGuideCurveElement(Element element)
        {
            return HasLineStyleName(element, "00 方案布置-荷载中心线");
        }

        private static bool HasLineStyleName(Element element, string expectedLineStyleName)
        {
            if (element is not CurveElement curveElement || string.IsNullOrWhiteSpace(expectedLineStyleName))
            {
                return false;
            }

            try
            {
                var gs = curveElement.LineStyle as GraphicsStyle;
                var name = gs?.Name;
                return string.Equals(name, expectedLineStyleName, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryGetCurveMidZ(Element element, out double z)
        {
            z = 0.0;
            if (element is not CurveElement ce)
            {
                return false;
            }

            try
            {
                var curve = ce.GeometryCurve;
                if (curve == null)
                {
                    return false;
                }

                var p0 = curve.GetEndPoint(0);
                var p1 = curve.GetEndPoint(1);
                z = (p0.Z + p1.Z) * 0.5;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryGetElementTopZ(Element element, out double topZ)
        {
            topZ = 0.0;
            if (element == null)
            {
                return false;
            }

            try
            {
                var bb = element.get_BoundingBox(null);
                if (bb == null)
                {
                    return false;
                }

                topZ = bb.Max.Z;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryGetColumnVerticalRange(Document doc, Element element, out double minZ, out double maxZ)
        {
            minZ = 0.0;
            maxZ = 0.0;
            if (doc == null || element == null)
            {
                return false;
            }

            if (element is not FamilyInstance)
            {
                return false;
            }

            try
            {
                var baseLvlId = element.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.AsElementId();
                var baseLvl = (baseLvlId == null || baseLvlId == ElementId.InvalidElementId) ? null : (doc.GetElement(baseLvlId) as Level);
                var baseOff = element.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0.0;
                var zBase = (baseLvl?.ProjectElevation ?? 0.0) + baseOff;

                double zTop;
                var topLvlId = element.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.AsElementId();
                if (topLvlId != null && topLvlId != ElementId.InvalidElementId)
                {
                    var topLvl = doc.GetElement(topLvlId) as Level;
                    var topOff = element.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0.0;
                    zTop = (topLvl?.ProjectElevation ?? zBase) + topOff;
                }
                else
                {
                    var h = element.get_Parameter(BuiltInParameter.INSTANCE_LENGTH_PARAM)?.AsDouble() ?? 0.0;
                    zTop = zBase + h;
                }

                minZ = Math.Min(zBase, zTop);
                maxZ = Math.Max(zBase, zTop);
                return maxZ >= minZ;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryGetColumnPointAtZ(Document doc, Element element, double z, out XYZ point)
        {
            point = null;
            if (doc == null || element == null)
            {
                return false;
            }

            if (element.Location is not LocationPoint lp)
            {
                return false;
            }

            if (!TryGetColumnVerticalRange(doc, element, out var minZ, out var maxZ))
            {
                return false;
            }

            if (z < minZ || z > maxZ)
            {
                return false;
            }

            point = new XYZ(lp.Point.X, lp.Point.Y, z);
            return true;
        }

        internal static bool TryGetColumnRectSizeFromSymbol(Element element, out double b, out double h)
        {
            b = 0.0;
            h = 0.0;

            if (element is not FamilyInstance fi)
            {
                return false;
            }

            var symbol = fi.Symbol;
            if (symbol == null)
            {
                return false;
            }

            try
            {
                var pb = symbol.LookupParameter("b");
                var ph = symbol.LookupParameter("h");
                if (pb == null || ph == null)
                {
                    return false;
                }

                if (pb.StorageType != StorageType.Double || ph.StorageType != StorageType.Double)
                {
                    return false;
                }

                b = pb.AsDouble();
                h = ph.AsDouble();
                return b > 0.0 && h > 0.0;
            }
            catch
            {
                return false;
            }
        }
    }
}
