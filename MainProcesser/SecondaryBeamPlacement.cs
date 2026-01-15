using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitAgent.Utils;

namespace RevitAgent.MainProcesser
{
    internal static class SecondaryBeamPlacement
    {
        private static double MmToFeet(double mm)
        {
            const double mmPerFoot = 304.8;
            return mm / mmPerFoot;
        }

        internal static bool ShouldPlaceInSubRegion(IList<XYZ> polygon, double minSpanFeet)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return false;
            }

            if (!TryGetLocalAxesFromShortestEdge(polygon, out var u, out var v))
            {
                return false;
            }

            double minU = double.PositiveInfinity;
            double maxU = double.NegativeInfinity;
            double minV = double.PositiveInfinity;
            double maxV = double.NegativeInfinity;

            foreach (var p in polygon)
            {
                if (p == null)
                {
                    continue;
                }

                double pu = BeamLayoutGeometry2D.Dot2D(p, u);
                double pv = BeamLayoutGeometry2D.Dot2D(p, v);
                if (pu < minU) minU = pu;
                if (pu > maxU) maxU = pu;
                if (pv < minV) minV = pv;
                if (pv > maxV) maxV = pv;
            }

            if (double.IsInfinity(minU) || double.IsInfinity(minV))
            {
                return false;
            }

            double spanU = maxU - minU;
            double spanV = maxV - minV;
            double minSpan = spanU < spanV ? spanU : spanV;
            return minSpan >= minSpanFeet - 1e-6;
        }

        internal static List<BeamPlacementInfo> GeneratePlacementsFromFaceIndices(
            IEnumerable<IList<int>> faces,
            IList<XYZ> vertices,
            double z,
            double spacing)
        {
            return GeneratePlacementsFromFaceIndices(faces, vertices, z, spacing, guideCurves: null);
        }

        internal static List<BeamPlacementInfo> GeneratePlacementsFromFaceIndices(
            IEnumerable<IList<int>> faces,
            IList<XYZ> vertices,
            double z,
            double spacing,
            IList<Curve> guideCurves)
        {
            if (faces == null || vertices == null || vertices.Count < 3)
            {
                return new List<BeamPlacementInfo>();
            }

            var polygons = faces
                .Where(face => face != null && face.Count >= 3)
                .Select(face => face.Select(idx => vertices[idx]).ToList())
                .Cast<IList<XYZ>>()
                .ToList();

            return GeneratePlacementsFromPolygons(polygons, z, spacing, guideCurves);
        }

        internal static List<BeamPlacementInfo> GeneratePlacementsFromPolygons(
            IEnumerable<IList<XYZ>> polygons,
            double z,
            double spacing)
        {
            return GeneratePlacementsFromPolygons(polygons, z, spacing, guideCurves: null);
        }

        internal static List<BeamPlacementInfo> GeneratePlacementsFromPolygons(
            IEnumerable<IList<XYZ>> polygons,
            double z,
            double spacing,
            IList<Curve> guideCurves)
        {
            if (polygons == null)
            {
                return new List<BeamPlacementInfo>();
            }

            ClassifyGuideCurves(guideCurves, z, out var openings, out var loads);

            double minSpanFeet = MmToFeet(4000.0);
            var placements = new List<BeamPlacementInfo>();
            foreach (var polygon in polygons)
            {
                if (polygon == null || polygon.Count < 3)
                {
                    continue;
                }

                placements.AddRange(GeneratePlacementsForPolygon(
                    BeamLayoutGeometry2D.NormalizePolygon2D(polygon, 1e-6),
                    z,
                    spacing,
                    minSpanFeet,
                    openings,
                    loads));
            }

            return placements;
        }

        private sealed class LoadGuide
        {
            public Curve Curve { get; set; }
            public XYZ SamplePoint { get; set; }
        }

        private static void ClassifyGuideCurves(
            IList<Curve> curves,
            double z,
            out List<OpeningRect> openings,
            out List<LoadGuide> loads)
        {
            openings = new List<OpeningRect>();
            loads = new List<LoadGuide>();

            if (curves == null || curves.Count == 0)
            {
                return;
            }

            var lineCurves = new List<Line>();
            foreach (var c in curves)
            {
                if (c is Line ln)
                {
                    lineCurves.Add(BeamLayoutGeometry2D.ForceLineToZ(ln, z));
                }
                else if (c != null)
                {
                    loads.Add(new LoadGuide { Curve = c, SamplePoint = BeamLayoutGeometry2D.ForcePointToZ(c.Evaluate(0.5, true), z) });
                }
            }

            var comps = BuildConnectedComponents(lineCurves, 1e-4);
            foreach (var comp in comps)
            {
                if (TryBuildRectangle(comp, out var rect))
                {
                    openings.Add(rect);
                }
                else
                {
                    foreach (var ln in comp)
                    {
                        loads.Add(new LoadGuide { Curve = ln, SamplePoint = BeamLayoutGeometry2D.ForcePointToZ(ln.Evaluate(0.5, true), z) });
                    }
                }
            }
        }

        private static List<BeamPlacementInfo> GeneratePlacementsForPolygon(
            IList<XYZ> polygon,
            double z,
            double spacing,
            double minSpanFeet,
            List<OpeningRect> allOpenings,
            List<LoadGuide> allLoads)
        {
            polygon = BeamLayoutGeometry2D.NormalizePolygon2D(polygon, 1e-6);
            if (polygon == null || polygon.Count < 3)
            {
                return new List<BeamPlacementInfo>();
            }

            double oneMeterFeet = MmToFeet(1000.0);
            const double containTol = 1e-6;

            var placements = new List<BeamPlacementInfo>();
            var queue = new Queue<IList<XYZ>>();
            queue.Enqueue(polygon);

            while (queue.Count > 0)
            {
                var region = BeamLayoutGeometry2D.NormalizePolygon2D(queue.Dequeue(), 1e-6);
                if (region == null || region.Count < 3)
                {
                    continue;
                }

                var openingsInRegion = (allOpenings ?? new List<OpeningRect>())
                    .Where(o => o?.Corners != null && o.Corners.Count == 4 && IsOpeningFullyInsideRegion(region, o, containTol))
                    .Where(o => OpeningSecondaryBeamPlacement.ShouldPlaceOpeningBeams(o, oneMeterFeet))
                    .ToList();

                if (openingsInRegion.Count > 0)
                {
                    // Linear processing: handle one opening, split into sub-regions, continue.
                    var hole = openingsInRegion[0];
                    placements.AddRange(OpeningSecondaryBeamPlacement.GeneratePlacements(region, z, hole, oneMeterFeet));

                    var subRegions = OpeningSecondaryBeamPlacement.SplitRegionByHole(region, z, hole);
                    foreach (var sub in subRegions)
                    {
                        queue.Enqueue(sub);
                    }

                    continue;
                }

                // No openings: place normal secondaries once (no recursion after normal).
                if (!ShouldPlaceInSubRegion(region, minSpanFeet))
                {
                    continue;
                }

                var loadsInRegion = (allLoads ?? new List<LoadGuide>())
                    .Where(l => l?.SamplePoint != null && BeamLayoutGeometry2D.IsPointInPolygon2D(region, l.SamplePoint))
                    .ToList();

                var role = loadsInRegion.Count > 0 ? BeamRole.LoadSecondary : BeamRole.Secondary;
                foreach (var segment in SecondaryBeamLayout.GenerateSegments(region, z, spacing))
                {
                    placements.Add(new BeamPlacementInfo
                    {
                        Role = role,
                        Start = segment.GetEndPoint(0),
                        End = segment.GetEndPoint(1),
                    });
                }
            }

            return placements;
        }

        private static bool IsOpeningFullyInsideRegion(IList<XYZ> region, OpeningRect hole, double tol)
        {
            if (region == null || region.Count < 3 || hole?.Corners == null || hole.Corners.Count != 4)
            {
                return false;
            }

            foreach (var c in hole.Corners)
            {
                if (!BeamLayoutGeometry2D.IsPointStrictlyInsidePolygon2D(region, c, tol))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<List<Line>> BuildConnectedComponents(IList<Line> lines, double tol)
        {
            var results = new List<List<Line>>();
            if (lines == null || lines.Count == 0)
            {
                return results;
            }

            var comparer = new BeamLayoutGeometry2D.Xyz2DEqualityComparer(tol);
            var pointToIndex = new Dictionary<XYZ, int>(comparer);
            int nextIndex = 0;

            int GetIndex(XYZ p)
            {
                if (!pointToIndex.TryGetValue(p, out int idx))
                {
                    idx = nextIndex++;
                    pointToIndex[p] = idx;
                }
                return idx;
            }

            var dsu = new DisjointSet(2 * lines.Count + 8);
            var endpoints = new List<(Line line, int a, int b)>();
            foreach (var ln in lines)
            {
                var p0 = ln.GetEndPoint(0);
                var p1 = ln.GetEndPoint(1);
                int a = GetIndex(p0);
                int b = GetIndex(p1);
                dsu.Union(a, b);
                endpoints.Add((ln, a, b));
            }

            var byRoot = new Dictionary<int, List<Line>>();
            foreach (var e in endpoints)
            {
                int root = dsu.Find(e.a);
                if (!byRoot.TryGetValue(root, out var list))
                {
                    list = new List<Line>();
                    byRoot[root] = list;
                }
                list.Add(e.line);
            }

            results.AddRange(byRoot.Values);
            return results;
        }

        private static bool TryBuildRectangle(IList<Line> lines, out OpeningRect rect)
        {
            rect = null;
            if (lines == null || lines.Count != 4)
            {
                return false;
            }

            // Collect unique vertices.
            var points = new List<XYZ>();
            foreach (var ln in lines)
            {
                if (ln == null)
                {
                    return false;
                }

                points.Add(ln.GetEndPoint(0));
                points.Add(ln.GetEndPoint(1));
            }

            var unique = points.Distinct(new BeamLayoutGeometry2D.Xyz2DEqualityComparer(1e-4)).ToList();
            if (unique.Count != 4)
            {
                return false;
            }

            // Degree check: each vertex should touch exactly 2 edges.
            var deg = new int[unique.Count];
            foreach (var ln in lines)
            {
                int i0 = IndexOfPoint(unique, ln.GetEndPoint(0));
                int i1 = IndexOfPoint(unique, ln.GetEndPoint(1));
                if (i0 < 0 || i1 < 0)
                {
                    return false;
                }
                deg[i0]++;
                deg[i1]++;
            }

            if (deg.Any(d => d != 2))
            {
                return false;
            }

            // Direction clustering.
            var dirs = lines.Select(BeamLayoutGeometry2D.NormalizeDir2D).ToList();
            var clusters = new List<XYZ>();
            foreach (var d in dirs)
            {
                bool matched = false;
                foreach (var c in clusters)
                {
                    if (Math.Abs(BeamLayoutGeometry2D.Dot2D(d, c)) > 0.999)
                    {
                        matched = true;
                        break;
                    }
                }
                if (!matched)
                {
                    clusters.Add(d);
                }
            }

            if (clusters.Count != 2)
            {
                return false;
            }

            if (Math.Abs(BeamLayoutGeometry2D.Dot2D(clusters[0], clusters[1])) > 1e-2)
            {
                return false;
            }

            // Pick U/V as the two orthogonal directions.
            var u = clusters[0];
            var v = clusters[1];

            double minU = double.PositiveInfinity;
            double maxU = double.NegativeInfinity;
            double minV = double.PositiveInfinity;
            double maxV = double.NegativeInfinity;
            foreach (var p in unique)
            {
                double pu = BeamLayoutGeometry2D.Dot2D(p, u);
                double pv = BeamLayoutGeometry2D.Dot2D(p, v);
                if (pu < minU) minU = pu;
                if (pu > maxU) maxU = pu;
                if (pv < minV) minV = pv;
                if (pv > maxV) maxV = pv;
            }

            double spanU = maxU - minU;
            double spanV = maxV - minV;
            double longSide = Math.Max(spanU, spanV);
            double shortSide = Math.Min(spanU, spanV);

            var center = BeamLayoutGeometry2D.PointFromUV(u, v, (minU + maxU) * 0.5, (minV + maxV) * 0.5, unique[0].Z);
            rect = new OpeningRect
            {
                U = u,
                V = v,
                MinU = minU,
                MaxU = maxU,
                MinV = minV,
                MaxV = maxV,
                LongSide = longSide,
                ShortSide = shortSide,
                Center = center,
                Corners = new List<XYZ>
                {
                    BeamLayoutGeometry2D.PointFromUV(u, v, minU, minV, unique[0].Z),
                    BeamLayoutGeometry2D.PointFromUV(u, v, maxU, minV, unique[0].Z),
                    BeamLayoutGeometry2D.PointFromUV(u, v, maxU, maxV, unique[0].Z),
                    BeamLayoutGeometry2D.PointFromUV(u, v, minU, maxV, unique[0].Z),
                }
            };
            return true;
        }

        private static int IndexOfPoint(IList<XYZ> pts, XYZ p)
        {
            for (int i = 0; i < pts.Count; i++)
            {
                if (Math.Abs(pts[i].X - p.X) <= 1e-4 && Math.Abs(pts[i].Y - p.Y) <= 1e-4)
                {
                    return i;
                }
            }
            return -1;
        }

        private sealed class DisjointSet
        {
            private readonly int[] _parent;
            private readonly int[] _rank;

            public DisjointSet(int n)
            {
                _parent = new int[n];
                _rank = new int[n];
                for (int i = 0; i < n; i++)
                {
                    _parent[i] = i;
                }
            }

            public int Find(int x)
            {
                if (_parent[x] != x)
                {
                    _parent[x] = Find(_parent[x]);
                }
                return _parent[x];
            }

            public void Union(int a, int b)
            {
                int ra = Find(a);
                int rb = Find(b);
                if (ra == rb)
                {
                    return;
                }

                if (_rank[ra] < _rank[rb])
                {
                    _parent[ra] = rb;
                }
                else if (_rank[ra] > _rank[rb])
                {
                    _parent[rb] = ra;
                }
                else
                {
                    _parent[rb] = ra;
                    _rank[ra]++;
                }
            }
        }

        private static bool TryGetLocalAxesFromShortestEdge(IList<XYZ> polygon, out XYZ u, out XYZ v)
        {
            u = null;
            v = null;
            if (polygon == null || polygon.Count < 2)
            {
                return false;
            }

            // Avoid tiny clipping artifacts driving orientation.
            double minEdgeLenForOrientation = MmToFeet(50.0);

            double bestLen = double.PositiveInfinity;
            double bestLenAny = double.PositiveInfinity;
            XYZ bestDir = null;
            XYZ bestDirAny = null;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                if (a == null || b == null)
                {
                    continue;
                }

                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                double len = System.Math.Sqrt((dx * dx) + (dy * dy));
                if (len > 1e-9 && len < bestLenAny)
                {
                    bestLenAny = len;
                    bestDirAny = new XYZ(dx / len, dy / len, 0.0);
                }

                if (len >= minEdgeLenForOrientation && len < bestLen)
                {
                    bestLen = len;
                    bestDir = new XYZ(dx / len, dy / len, 0.0);
                }
            }

            if (bestDir == null)
            {
                bestDir = bestDirAny;
                if (bestDir == null)
                {
                    return false;
                }
            }

            u = bestDir;
            v = new XYZ(-u.Y, u.X, 0.0);
            return true;
        }
    }
}
