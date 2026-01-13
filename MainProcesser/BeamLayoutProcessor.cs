using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitAgent.Utils;

namespace RevitAgent.MainProcesser
{
    internal sealed class BeamLayoutResult
    {
        public int ColumnCount { get; internal set; }
        public int FloorCount { get; internal set; }
        public int FaceCount { get; internal set; }
        public int MainBeamCount { get; internal set; }
        public int SecondaryBeamCount { get; internal set; }
        public string ErrorMessage { get; internal set; }
    }

    internal static class BeamLayoutProcessor
    {
        internal static BeamLayoutResult LayoutMainAndSecondaryBeams(
            Document doc,
            ViewPlan targetPlan,
            IList<ElementId> pickedElementIds,
            double secondarySpacingMm)
        {
            var result = new BeamLayoutResult();
            if (doc == null || targetPlan == null)
            {
                result.ErrorMessage = "未找到当前文档或视图。";
                return result;
            }

            var pickedIds = (pickedElementIds ?? new List<ElementId>())
                .Where(id => id != null && id != ElementId.InvalidElementId)
                .Distinct()
                .ToList();

            var columnCandidateIds = new List<ElementId>();
            var floorIds = new List<ElementId>();
            foreach (var id in pickedIds)
            {
                var element = doc.GetElement(id);
                if (ElementClassifier.IsStructuralColumn(element))
                {
                    columnCandidateIds.Add(id);
                }

                if (ElementClassifier.IsFloor(element))
                {
                    floorIds.Add(id);
                }
            }

            result.FloorCount = floorIds.Count;

            if (floorIds.Count < 1)
            {
                result.ErrorMessage = "请至少框选 1 个楼板，用于确定主梁生成高度并过滤柱子。";
                return result;
            }

            if (!TryGetAverageTopZ(doc, floorIds, out double floorZ))
            {
                result.ErrorMessage = "无法从所选楼板获取高度（BoundingBox/Level 解析失败）。";
                return result;
            }

            const double zTol = 1e-6;
            var columnIds = new List<ElementId>();
            foreach (var id in columnCandidateIds)
            {
                var element = doc.GetElement(id);
                if (!TryGetColumnPlacementPoint(doc, element, out XYZ placementPoint))
                {
                    continue;
                }

                if (placementPoint.Z > floorZ + zTol)
                {
                    continue;
                }

                columnIds.Add(id);
            }

            result.ColumnCount = columnIds.Count;
            if (columnIds.Count < 3)
            {
                result.ErrorMessage = "过滤后可用结构柱不足 3 个（柱放置点需在楼板平面以下）。";
                return result;
            }

            var points = CollectColumnPoints(doc, columnIds);
            if (points.Count < 3)
            {
                result.ErrorMessage = "可用的柱点数量不足（柱需包含 LocationPoint）。";
                return result;
            }

            var facesResult = PlanarGraphFaces.BuildFromRelativeNeighborhoodGraph(points);
            if (facesResult.UniqueEdges.Count == 0)
            {
                result.ErrorMessage = "未能生成相邻边（相邻关系不足）。";
                return result;
            }

            double z = floorZ;

            using (var tx = new Transaction(doc, "RevitAgent - Beam layout"))
            {
                tx.Start();

                var store = BeamLayoutStore.GetOrCreate(doc);
                store.MainBeamCurveIds.Clear();
                store.SecondaryBeamCurveIds.Clear();

                var sketchPlane = CreateSketchPlaneAtZ(doc, z);

                foreach (var edge in facesResult.UniqueEdges)
                {
                    var a = points[edge.A];
                    var b = points[edge.B];
                    var line = Line.CreateBound(
                        new XYZ(a.X, a.Y, z),
                        new XYZ(b.X, b.Y, z));
                    var curve = doc.Create.NewModelCurve(line, sketchPlane);
                    TagCurve(curve, "RevitAgent-MainBeam");
                    store.MainBeamCurveIds.Add(curve.Id);
                }

                double spacing = MmToFeet(secondarySpacingMm);
                foreach (var face in facesResult.Faces ?? new List<List<int>>())
                {
                    if (face == null || face.Count < 3)
                    {
                        continue;
                    }

                    var polygon = face.Select(idx => points[idx]).ToList();
                    foreach (var segment in SecondaryBeamLayout.GenerateSegments(polygon, z, spacing))
                    {
                        var curve = doc.Create.NewModelCurve(segment, sketchPlane);
                        TagCurve(curve, "RevitAgent-SecondaryBeam");
                        store.SecondaryBeamCurveIds.Add(curve.Id);
                    }
                }

                tx.Commit();

                result.FaceCount = facesResult.FacesCount;
                result.MainBeamCount = store.MainBeamCurveIds.Count;
                result.SecondaryBeamCount = store.SecondaryBeamCurveIds.Count;
            }

            return result;
        }

        private static void TagCurve(ModelCurve curve, string tag)
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

        private static double MmToFeet(double mm)
        {
            const double mmPerFoot = 304.8;
            return mm / mmPerFoot;
        }

        private static SketchPlane CreateSketchPlaneAtZ(Document doc, double z)
        {
            var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, z));
            return SketchPlane.Create(doc, plane);
        }

        private static List<XYZ> CollectColumnPoints(Document doc, IList<ElementId> ids)
        {
            var points = new List<XYZ>();
            foreach (var id in ids)
            {
                var element = doc.GetElement(id);
                if (TryGetColumnPlacementPoint(doc, element, out XYZ placementPoint))
                {
                    points.Add(placementPoint);
                }
            }
            return points;
        }

        private static bool TryGetColumnPlacementPoint(Document doc, Element element, out XYZ placementPoint)
        {
            placementPoint = null;
            if (doc == null || element == null)
            {
                return false;
            }

            if (element.Location is not LocationPoint lp)
            {
                return false;
            }

            double x = lp.Point.X;
            double y = lp.Point.Y;

            double offset = 0.0;
            try
            {
                var offsetParam = element.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM) ??
                                  element.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
                if (offsetParam != null && offsetParam.StorageType == StorageType.Double)
                {
                    offset = offsetParam.AsDouble();
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                ElementId levelId = ElementId.InvalidElementId;

                var baseLevelParam = element.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
                if (baseLevelParam != null && baseLevelParam.StorageType == StorageType.ElementId)
                {
                    levelId = baseLevelParam.AsElementId();
                }

                if (levelId == ElementId.InvalidElementId && element.LevelId != ElementId.InvalidElementId)
                {
                    levelId = element.LevelId;
                }

                if (levelId != ElementId.InvalidElementId)
                {
                    var level = doc.GetElement(levelId) as Level;
                    if (level != null)
                    {
                        placementPoint = new XYZ(x, y, level.Elevation + offset);
                        return true;
                    }
                }
            }
            catch
            {
                // ignore
            }

            placementPoint = new XYZ(x, y, lp.Point.Z + offset);
            return true;
        }

        private static bool TryGetAverageTopZ(Document doc, IList<ElementId> elementIds, out double averageTopZ)
        {
            averageTopZ = 0.0;
            if (doc == null || elementIds == null || elementIds.Count == 0)
            {
                return false;
            }

            var zCandidates = new List<double>();
            foreach (var id in elementIds)
            {
                var element = doc.GetElement(id);
                if (element == null)
                {
                    continue;
                }

                if (TryGetElementTopZ(element, doc, out double topZ))
                {
                    zCandidates.Add(topZ);
                }
            }

            if (zCandidates.Count == 0)
            {
                return false;
            }

            averageTopZ = zCandidates.Average();
            return true;
        }

        private static bool TryGetElementTopZ(Element element, Document doc, out double topZ)
        {
            topZ = 0.0;
            if (element == null)
            {
                return false;
            }

            try
            {
                var bbox = element.get_BoundingBox(null);
                if (bbox != null)
                {
                    topZ = bbox.Max.Z;
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                if (element.LevelId != ElementId.InvalidElementId)
                {
                    var level = doc.GetElement(element.LevelId) as Level;
                    if (level != null)
                    {
                        double z = level.Elevation;

                        var offsetParam = element.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM) ??
                                          element.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
                        if (offsetParam != null && offsetParam.StorageType == StorageType.Double)
                        {
                            z += offsetParam.AsDouble();
                        }

                        topZ = z;
                        return true;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }
    }
}

