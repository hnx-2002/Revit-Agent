using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
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
        public int PlacedBeamCount { get; internal set; }
        public int MissingBeamTypeCount { get; internal set; }
        public string MissingBeamTypeNamesPreview { get; internal set; }
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
                if (ElementClassifier.IsConcreteRectColumn(element))
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
                result.ErrorMessage = "请至少框选 1 个楼板，用于提取外边界与孔洞。";
                return result;
            }

            if (!ViewPlanUtils.TryGetPlanViewZ(doc, targetPlan, out double viewZ))
            {
                result.ErrorMessage = "无法从当前平面视图获取高度（Level 解析失败）。";
                return result;
            }

            var previewLoops = new List<FloorBoundaryPreview.TaggedLoop>();
            foreach (var floorId in floorIds)
            {
                var floorElement = doc.GetElement(floorId);
                var boundary = FloorBoundaryPreview.GetTopFaceBoundaryLoops(floorElement, doc, forceZ: viewZ);
                if (!string.IsNullOrWhiteSpace(boundary.ErrorMessage))
                {
                    result.ErrorMessage = boundary.ErrorMessage;
                    return result;
                }

                for (int i = 0; i < boundary.Loops.Count; i++)
                {
                    previewLoops.Add(new FloorBoundaryPreview.TaggedLoop
                    {
                        IsOuter = i == 0,
                        Curves = boundary.Loops[i]
                    });
                }
            }

            var columnIds = columnCandidateIds.Distinct().ToList();

            result.ColumnCount = columnIds.Count;
            if (columnIds.Count < 3)
            {
                result.ErrorMessage = "可用混凝土矩形柱不足 3 个（族名需以“结构_柱_矩形混凝土柱”开头，且当前平面视图高度需落在柱底/柱顶标高范围内）。";
                return result;
            }

            var points = CollectColumnPoints(doc, columnIds, viewZ);
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

            double z = viewZ;

            BeamPlacementExecutionResult beamPlacementResult = null;
            using (var tx = new Transaction(doc, "RevitAgent - Beam layout"))
            {
                tx.Start();

                var store = BeamLayoutStore.GetOrCreate(doc);
                store.MainBeamCurveIds.Clear();
                store.SecondaryBeamCurveIds.Clear();
                store.BeamPlacements.Clear();
                store.PlacedBeamInstanceIds.Clear();

                FloorBoundaryPreview.DrawPreviewCurves(doc, previewLoops, z, "RevitAgent-FloorBoundary");

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
                    store.BeamPlacements.Add(new BeamPlacementInfo
                    {
                        Role = BeamRole.Main,
                        Start = line.GetEndPoint(0),
                        End = line.GetEndPoint(1),
                    });
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
                        store.BeamPlacements.Add(new BeamPlacementInfo
                        {
                            Role = BeamRole.Secondary,
                            Start = segment.GetEndPoint(0),
                            End = segment.GetEndPoint(1),
                        });
                    }
                }

                beamPlacementResult = PlaceRealBeams(doc, targetPlan, store.BeamPlacements, store.PlacedBeamInstanceIds);
                if (beamPlacementResult.MissingTypeNames.Count == 0)
                {
                    var guideIds = new List<ElementId>();
                    guideIds.AddRange(store.MainBeamCurveIds);
                    guideIds.AddRange(store.SecondaryBeamCurveIds);
                    if (guideIds.Count > 0)
                    {
                        try
                        {
                            doc.Delete(guideIds);
                            store.MainBeamCurveIds.Clear();
                            store.SecondaryBeamCurveIds.Clear();
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                }

                tx.Commit();

                result.FaceCount = facesResult.FacesCount;
                result.MainBeamCount = store.BeamPlacements.Count(x => x.Role == BeamRole.Main);
                result.SecondaryBeamCount = store.BeamPlacements.Count(x => x.Role == BeamRole.Secondary);
                result.PlacedBeamCount = store.PlacedBeamInstanceIds.Count;
                result.MissingBeamTypeCount = beamPlacementResult.MissingTypeNames.Count;
                result.MissingBeamTypeNamesPreview = beamPlacementResult.MissingTypeNamesPreview;
            }

            return result;
        }

        private sealed class BeamPlacementExecutionResult
        {
            public List<string> MissingTypeNames { get; } = new List<string>();
            public string MissingTypeNamesPreview { get; set; }
        }

        private static BeamPlacementExecutionResult PlaceRealBeams(
            Document doc,
            ViewPlan targetPlan,
            IList<BeamPlacementInfo> placements,
            IList<ElementId> placedInstanceIds)
        {
            var exec = new BeamPlacementExecutionResult();
            if (doc == null || targetPlan == null)
            {
                return exec;
            }

            if (placements == null || placements.Count == 0)
            {
                return exec;
            }

            var level = targetPlan.GenLevel ?? doc.GetElement(targetPlan.LevelId) as Level;
            if (level == null)
            {
                return exec;
            }

            var symbols = CollectConcreteBeamSymbols(doc);
            if (symbols.Count == 0)
            {
                // No candidates; treat all as missing to keep guide lines for review.
                foreach (var p in placements)
                {
                    if (p == null)
                    {
                        continue;
                    }

                    var name = ComputeBeamTypeName(p.Role, p.Length);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        exec.MissingTypeNames.Add(name);
                    }
                }

                exec.MissingTypeNamesPreview = FormatMissingTypeNamesPreview(exec.MissingTypeNames);
                return exec;
            }

            var byNormalizedName = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in symbols)
            {
                var key = NormalizeBeamTypeName(s?.Name);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!byNormalizedName.ContainsKey(key))
                {
                    byNormalizedName[key] = s;
                }
            }

            var missingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var placement in placements)
            {
                if (placement == null || placement.Start == null || placement.End == null)
                {
                    continue;
                }

                var line = Line.CreateBound(placement.Start, placement.End);
                string typeName = ComputeBeamTypeName(placement.Role, line.Length);
                var normalizedTypeName = NormalizeBeamTypeName(typeName);

                if (!byNormalizedName.TryGetValue(normalizedTypeName, out var symbol) || symbol == null)
                {
                    missingNames.Add(typeName);
                    continue;
                }

                try
                {
                    if (!symbol.IsActive)
                    {
                        symbol.Activate();
                        doc.Regenerate();
                    }

                    var beam = doc.Create.NewFamilyInstance(line, symbol, level, StructuralType.Beam);
                    TagElement(beam, placement.Role == BeamRole.Main ? "RevitAgent-MainBeam" : "RevitAgent-SecondaryBeam");

                    double offset = placement.Start.Z - level.ProjectElevation;
                    TrySetInstanceOffsetParams(beam, offset);

                    if (beam != null)
                    {
                        placedInstanceIds?.Add(beam.Id);
                    }
                }
                catch
                {
                    missingNames.Add(typeName);
                }
            }

            exec.MissingTypeNames.AddRange(missingNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            exec.MissingTypeNamesPreview = FormatMissingTypeNamesPreview(exec.MissingTypeNames);
            return exec;
        }

        private static List<FamilySymbol> CollectConcreteBeamSymbols(Document doc)
        {
            if (doc == null)
            {
                return new List<FamilySymbol>();
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .Cast<FamilySymbol>()
                .Where(IsConcreteBeamSymbol)
                .ToList();
        }

        private static bool IsConcreteBeamSymbol(FamilySymbol symbol)
        {
            if (symbol == null)
            {
                return false;
            }

            try
            {
                var familyName = symbol.FamilyName ?? symbol.Family?.Name ?? string.Empty;
                return familyName.IndexOf("混凝土梁", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static string ComputeBeamTypeName(BeamRole role, double lengthFeet)
        {
            double lengthMm = FeetToMm(lengthFeet);
            double lengthM = lengthMm / 1000.0;

            if (role == BeamRole.Main)
            {
                int widthMm = lengthM < 6.5 ? 250 : 300;
                int heightMm = RoundUpToMultiple((int)Math.Ceiling(lengthMm / 12.0), 50);
                return $"{widthMm}*{heightMm}";
            }

            int secondaryHeightMm = RoundUpToMultiple((int)Math.Ceiling(lengthMm / 15.0), 50);
            if (secondaryHeightMm < 300)
            {
                secondaryHeightMm = 300;
            }

            int secondaryWidthMm;
            if (secondaryHeightMm <= 700)
            {
                secondaryWidthMm = 250;
            }
            else
            {
                secondaryWidthMm = RoundUpToMultiple((int)Math.Ceiling(secondaryHeightMm / 3.0), 50);
            }

            return $"{secondaryWidthMm}*{secondaryHeightMm}";
        }

        private static string NormalizeBeamTypeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var s = name.Trim()
                .Replace(" ", string.Empty)
                .Replace("×", "*")
                .Replace("x", "*")
                .Replace("X", "*");

            while (s.Contains("**"))
            {
                s = s.Replace("**", "*");
            }

            return s;
        }

        private static int RoundUpToMultiple(int value, int multiple)
        {
            if (multiple <= 0)
            {
                return value;
            }

            int rem = value % multiple;
            if (rem == 0)
            {
                return value;
            }

            return value + (multiple - rem);
        }

        private static double FeetToMm(double feet)
        {
            const double mmPerFoot = 304.8;
            return feet * mmPerFoot;
        }

        private static void TagElement(Element element, string tag)
        {
            if (element == null || string.IsNullOrWhiteSpace(tag))
            {
                return;
            }

            try
            {
                var p = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
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

        private static void TrySetInstanceOffsetParams(FamilyInstance beam, double offsetFromLevel)
        {
            if (beam == null)
            {
                return;
            }

            TrySetDoubleParam(beam, BuiltInParameter.INSTANCE_ELEVATION_PARAM, offsetFromLevel);
            TrySetDoubleParam(beam, BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION, offsetFromLevel);
            TrySetDoubleParam(beam, BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION, offsetFromLevel);
        }

        private static void TrySetDoubleParam(Element element, BuiltInParameter bip, double value)
        {
            if (element == null)
            {
                return;
            }

            try
            {
                var p = element.get_Parameter(bip);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                {
                    p.Set(value);
                }
            }
            catch
            {
                // ignore
            }
        }

        private static string FormatMissingTypeNamesPreview(IList<string> names)
        {
            if (names == null || names.Count == 0)
            {
                return string.Empty;
            }

            const int limit = 10;
            var preview = names.Take(limit).ToList();
            var suffix = names.Count > limit ? $" ...(+{names.Count - limit})" : string.Empty;
            return string.Join(", ", preview) + suffix;
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

        private static List<XYZ> CollectColumnPoints(Document doc, IList<ElementId> ids, double viewZ)
        {
            var points = new List<XYZ>();
            foreach (var id in ids)
            {
                var element = doc.GetElement(id);
                if (ElementClassifier.TryGetColumnPointAtZ(doc, element, viewZ, out XYZ placementPoint))
                {
                    points.Add(placementPoint);
                }
            }
            return points;
        }
    }
}
