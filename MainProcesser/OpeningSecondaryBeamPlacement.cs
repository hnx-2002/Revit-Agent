using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitAgent.Utils;

namespace RevitAgent.MainProcesser
{
    internal sealed class OpeningRect
    {
        public XYZ U { get; set; }
        public XYZ V { get; set; }
        public double MinU { get; set; }
        public double MaxU { get; set; }
        public double MinV { get; set; }
        public double MaxV { get; set; }
        public double LongSide { get; set; }
        public double ShortSide { get; set; }
        public XYZ Center { get; set; }
        public List<XYZ> Corners { get; set; }
    }

    internal static class OpeningSecondaryBeamPlacement
    {
        internal static bool ShouldPlaceOpeningBeams(OpeningRect hole, double oneMeterFeet)
        {
            if (hole == null)
            {
                return false;
            }

            return hole.LongSide > oneMeterFeet + 1e-6 || hole.ShortSide > oneMeterFeet + 1e-6;
        }

        internal static IEnumerable<BeamPlacementInfo> GeneratePlacements(
            IList<XYZ> regionPolygon,
            double z,
            OpeningRect hole,
            double oneMeterFeet)
        {
            var placements = new List<BeamPlacementInfo>();
            if (regionPolygon == null || regionPolygon.Count < 3 || hole == null)
            {
                return placements;
            }

            bool longAlongU = (hole.MaxU - hole.MinU) >= (hole.MaxV - hole.MinV);
            var dirLong = longAlongU ? hole.U : hole.V;
            var dirShort = longAlongU ? hole.V : hole.U;

            double longSpan = longAlongU ? (hole.MaxU - hole.MinU) : (hole.MaxV - hole.MinV);
            double shortSpan = longAlongU ? (hole.MaxV - hole.MinV) : (hole.MaxU - hole.MinU);

            if (longSpan <= oneMeterFeet + 1e-6 && shortSpan <= oneMeterFeet + 1e-6)
            {
                return placements;
            }

            if (longSpan > oneMeterFeet + 1e-6)
            {
                if (longAlongU)
                {
                    placements.AddRange(IntersectAndPlaceAlong(regionPolygon, z, dirLong, dirShort, hole.MinV, BeamRole.OpeningSecondary));
                    placements.AddRange(IntersectAndPlaceAlong(regionPolygon, z, dirLong, dirShort, hole.MaxV, BeamRole.OpeningSecondary));
                }
                else
                {
                    placements.AddRange(IntersectAndPlaceAlong(regionPolygon, z, dirLong, dirShort, hole.MinU, BeamRole.OpeningSecondary));
                    placements.AddRange(IntersectAndPlaceAlong(regionPolygon, z, dirLong, dirShort, hole.MaxU, BeamRole.OpeningSecondary));
                }
            }

            if (shortSpan > oneMeterFeet + 1e-6)
            {
                if (longAlongU)
                {
                    placements.Add(new BeamPlacementInfo
                    {
                        Role = BeamRole.OpeningSecondary,
                        Start = BeamLayoutGeometry2D.PointFromUV(hole.U, hole.V, hole.MinU, hole.MinV, z),
                        End = BeamLayoutGeometry2D.PointFromUV(hole.U, hole.V, hole.MinU, hole.MaxV, z),
                    });
                    placements.Add(new BeamPlacementInfo
                    {
                        Role = BeamRole.OpeningSecondary,
                        Start = BeamLayoutGeometry2D.PointFromUV(hole.U, hole.V, hole.MaxU, hole.MinV, z),
                        End = BeamLayoutGeometry2D.PointFromUV(hole.U, hole.V, hole.MaxU, hole.MaxV, z),
                    });
                }
                else
                {
                    placements.Add(new BeamPlacementInfo
                    {
                        Role = BeamRole.OpeningSecondary,
                        Start = BeamLayoutGeometry2D.PointFromUV(hole.U, hole.V, hole.MinU, hole.MinV, z),
                        End = BeamLayoutGeometry2D.PointFromUV(hole.U, hole.V, hole.MaxU, hole.MinV, z),
                    });
                    placements.Add(new BeamPlacementInfo
                    {
                        Role = BeamRole.OpeningSecondary,
                        Start = BeamLayoutGeometry2D.PointFromUV(hole.U, hole.V, hole.MinU, hole.MaxV, z),
                        End = BeamLayoutGeometry2D.PointFromUV(hole.U, hole.V, hole.MaxU, hole.MaxV, z),
                    });
                }
            }

            return placements;
        }

        internal static List<IList<XYZ>> SplitRegionByHole(IList<XYZ> regionPolygon, double z, OpeningRect hole)
        {
            var polys = SubtractRectangleFromPolygon(regionPolygon, z, hole);
            if (polys.Count == 0)
            {
                return polys;
            }

            // Defensive: never return the hole itself as a sub-region.
            var filtered = new List<IList<XYZ>>();
            foreach (var p in polys)
            {
                var c = BeamLayoutGeometry2D.Centroid2D(p);
                if (c != null && IsInsideHole(hole, c, 1e-6))
                {
                    continue;
                }
                filtered.Add(p);
            }

            return filtered;
        }

        private static IEnumerable<BeamPlacementInfo> IntersectAndPlaceAlong(
            IList<XYZ> polygon,
            double z,
            XYZ u,
            XYZ v,
            double t,
            BeamRole role)
        {
            var segs = BeamLayoutGeometry2D.IntersectPolygonWithLine(polygon, u, v, t, z)
                .OrderByDescending(x => x.Length)
                .ToList();

            if (segs.Count == 0)
            {
                return Enumerable.Empty<BeamPlacementInfo>();
            }

            var best = segs[0];
            return new[]
            {
                new BeamPlacementInfo
                {
                    Role = role,
                    Start = best.GetEndPoint(0),
                    End = best.GetEndPoint(1),
                }
            };
        }

        private static List<IList<XYZ>> SubtractRectangleFromPolygon(IList<XYZ> polygon, double z, OpeningRect hole)
        {
            var results = new List<IList<XYZ>>();
            if (polygon == null || hole == null)
            {
                return results;
            }

            polygon = BeamLayoutGeometry2D.NormalizePolygon2D(polygon, 1e-6);
            if (polygon.Count < 3)
            {
                return results;
            }

            var u = hole.U;
            var v = hole.V;
            double u0 = hole.MinU;
            double u1 = hole.MaxU;
            double v0 = hole.MinV;
            double v1 = hole.MaxV;

            var left = ClipByBound(polygon, p => BeamLayoutGeometry2D.Dot2D(p, u) - u0, keepNonPositive: true, z);
            AddIfValid(results, left);

            var right = ClipByBound(polygon, p => BeamLayoutGeometry2D.Dot2D(p, u) - u1, keepNonPositive: false, z);
            AddIfValid(results, right);

            var middle = ClipByBound(polygon, p => BeamLayoutGeometry2D.Dot2D(p, u) - u0, keepNonPositive: false, z);
            middle = ClipByBound(middle, p => BeamLayoutGeometry2D.Dot2D(p, u) - u1, keepNonPositive: true, z);

            var bottom = ClipByBound(middle, p => BeamLayoutGeometry2D.Dot2D(p, v) - v0, keepNonPositive: true, z);
            AddIfValid(results, bottom);

            var top = ClipByBound(middle, p => BeamLayoutGeometry2D.Dot2D(p, v) - v1, keepNonPositive: false, z);
            AddIfValid(results, top);

            return results;
        }

        private static void AddIfValid(List<IList<XYZ>> target, IList<XYZ> poly)
        {
            if (poly == null || poly.Count < 3)
            {
                return;
            }

            poly = BeamLayoutGeometry2D.NormalizePolygon2D(poly, 1e-6);
            if (poly.Count < 3)
            {
                return;
            }

            if (Math.Abs(BeamLayoutGeometry2D.ComputeArea2D(poly)) <= 1e-6)
            {
                return;
            }

            target.Add(poly);
        }

        private static bool IsInsideHole(OpeningRect hole, XYZ p, double tol)
        {
            if (hole == null || p == null)
            {
                return false;
            }

            double pu = BeamLayoutGeometry2D.Dot2D(p, hole.U);
            double pv = BeamLayoutGeometry2D.Dot2D(p, hole.V);
            return pu >= hole.MinU - tol && pu <= hole.MaxU + tol &&
                   pv >= hole.MinV - tol && pv <= hole.MaxV + tol;
        }

        private static IList<XYZ> ClipByBound(
            IList<XYZ> polygon,
            Func<XYZ, double> signedDistance,
            bool keepNonPositive,
            double z)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return new List<XYZ>();
            }

            const double tol = 1e-9;
            var output = new List<XYZ>();
            XYZ prev = polygon[polygon.Count - 1];
            double fPrev = signedDistance(prev);
            bool prevInside = keepNonPositive ? (fPrev <= tol) : (fPrev >= -tol);

            for (int i = 0; i < polygon.Count; i++)
            {
                var curr = polygon[i];
                double fCurr = signedDistance(curr);
                bool currInside = keepNonPositive ? (fCurr <= tol) : (fCurr >= -tol);

                if (currInside)
                {
                    if (!prevInside)
                    {
                        output.Add(BeamLayoutGeometry2D.ForcePointToZ(IntersectAtZero(prev, curr, fPrev, fCurr), z));
                    }
                    output.Add(BeamLayoutGeometry2D.ForcePointToZ(curr, z));
                }
                else if (prevInside)
                {
                    output.Add(BeamLayoutGeometry2D.ForcePointToZ(IntersectAtZero(prev, curr, fPrev, fCurr), z));
                }

                prev = curr;
                fPrev = fCurr;
                prevInside = currInside;
            }

            return output;
        }

        private static XYZ IntersectAtZero(XYZ a, XYZ b, double fa, double fb)
        {
            double denom = fa - fb;
            if (Math.Abs(denom) <= 1e-12)
            {
                return a;
            }

            double t = fa / denom;
            double x = a.X + (b.X - a.X) * t;
            double y = a.Y + (b.Y - a.Y) * t;
            double z = a.Z + (b.Z - a.Z) * t;
            return new XYZ(x, y, z);
        }
    }
}
