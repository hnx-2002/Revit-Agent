using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitAgent.MainProcesser
{
    internal static class FloorBoundaryPreview
    {
        internal sealed class TaggedLoop
        {
            public bool IsOuter { get; set; }
            public IList<Curve> Curves { get; set; }
        }

        internal sealed class BoundaryLoopsResult
        {
            public double TopZ { get; set; }
            public List<List<Curve>> Loops { get; } = new List<List<Curve>>();
            public string ErrorMessage { get; set; }
        }

        internal static BoundaryLoopsResult GetTopFaceBoundaryLoops(Element floorElement, Document doc, double? forceZ = null)
        {
            var result = new BoundaryLoopsResult();
            if (floorElement == null || doc == null)
            {
                result.ErrorMessage = "Floor boundary read failed: missing floor element or document.";
                return result;
            }

            try
            {
                var options = new Options
                {
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = true,
                    DetailLevel = ViewDetailLevel.Fine
                };

                var solids = CollectSolids(floorElement.get_Geometry(options));
                if (solids.Count == 0)
                {
                    result.ErrorMessage = "Floor boundary read failed: no solids found in floor geometry.";
                    return result;
                }

                var topFace = FindTopPlanarFace(solids, out double topZ);
                if (topFace == null)
                {
                    result.ErrorMessage = "Floor boundary read failed: cannot find top planar face.";
                    return result;
                }

                double zToUse = forceZ ?? topZ;
                result.TopZ = zToUse;

                var loops = new List<List<Curve>>();
                foreach (EdgeArray edgeArray in topFace.EdgeLoops)
                {
                    if (edgeArray == null || edgeArray.Size == 0)
                    {
                        continue;
                    }

                    var curves = new List<Curve>();
                    foreach (Edge edge in edgeArray)
                    {
                        var c = edge?.AsCurve();
                        if (c == null)
                        {
                            continue;
                        }

                        curves.Add(ForceCurveToZ(c, zToUse));
                    }

                    if (curves.Count > 0)
                    {
                        loops.Add(curves);
                    }
                }

                if (loops.Count == 0)
                {
                    result.ErrorMessage = "Floor boundary read failed: no boundary edge loops found.";
                    return result;
                }

                // Sort loops by absolute projected area: largest is treated as outer boundary.
                loops.Sort((a, b) =>
                {
                    double aa = Math.Abs(ComputeProjectedArea(a));
                    double bb = Math.Abs(ComputeProjectedArea(b));
                    return bb.CompareTo(aa);
                });

                result.Loops.AddRange(loops);
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "Floor boundary read failed: " + ex.Message;
                return result;
            }
        }

        internal static void DrawPreviewCurves(
            Document doc,
            IEnumerable<IList<Curve>> sortedLoops,
            double z,
            string tagPrefix = "RevitAgent-FloorBoundary")
        {
            if (doc == null || sortedLoops == null)
            {
                return;
            }

            ClearExistingPreviewCurves(doc, tagPrefix);

            var sketchPlane = CreateSketchPlaneAtZ(doc, z);
            int loopIndex = 0;
            foreach (var loop in sortedLoops)
            {
                if (loop == null)
                {
                    loopIndex++;
                    continue;
                }

                string tag = loopIndex == 0 ? (tagPrefix + "-Outer") : (tagPrefix + "-Hole");
                foreach (var curve in loop)
                {
                    if (curve == null)
                    {
                        continue;
                    }

                    try
                    {
                        var mc = doc.Create.NewModelCurve(curve, sketchPlane);
                        TagCurve(mc, tag);
                    }
                    catch
                    {
                        // ignore per-curve failures
                    }
                }

                loopIndex++;
            }
        }

        internal static void DrawPreviewCurves(
            Document doc,
            IEnumerable<TaggedLoop> loops,
            double z,
            string tagPrefix = "RevitAgent-FloorBoundary")
        {
            if (doc == null || loops == null)
            {
                return;
            }

            ClearExistingPreviewCurves(doc, tagPrefix);

            var sketchPlane = CreateSketchPlaneAtZ(doc, z);
            foreach (var loop in loops)
            {
                if (loop?.Curves == null)
                {
                    continue;
                }

                string tag = loop.IsOuter ? (tagPrefix + "-Outer") : (tagPrefix + "-Hole");
                foreach (var curve in loop.Curves)
                {
                    if (curve == null)
                    {
                        continue;
                    }

                    try
                    {
                        var mc = doc.Create.NewModelCurve(curve, sketchPlane);
                        TagCurve(mc, tag);
                    }
                    catch
                    {
                        // ignore per-curve failures
                    }
                }
            }
        }

        private static void ClearExistingPreviewCurves(Document doc, string tagPrefix)
        {
            if (doc == null || string.IsNullOrWhiteSpace(tagPrefix))
            {
                return;
            }

            try
            {
                var ids = new FilteredElementCollector(doc)
                    .OfClass(typeof(CurveElement))
                    .WhereElementIsNotElementType()
                    .Cast<CurveElement>()
                    .Where(e => HasCommentPrefix(e, tagPrefix))
                    .Select(e => e.Id)
                    .ToList();

                if (ids.Count > 0)
                {
                    doc.Delete(ids);
                }
            }
            catch
            {
                // ignore
            }
        }

        private static bool HasCommentPrefix(Element element, string prefix)
        {
            if (element == null || string.IsNullOrWhiteSpace(prefix))
            {
                return false;
            }

            try
            {
                var p = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                var v = p?.AsString() ?? string.Empty;
                return v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void TagCurve(CurveElement curve, string tag)
        {
            if (curve == null || string.IsNullOrWhiteSpace(tag))
            {
                return;
            }

            try
            {
                var p = curve.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (p != null && !p.IsReadOnly)
                {
                    p.Set(tag);
                }
            }
            catch
            {
                // ignore
            }
        }

        private static SketchPlane CreateSketchPlaneAtZ(Document doc, double z)
        {
            var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, z));
            return SketchPlane.Create(doc, plane);
        }

        private static List<Solid> CollectSolids(GeometryElement ge)
        {
            var solids = new List<Solid>();
            if (ge == null)
            {
                return solids;
            }

            foreach (var obj in ge)
            {
                if (obj is Solid s)
                {
                    if (s.Volume > 1e-9)
                    {
                        solids.Add(s);
                    }
                    continue;
                }

                if (obj is GeometryInstance gi)
                {
                    try
                    {
                        solids.AddRange(CollectSolids(gi.GetInstanceGeometry()));
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            return solids;
        }

        private static PlanarFace FindTopPlanarFace(IList<Solid> solids, out double topZ)
        {
            topZ = double.NegativeInfinity;
            PlanarFace best = null;
            if (solids == null)
            {
                return null;
            }

            foreach (var solid in solids)
            {
                if (solid == null || solid.Faces == null || solid.Faces.Size == 0)
                {
                    continue;
                }

                foreach (Face f in solid.Faces)
                {
                    if (f is not PlanarFace pf)
                    {
                        continue;
                    }

                    // Horizontal-ish and facing up.
                    if (pf.FaceNormal == null || pf.FaceNormal.Z < 0.9)
                    {
                        continue;
                    }

                    double z = pf.Origin.Z;
                    if (z > topZ)
                    {
                        topZ = z;
                        best = pf;
                    }
                }
            }

            return best;
        }

        private static Curve ForceCurveToZ(Curve curve, double z)
        {
            if (curve == null)
            {
                return null;
            }

            try
            {
                var p0 = curve.GetEndPoint(0);
                var p1 = curve.GetEndPoint(1);

                if (curve is Line)
                {
                    return Line.CreateBound(
                        new XYZ(p0.X, p0.Y, z),
                        new XYZ(p1.X, p1.Y, z));
                }

                if (curve is Arc)
                {
                    var pm = curve.Evaluate(0.5, true);
                    return Arc.Create(
                        new XYZ(p0.X, p0.Y, z),
                        new XYZ(p1.X, p1.Y, z),
                        new XYZ(pm.X, pm.Y, z));
                }

                // Fallback: translation if the curve is already almost horizontal.
                if (Math.Abs(p0.Z - p1.Z) <= 1e-6)
                {
                    var t = Transform.CreateTranslation(new XYZ(0, 0, z - p0.Z));
                    return curve.CreateTransformed(t);
                }
            }
            catch
            {
                // ignore
            }

            return curve;
        }

        private static double ComputeProjectedArea(IList<Curve> loop)
        {
            if (loop == null || loop.Count == 0)
            {
                return 0.0;
            }

            var points = new List<XYZ>();
            foreach (var c in loop)
            {
                if (c == null)
                {
                    continue;
                }

                try
                {
                    var tess = c.Tessellate();
                    if (tess != null && tess.Count > 0)
                    {
                        // Avoid duplicating the first point of the next segment.
                        if (points.Count > 0)
                        {
                            tess = tess.Skip(1).ToList();
                        }

                        points.AddRange(tess);
                    }
                }
                catch
                {
                    // ignore
                }
            }

            if (points.Count < 3)
            {
                return 0.0;
            }

            // Shoelace formula in XY.
            double area2 = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                area2 += (a.X * b.Y) - (b.X * a.Y);
            }

            return area2 * 0.5;
        }
    }
}
