using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitAgent.MainProcesser
{
    internal static class BeamLayoutGeometry2D
    {
        internal sealed class Xyz2DEqualityComparer : IEqualityComparer<XYZ>
        {
            private readonly double _tol;

            internal Xyz2DEqualityComparer(double tol)
            {
                _tol = tol;
            }

            public bool Equals(XYZ x, XYZ y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null) return false;
                return Math.Abs(x.X - y.X) <= _tol && Math.Abs(x.Y - y.Y) <= _tol;
            }

            public int GetHashCode(XYZ obj)
            {
                unchecked
                {
                    int hx = (int)Math.Round(obj.X / _tol);
                    int hy = (int)Math.Round(obj.Y / _tol);
                    return (hx * 397) ^ hy;
                }
            }
        }

        internal static double Dot2D(XYZ a, XYZ b)
        {
            return (a.X * b.X) + (a.Y * b.Y);
        }

        internal static XYZ PointFromUV(XYZ u, XYZ v, double du, double dv, double z)
        {
            double x = (u.X * du) + (v.X * dv);
            double y = (u.Y * du) + (v.Y * dv);
            return new XYZ(x, y, z);
        }

        internal static XYZ ForcePointToZ(XYZ p, double z)
        {
            return p == null ? null : new XYZ(p.X, p.Y, z);
        }

        internal static Line ForceLineToZ(Line ln, double z)
        {
            if (ln == null)
            {
                return null;
            }

            var p0 = ln.GetEndPoint(0);
            var p1 = ln.GetEndPoint(1);
            return Line.CreateBound(ForcePointToZ(p0, z), ForcePointToZ(p1, z));
        }

        internal static bool IsPointInPolygon2D(IList<XYZ> polygon, XYZ p)
        {
            if (polygon == null || polygon.Count < 3 || p == null)
            {
                return false;
            }

            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                if (a == null || b == null)
                {
                    continue;
                }

                bool intersect = ((a.Y > p.Y) != (b.Y > p.Y)) &&
                                 (p.X < (b.X - a.X) * (p.Y - a.Y) / ((b.Y - a.Y) + 1e-12) + a.X);
                if (intersect)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        internal static bool IsPointInOrOnPolygon2D(IList<XYZ> polygon, XYZ p, double tol)
        {
            if (polygon == null || polygon.Count < 3 || p == null)
            {
                return false;
            }

            if (IsPointOnPolygonBoundary2D(polygon, p, tol))
            {
                return true;
            }

            return IsPointInPolygon2D(polygon, p);
        }

        internal static bool IsPointStrictlyInsidePolygon2D(IList<XYZ> polygon, XYZ p, double tol)
        {
            if (polygon == null || polygon.Count < 3 || p == null)
            {
                return false;
            }

            if (IsPointOnPolygonBoundary2D(polygon, p, tol))
            {
                return false;
            }

            return IsPointInPolygon2D(polygon, p);
        }

        internal static bool IsPointOnPolygonBoundary2D(IList<XYZ> polygon, XYZ p, double tol)
        {
            if (polygon == null || polygon.Count < 2 || p == null)
            {
                return false;
            }

            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                if (a == null || b == null)
                {
                    continue;
                }

                if (DistancePointToSegment2D(p, a, b) <= tol)
                {
                    return true;
                }
            }

            return false;
        }

        internal static double DistancePointToSegment2D(XYZ p, XYZ a, XYZ b)
        {
            double abx = b.X - a.X;
            double aby = b.Y - a.Y;
            double apx = p.X - a.X;
            double apy = p.Y - a.Y;

            double ab2 = (abx * abx) + (aby * aby);
            if (ab2 <= 1e-18)
            {
                double dx = p.X - a.X;
                double dy = p.Y - a.Y;
                return Math.Sqrt((dx * dx) + (dy * dy));
            }

            double t = ((apx * abx) + (apy * aby)) / ab2;
            if (t < 0) t = 0;
            if (t > 1) t = 1;

            double cx = a.X + (abx * t);
            double cy = a.Y + (aby * t);
            double dx2 = p.X - cx;
            double dy2 = p.Y - cy;
            return Math.Sqrt((dx2 * dx2) + (dy2 * dy2));
        }

        internal static double ComputeArea2D(IList<XYZ> polygon)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return 0.0;
            }

            double area2 = 0.0;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                area2 += (a.X * b.Y) - (b.X * a.Y);
            }

            return area2 * 0.5;
        }

        internal static IList<XYZ> NormalizePolygon2D(IList<XYZ> polygon, double tol)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return new List<XYZ>();
            }

            var result = new List<XYZ>();
            XYZ prev = null;
            foreach (var p in polygon)
            {
                if (p == null)
                {
                    continue;
                }

                if (prev != null && Math.Abs(prev.X - p.X) <= tol && Math.Abs(prev.Y - p.Y) <= tol)
                {
                    continue;
                }

                result.Add(p);
                prev = p;
            }

            if (result.Count >= 2)
            {
                var first = result[0];
                var last = result[result.Count - 1];
                if (Math.Abs(first.X - last.X) <= tol && Math.Abs(first.Y - last.Y) <= tol)
                {
                    result.RemoveAt(result.Count - 1);
                }
            }

            if (result.Count < 3)
            {
                return new List<XYZ>();
            }

            // Remove near-colinear points.
            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < result.Count && result.Count >= 3; i++)
                {
                    var a = result[(i - 1 + result.Count) % result.Count];
                    var b = result[i];
                    var c = result[(i + 1) % result.Count];

                    double abx = b.X - a.X;
                    double aby = b.Y - a.Y;
                    double bcx = c.X - b.X;
                    double bcy = c.Y - b.Y;
                    double cross = (abx * bcy) - (aby * bcx);
                    double lab = Math.Sqrt((abx * abx) + (aby * aby));
                    double lbc = Math.Sqrt((bcx * bcx) + (bcy * bcy));
                    if (lab <= 1e-12 || lbc <= 1e-12)
                    {
                        result.RemoveAt(i);
                        changed = true;
                        break;
                    }

                    if (Math.Abs(cross) <= 1e-9 * lab * lbc)
                    {
                        result.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            } while (changed);

            return result.Count >= 3 ? result : new List<XYZ>();
        }

        internal static XYZ Centroid2D(IList<XYZ> polygon)
        {
            if (polygon == null || polygon.Count == 0)
            {
                return null;
            }

            double sx = 0.0;
            double sy = 0.0;
            int n = 0;
            foreach (var p in polygon)
            {
                if (p == null)
                {
                    continue;
                }

                sx += p.X;
                sy += p.Y;
                n++;
            }

            if (n == 0)
            {
                return null;
            }

            return new XYZ(sx / n, sy / n, polygon[0]?.Z ?? 0.0);
        }

        internal static IEnumerable<Line> IntersectPolygonWithLine(
            IList<XYZ> polygon,
            XYZ u,
            XYZ v,
            double t,
            double z)
        {
            const double tol = 1e-9;
            var intersections = new List<XYZ>();

            for (int i = 0; i < polygon.Count; i++)
            {
                XYZ a = polygon[i];
                XYZ b = polygon[(i + 1) % polygon.Count];

                double ta = Dot2D(a, v) - t;
                double tb = Dot2D(b, v) - t;

                if (Math.Abs(ta) <= tol && Math.Abs(tb) <= tol)
                {
                    continue;
                }

                if ((ta > tol && tb > tol) || (ta < -tol && tb < -tol))
                {
                    continue;
                }

                double denom = tb - ta;
                if (Math.Abs(denom) <= tol)
                {
                    continue;
                }

                double s = -ta / denom;
                if (s < -tol || s > 1.0 + tol)
                {
                    continue;
                }

                double x = a.X + (b.X - a.X) * s;
                double y = a.Y + (b.Y - a.Y) * s;
                intersections.Add(new XYZ(x, y, z));
            }

            if (intersections.Count < 2)
            {
                yield break;
            }

            intersections = intersections
                .Distinct(new Xyz2DEqualityComparer(1e-6))
                .ToList();

            intersections.Sort((p1, p2) =>
            {
                double d1 = Dot2D(p1, u);
                double d2 = Dot2D(p2, u);
                return d1.CompareTo(d2);
            });

            for (int i = 0; i + 1 < intersections.Count; i += 2)
            {
                var p0 = intersections[i];
                var p1 = intersections[i + 1];
                if (p0.DistanceTo(p1) <= 1e-6)
                {
                    continue;
                }

                yield return Line.CreateBound(p0, p1);
            }
        }

        internal static XYZ NormalizeDir2D(Line ln)
        {
            var p0 = ln.GetEndPoint(0);
            var p1 = ln.GetEndPoint(1);
            double dx = p1.X - p0.X;
            double dy = p1.Y - p0.Y;
            double len = Math.Sqrt((dx * dx) + (dy * dy));
            if (len <= 1e-12)
            {
                return XYZ.BasisX;
            }

            dx /= len;
            dy /= len;
            if (dx < -1e-12 || (Math.Abs(dx) <= 1e-12 && dy < 0))
            {
                dx = -dx;
                dy = -dy;
            }

            return new XYZ(dx, dy, 0.0);
        }
    }
}
