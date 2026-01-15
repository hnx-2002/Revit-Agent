using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitAgent.MainProcesser
{
    internal static class SecondaryRegionSplitter
    {
        internal static List<IList<XYZ>> SplitRegionByChord(IList<XYZ> region, XYZ a, XYZ b)
        {
            var result = new List<IList<XYZ>>();
            region = BeamLayoutGeometry2D.NormalizePolygon2D(region, 1e-6);
            if (region == null || region.Count < 3 || a == null || b == null)
            {
                return result;
            }

            if (a.DistanceTo(b) <= 1e-6)
            {
                return result;
            }

            var poly = region.ToList();
            if (!TryInsertPointOnPolygon(poly, a, 1e-6, out _))
            {
                return result;
            }

            if (!TryInsertPointOnPolygon(poly, b, 1e-6, out _))
            {
                return result;
            }

            int ia = FindPointIndex(poly, a, 1e-6);
            int ib = FindPointIndex(poly, b, 1e-6);
            if (ia < 0 || ib < 0)
            {
                return result;
            }

            if (ia == ib)
            {
                return result;
            }

            var path1 = BuildBoundaryPath(poly, ia, ib);
            var path2 = BuildBoundaryPath(poly, ib, ia);

            AddIfValid(result, path1);
            AddIfValid(result, path2);
            return result;
        }

        private static int FindPointIndex(IList<XYZ> polygon, XYZ p, double tol)
        {
            if (polygon == null || polygon.Count == 0 || p == null)
            {
                return -1;
            }

            for (int i = 0; i < polygon.Count; i++)
            {
                var vi = polygon[i];
                if (vi != null && Math.Abs(vi.X - p.X) <= tol && Math.Abs(vi.Y - p.Y) <= tol)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool TryInsertPointOnPolygon(List<XYZ> polygon, XYZ p, double tol, out int index)
        {
            index = -1;
            if (polygon == null || polygon.Count < 3 || p == null)
            {
                return false;
            }

            // Existing vertex?
            for (int i = 0; i < polygon.Count; i++)
            {
                var vi = polygon[i];
                if (vi != null && Math.Abs(vi.X - p.X) <= tol && Math.Abs(vi.Y - p.Y) <= tol)
                {
                    polygon[i] = BeamLayoutGeometry2D.ForcePointToZ(p, p.Z);
                    index = i;
                    return true;
                }
            }

            // Insert into an edge.
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                if (a == null || b == null)
                {
                    continue;
                }

                if (BeamLayoutGeometry2D.DistancePointToSegment2D(p, a, b) <= tol)
                {
                    var pz = BeamLayoutGeometry2D.ForcePointToZ(p, p.Z);
                    int insertAt = i + 1;
                    polygon.Insert(insertAt, pz);
                    index = insertAt;
                    return true;
                }
            }

            return false;
        }

        private static List<XYZ> BuildBoundaryPath(List<XYZ> polygon, int startIndex, int endIndex)
        {
            var path = new List<XYZ>();
            if (polygon == null || polygon.Count < 3)
            {
                return path;
            }

            int n = polygon.Count;
            int i = startIndex;
            while (true)
            {
                path.Add(polygon[i]);
                if (i == endIndex)
                {
                    break;
                }
                i = (i + 1) % n;
                if (path.Count > n + 2)
                {
                    break;
                }
            }

            return path;
        }

        private static void AddIfValid(List<IList<XYZ>> target, IList<XYZ> poly)
        {
            if (target == null || poly == null || poly.Count < 3)
            {
                return;
            }

            poly = BeamLayoutGeometry2D.NormalizePolygon2D(poly, 1e-6);
            if (poly == null || poly.Count < 3)
            {
                return;
            }

            if (Math.Abs(BeamLayoutGeometry2D.ComputeArea2D(poly)) <= 1e-6)
            {
                return;
            }

            target.Add(poly);
        }
    }
}

