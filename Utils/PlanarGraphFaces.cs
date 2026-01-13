using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitAgent.Utils
{
    internal readonly struct UndirectedEdge : IEquatable<UndirectedEdge>
    {
        public readonly int A;
        public readonly int B;

        public UndirectedEdge(int a, int b)
        {
            if (a <= b)
            {
                A = a;
                B = b;
            }
            else
            {
                A = b;
                B = a;
            }
        }

        public bool Equals(UndirectedEdge other)
        {
            return A == other.A && B == other.B;
        }

        public override bool Equals(object obj)
        {
            return obj is UndirectedEdge other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (A * 397) ^ B;
            }
        }
    }

    internal sealed class PlanarGraphFacesResult
    {
        public int GabrielEdgesCount { get; internal set; }
        public int FacesCount { get; internal set; }
        public HashSet<UndirectedEdge> UniqueEdges { get; internal set; }
        public List<List<int>> Faces { get; internal set; }

        public static PlanarGraphFacesResult Empty()
        {
            return new PlanarGraphFacesResult
            {
                GabrielEdgesCount = 0,
                FacesCount = 0,
                UniqueEdges = new HashSet<UndirectedEdge>(),
                Faces = new List<List<int>>()
            };
        }
    }

    internal static class PlanarGraphFaces
    {
        private readonly struct DirectedEdge : IEquatable<DirectedEdge>
        {
            public readonly int From;
            public readonly int To;

            public DirectedEdge(int from, int to)
            {
                From = from;
                To = to;
            }

            public bool Equals(DirectedEdge other)
            {
                return From == other.From && To == other.To;
            }

            public override bool Equals(object obj)
            {
                return obj is DirectedEdge other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (From * 397) ^ To;
                }
            }
        }

        public static PlanarGraphFacesResult BuildFromGabrielGraph(IList<XYZ> points)
        {
            if (points == null || points.Count < 2)
            {
                return PlanarGraphFacesResult.Empty();
            }

            int n = points.Count;
            List<UndirectedEdge> gabrielEdges = new List<UndirectedEdge>();
            const double tol = 1e-9;

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    XYZ pi = points[i];
                    XYZ pj = points[j];
                    double dx = pj.X - pi.X;
                    double dy = pj.Y - pi.Y;
                    double dist2 = dx * dx + dy * dy;
                    if (dist2 <= tol)
                    {
                        continue;
                    }

                    double cx = (pi.X + pj.X) * 0.5;
                    double cy = (pi.Y + pj.Y) * 0.5;
                    double radius2 = dist2 * 0.25;

                    bool hasPointInside = false;
                    for (int k = 0; k < n; k++)
                    {
                        if (k == i || k == j)
                        {
                            continue;
                        }

                        XYZ pk = points[k];
                        double kdx = pk.X - cx;
                        double kdy = pk.Y - cy;
                        double d2 = kdx * kdx + kdy * kdy;
                        if (d2 < radius2 - tol)
                        {
                            hasPointInside = true;
                            break;
                        }
                    }

                    if (!hasPointInside)
                    {
                        gabrielEdges.Add(new UndirectedEdge(i, j));
                    }
                }
            }

            return BuildFromPlanarEdges(points, gabrielEdges, gabrielEdges.Count);
        }

        public static PlanarGraphFacesResult BuildFromRelativeNeighborhoodGraph(IList<XYZ> points)
        {
            if (points == null || points.Count < 2)
            {
                return PlanarGraphFacesResult.Empty();
            }

            int n = points.Count;
            List<UndirectedEdge> edges = new List<UndirectedEdge>();
            const double tol = 1e-9;

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    XYZ pi = points[i];
                    XYZ pj = points[j];
                    double dx = pj.X - pi.X;
                    double dy = pj.Y - pi.Y;
                    double dij2 = dx * dx + dy * dy;
                    if (dij2 <= tol)
                    {
                        continue;
                    }

                    bool blocked = false;
                    for (int k = 0; k < n; k++)
                    {
                        if (k == i || k == j)
                        {
                            continue;
                        }

                        XYZ pk = points[k];

                        double ikx = pk.X - pi.X;
                        double iky = pk.Y - pi.Y;
                        double dik2 = ikx * ikx + iky * iky;
                        if (dik2 >= dij2 - tol)
                        {
                            continue;
                        }

                        double jkx = pk.X - pj.X;
                        double jky = pk.Y - pj.Y;
                        double djk2 = jkx * jkx + jky * jky;
                        if (djk2 >= dij2 - tol)
                        {
                            continue;
                        }

                        // Relative Neighborhood Graph (beta-skeleton with beta=2):
                        // reject (i,j) if ∃k with max(dik, djk) < dij.
                        if (Math.Max(dik2, djk2) < dij2 - tol)
                        {
                            blocked = true;
                            break;
                        }
                    }

                    if (!blocked)
                    {
                        edges.Add(new UndirectedEdge(i, j));
                    }
                }
            }

            return BuildFromPlanarEdges(points, edges, edges.Count);
        }

        private static PlanarGraphFacesResult BuildFromPlanarEdges(
            IList<XYZ> points,
            List<UndirectedEdge> edges,
            int rawEdgeCount)
        {
            int n = points.Count;
            Dictionary<int, List<int>> adjacency = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                adjacency[i] = new List<int>();
            }

            foreach (UndirectedEdge edge in edges)
            {
                adjacency[edge.A].Add(edge.B);
                adjacency[edge.B].Add(edge.A);
            }

            foreach (KeyValuePair<int, List<int>> kvp in adjacency)
            {
                int v = kvp.Key;
                kvp.Value.Sort((a, b) =>
                {
                    double aa = Math.Atan2(points[a].Y - points[v].Y, points[a].X - points[v].X);
                    double bb = Math.Atan2(points[b].Y - points[v].Y, points[b].X - points[v].X);
                    return aa.CompareTo(bb);
                });
            }

            Dictionary<DirectedEdge, bool> visited = new Dictionary<DirectedEdge, bool>();
            foreach (UndirectedEdge edge in edges)
            {
                visited[new DirectedEdge(edge.A, edge.B)] = false;
                visited[new DirectedEdge(edge.B, edge.A)] = false;
            }

            List<List<int>> faces = new List<List<int>>();
            foreach (DirectedEdge start in visited.Keys.ToList())
            {
                if (visited[start])
                {
                    continue;
                }

                List<int> cycle = TraceFace(points, adjacency, visited, start);
                if (cycle == null || cycle.Count < 3)
                {
                    continue;
                }

                double area = SignedArea(points, cycle);
                if (area > 1e-6)
                {
                    faces.Add(cycle);
                }
            }

            HashSet<UndirectedEdge> edgesToDraw = new HashSet<UndirectedEdge>();
            foreach (List<int> face in faces)
            {
                for (int i = 0; i < face.Count; i++)
                {
                    int a = face[i];
                    int b = face[(i + 1) % face.Count];
                    edgesToDraw.Add(new UndirectedEdge(a, b));
                }
            }

            // Columns that do not belong to any detected face should still connect to the nearest column.
            // This prevents isolated points (e.g. an extra column outside the main layout) from being ignored.
            int[] degrees = new int[n];
            foreach (UndirectedEdge edge in edgesToDraw)
            {
                degrees[edge.A]++;
                degrees[edge.B]++;
            }

            for (int i = 0; i < n; i++)
            {
                if (degrees[i] != 0)
                {
                    continue;
                }

                int nearest = FindNearestPointIndex(points, i);
                if (nearest >= 0 && nearest != i)
                {
                    var extra = new UndirectedEdge(i, nearest);
                    if (!edgesToDraw.Contains(extra))
                    {
                        edgesToDraw.Add(extra);
                        degrees[i]++;
                        degrees[nearest]++;
                    }
                }
            }

            return new PlanarGraphFacesResult
            {
                GabrielEdgesCount = rawEdgeCount,
                FacesCount = faces.Count,
                UniqueEdges = edgesToDraw,
                Faces = faces
            };
        }

        private static int FindNearestPointIndex(IList<XYZ> points, int index)
        {
            if (points == null || index < 0 || index >= points.Count)
            {
                return -1;
            }

            XYZ p = points[index];
            int nearest = -1;
            double bestD2 = double.PositiveInfinity;

            for (int j = 0; j < points.Count; j++)
            {
                if (j == index)
                {
                    continue;
                }

                XYZ q = points[j];
                double dx = q.X - p.X;
                double dy = q.Y - p.Y;
                double d2 = (dx * dx) + (dy * dy);
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    nearest = j;
                }
            }

            return nearest;
        }

        private static List<int> TraceFace(
            IList<XYZ> points,
            Dictionary<int, List<int>> adjacency,
            Dictionary<DirectedEdge, bool> visited,
            DirectedEdge start)
        {
            if (!visited.ContainsKey(start))
            {
                return null;
            }

            List<int> cycle = new List<int>();
            DirectedEdge current = start;

            int guard = 0;
            while (guard++ < 100000)
            {
                if (!visited.ContainsKey(current) || visited[current])
                {
                    break;
                }

                visited[current] = true;
                cycle.Add(current.From);

                int v = current.To;
                int u = current.From;
                List<int> neighbors = adjacency[v];
                if (neighbors.Count == 0)
                {
                    return null;
                }

                int idx = neighbors.IndexOf(u);
                if (idx < 0)
                {
                    return null;
                }

                int nextIdx = (idx - 1 + neighbors.Count) % neighbors.Count;
                int w = neighbors[nextIdx];
                DirectedEdge next = new DirectedEdge(v, w);

                if (next.Equals(start))
                {
                    return cycle;
                }

                current = next;
            }

            return null;
        }

        private static double SignedArea(IList<XYZ> points, IList<int> polygon)
        {
            double sum = 0.0;
            for (int i = 0; i < polygon.Count; i++)
            {
                XYZ a = points[polygon[i]];
                XYZ b = points[polygon[(i + 1) % polygon.Count]];
                sum += (a.X * b.Y) - (b.X * a.Y);
            }

            return 0.5 * sum;
        }
    }
}
