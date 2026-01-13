using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitAgent.MainProcesser
{
    internal static class SecondaryBeamLayout
    {
        internal static IEnumerable<Line> GenerateSegments(IList<XYZ> polygon, double z, double spacing)
        {
            if (polygon == null || polygon.Count < 3 || spacing <= 1e-9)
            {
                yield break;
            }

            var shortest = FindShortestEdge(polygon);
            if (shortest.Length <= 1e-9)
            {
                yield break;
            }

            XYZ u = shortest.Direction;
            XYZ v = new XYZ(-u.Y, u.X, 0.0);

            double minProj = double.PositiveInfinity;
            double maxProj = double.NegativeInfinity;
            foreach (var p in polygon)
            {
                double t = Dot2D(p, v);
                if (t < minProj) minProj = t;
                if (t > maxProj) maxProj = t;
            }

            double width = maxProj - minProj;
            if (width <= spacing)
            {
                yield break;
            }

            for (double d = spacing; d < width; d += spacing)
            {
                double t = minProj + d;
                foreach (var seg in IntersectPolygonWithLine(polygon, u, v, t, z))
                {
                    yield return seg;
                }

                if (width - d < spacing)
                {
                    yield break;
                }
            }
        }

        private readonly struct EdgeInfo
        {
            public readonly double Length;
            public readonly XYZ Direction;

            public EdgeInfo(double length, XYZ direction)
            {
                Length = length;
                Direction = direction;
            }
        }

        private static EdgeInfo FindShortestEdge(IList<XYZ> polygon)
        {
            double best = double.PositiveInfinity;
            XYZ bestDir = XYZ.BasisX;

            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                double len = Math.Sqrt((dx * dx) + (dy * dy));
                if (len > 1e-9 && len < best)
                {
                    best = len;
                    bestDir = new XYZ(dx / len, dy / len, 0.0);
                }
            }

            return new EdgeInfo(best, bestDir);
        }

        private static double Dot2D(XYZ a, XYZ b)
        {
            return (a.X * b.X) + (a.Y * b.Y);
        }

        private static IEnumerable<Line> IntersectPolygonWithLine(
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

        private sealed class Xyz2DEqualityComparer : IEqualityComparer<XYZ>
        {
            private readonly double _tol;

            public Xyz2DEqualityComparer(double tol)
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
    }
}

