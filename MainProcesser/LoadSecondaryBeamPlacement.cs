using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitAgent.Utils;

namespace RevitAgent.MainProcesser
{
    internal sealed class LoadGuide
    {
        public Curve Curve { get; set; }
        public XYZ SamplePoint { get; set; }
    }

    internal static class LoadSecondaryBeamPlacement
    {
        internal static bool TryPlaceAndSplit(
            IList<XYZ> region,
            double z,
            List<LoadGuide> allLoads,
            HashSet<LoadGuide> placedLoads,
            out BeamPlacementInfo placement,
            out List<IList<XYZ>> subRegions)
        {
            placement = null;
            subRegions = null;

            if (!TryFindNextLoadPlacement(region, z, allLoads, placedLoads, out var loadPlacement))
            {
                return false;
            }

            placement = new BeamPlacementInfo
            {
                Role = BeamRole.LoadSecondary,
                Start = loadPlacement.Segment.GetEndPoint(0),
                End = loadPlacement.Segment.GetEndPoint(1),
            };

            subRegions = SecondaryRegionSplitter.SplitRegionByChord(region, placement.Start, placement.End);
            return true;
        }

        private sealed class LoadPlacement
        {
            public LoadGuide Guide { get; set; }
            public Line Segment { get; set; }
        }

        private static bool TryFindNextLoadPlacement(
            IList<XYZ> region,
            double z,
            List<LoadGuide> allLoads,
            HashSet<LoadGuide> placedLoads,
            out LoadPlacement placement)
        {
            placement = null;
            if (region == null || region.Count < 3 || allLoads == null || allLoads.Count == 0)
            {
                return false;
            }

            foreach (var load in allLoads)
            {
                if (load?.Curve == null)
                {
                    continue;
                }

                if (placedLoads != null && placedLoads.Contains(load))
                {
                    continue;
                }

                // The load must belong to the current sub-region.
                if (load.SamplePoint == null || !IsPointInOrOnPolygon2D(region, load.SamplePoint, 1e-6))
                {
                    continue;
                }

                if (!TryGetCurveEndpointsAtZ(load.Curve, z, out var p0, out var p1))
                {
                    continue;
                }

                if (!TryComputeExtendedSegmentWithinRegion(region, z, p0, p1, load.SamplePoint, out var seg))
                {
                    continue;
                }

                if (seg.Length <= 1e-6)
                {
                    continue;
                }

                if (placedLoads != null)
                {
                    placedLoads.Add(load);
                }

                placement = new LoadPlacement { Guide = load, Segment = seg };
                return true;
            }

            return false;
        }

        private static bool TryGetCurveEndpointsAtZ(Curve curve, double z, out XYZ p0, out XYZ p1)
        {
            p0 = null;
            p1 = null;
            if (curve == null)
            {
                return false;
            }

            try
            {
                p0 = BeamLayoutGeometry2D.ForcePointToZ(curve.GetEndPoint(0), z);
                p1 = BeamLayoutGeometry2D.ForcePointToZ(curve.GetEndPoint(1), z);
            }
            catch
            {
                return false;
            }

            return p0 != null && p1 != null && p0.DistanceTo(p1) > 1e-9;
        }

        private static bool IsPointInOrOnPolygon2D(IList<XYZ> polygon, XYZ p, double tol)
        {
            if (polygon == null || polygon.Count < 3 || p == null)
            {
                return false;
            }

            return BeamLayoutGeometry2D.IsPointInPolygon2D(polygon, p) ||
                   BeamLayoutGeometry2D.IsPointOnPolygonBoundary2D(polygon, p, tol);
        }

        private static bool TryComputeExtendedSegmentWithinRegion(
            IList<XYZ> region,
            double z,
            XYZ p0,
            XYZ p1,
            XYZ samplePoint,
            out Line seg)
        {
            seg = null;
            if (region == null || region.Count < 3 || p0 == null || p1 == null)
            {
                return false;
            }

            double dx = p1.X - p0.X;
            double dy = p1.Y - p0.Y;
            double len = Math.Sqrt((dx * dx) + (dy * dy));
            if (len <= 1e-12)
            {
                return false;
            }

            var u = new XYZ(dx / len, dy / len, 0.0);
            var v = new XYZ(-u.Y, u.X, 0.0);
            double t = BeamLayoutGeometry2D.Dot2D(BeamLayoutGeometry2D.ForcePointToZ(p0, z), v);

            double pu0 = BeamLayoutGeometry2D.Dot2D(p0, u);
            double pu1 = BeamLayoutGeometry2D.Dot2D(p1, u);
            double minPu = Math.Min(pu0, pu1);
            double maxPu = Math.Max(pu0, pu1);

            var candidates = BeamLayoutGeometry2D.IntersectPolygonWithLine(region, u, v, t, z).ToList();
            if (candidates.Count == 0)
            {
                return false;
            }

            const double tol = 1e-6;
            Line best = null;

            // Prefer the segment that contains the sampled point (stable in non-convex regions / after splitting).
            if (samplePoint != null)
            {
                var sp = BeamLayoutGeometry2D.ForcePointToZ(samplePoint, z);
                double spDist = Math.Abs(BeamLayoutGeometry2D.Dot2D(sp, v) - t);
                if (spDist <= 1e-4)
                {
                    double spu = BeamLayoutGeometry2D.Dot2D(sp, u);
                    foreach (var c in candidates)
                    {
                        if (c == null)
                        {
                            continue;
                        }

                        double c0 = BeamLayoutGeometry2D.Dot2D(c.GetEndPoint(0), u);
                        double c1 = BeamLayoutGeometry2D.Dot2D(c.GetEndPoint(1), u);
                        double cMin = Math.Min(c0, c1);
                        double cMax = Math.Max(c0, c1);
                        if (spu >= cMin - tol && spu <= cMax + tol)
                        {
                            best = c;
                            break;
                        }
                    }
                }
            }

            if (best == null)
            {
                foreach (var c in candidates)
                {
                    if (c == null)
                    {
                        continue;
                    }

                    double c0 = BeamLayoutGeometry2D.Dot2D(c.GetEndPoint(0), u);
                    double c1 = BeamLayoutGeometry2D.Dot2D(c.GetEndPoint(1), u);
                    double cMin = Math.Min(c0, c1);
                    double cMax = Math.Max(c0, c1);

                    if (minPu >= cMin - tol && maxPu <= cMax + tol)
                    {
                        best = c;
                        break;
                    }
                }
            }

            if (best == null)
            {
                // Fallback: use a midpoint to locate the right segment (for non-convex regions).
                var mid = BeamLayoutGeometry2D.ForcePointToZ(new XYZ((p0.X + p1.X) * 0.5, (p0.Y + p1.Y) * 0.5, z), z);
                double pm = BeamLayoutGeometry2D.Dot2D(mid, u);
                foreach (var c in candidates)
                {
                    double c0 = BeamLayoutGeometry2D.Dot2D(c.GetEndPoint(0), u);
                    double c1 = BeamLayoutGeometry2D.Dot2D(c.GetEndPoint(1), u);
                    double cMin = Math.Min(c0, c1);
                    double cMax = Math.Max(c0, c1);
                    if (pm >= cMin - tol && pm <= cMax + tol)
                    {
                        best = c;
                        break;
                    }
                }
            }

            if (best == null)
            {
                return false;
            }

            seg = Line.CreateBound(
                BeamLayoutGeometry2D.ForcePointToZ(best.GetEndPoint(0), z),
                BeamLayoutGeometry2D.ForcePointToZ(best.GetEndPoint(1), z));
            return true;
        }
    }
}
