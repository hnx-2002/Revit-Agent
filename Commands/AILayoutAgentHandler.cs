using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using AILayoutAgent.Utils;

namespace AILayoutAgent.Commands
{
    public sealed class AILayoutAgentHandler : IExternalEventHandler
    {
        public Action<string> StatusCallback { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = app?.ActiveUIDocument;
                var doc = uiDoc?.Document;
                if (uiDoc == null || doc == null)
                {
                    TaskDialog.Show("AILayoutAgent", "当前没有可用的 Revit 文档。");
                    return;
                }

                var plan = uiDoc.ActiveView as ViewPlan;
                if (plan == null)
                {
                    TaskDialog.Show("AILayoutAgent", "请先激活一个平面视图（ViewPlan）。");
                    return;
                }

                if (!ViewPlanUtils.TryGetLayoutZFromViewNameMeters(plan, out var layoutZ, out var _, out var heightLabel, out var err))
                {
                    TaskDialog.Show("AILayoutAgent", err ?? "无法从当前平面名称解析标高。");
                    return;
                }

                StatusCallback?.Invoke($"当前视图：{plan.Name}\n标高：{heightLabel}m");

                IList<Element> picked;
                try
                {
                    picked = uiDoc.Selection.PickElementsByRectangle(
                        new AllowAllSelectionFilter(),
                        "请在视图中框选范围（包含柱、楼板、洞口线、荷载线）。");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    StatusCallback?.Invoke("用户取消框选。");
                    return;
                }

                if (picked == null || picked.Count == 0)
                {
                    StatusCallback?.Invoke("未选择任何元素。");
                    return;
                }

                const double zTol = 1e-2; // feet
                var columnIds = new List<ElementId>();
                var floorIds = new List<ElementId>();
                var openingLineIds = new List<ElementId>();
                var loadLineIds = new List<ElementId>();

                foreach (var e in picked)
                {
                    if (e == null) continue;

                    if (ElementClassifier.IsConcreteRectColumn(e) &&
                        ElementClassifier.TryGetColumnVerticalRange(doc, e, out var minZ, out var maxZ) &&
                        layoutZ > minZ + 1e-6 && layoutZ <= maxZ + 1e-6)
                    {
                        columnIds.Add(e.Id);
                        continue;
                    }

                    if (ElementClassifier.IsFloor(e) &&
                        ElementClassifier.TryGetElementTopZ(e, out var topZ) &&
                        Math.Abs(topZ - layoutZ) <= zTol)
                    {
                        floorIds.Add(e.Id);
                        continue;
                    }

                    if (ElementClassifier.IsOpeningGuideCurveElement(e) &&
                        ElementClassifier.TryGetCurveMidZ(e, out var midZ0) &&
                        Math.Abs(midZ0 - layoutZ) <= zTol)
                    {
                        openingLineIds.Add(e.Id);
                        continue;
                    }

                    if (ElementClassifier.IsLoadGuideCurveElement(e) &&
                        ElementClassifier.TryGetCurveMidZ(e, out var midZ1) &&
                        Math.Abs(midZ1 - layoutZ) <= zTol)
                    {
                        loadLineIds.Add(e.Id);
                        continue;
                    }
                }

                columnIds = columnIds.Distinct().ToList();
                floorIds = floorIds.Distinct().ToList();
                openingLineIds = openingLineIds.Distinct().ToList();
                loadLineIds = loadLineIds.Distinct().ToList();

                var sb = new StringBuilder();
                sb.AppendLine($"视图：{plan.Name}");
                sb.AppendLine($"解析标高：{heightLabel}m（{layoutZ:0.###} ft）");
                sb.AppendLine($"柱：{columnIds.Count}；楼板：{floorIds.Count}；洞口线：{openingLineIds.Count}；荷载线：{loadLineIds.Count}");
                sb.AppendLine();

                sb.AppendLine("柱：");
                foreach (var id in columnIds)
                {
                    var e = doc.GetElement(id);
                    string familyType = TryGetFamilyTypeLabel(e);

                    string xyLabel = "XY=?";
                    if (ElementClassifier.TryGetColumnPointAtZ(doc, e, layoutZ, out var p))
                    {
                        var xMm = RevitUnitUtils.FeetToMillimeters(p.X);
                        var yMm = RevitUnitUtils.FeetToMillimeters(p.Y);
                        xyLabel = $"XY=({xMm:0.#},{yMm:0.#})mm";
                    }

                    string bhLabel = "b×h=?";
                    if (ElementClassifier.TryGetColumnRectSizeFromSymbol(e, out var b, out var h))
                    {
                        var bMm = RevitUnitUtils.FeetToMillimeters(b);
                        var hMm = RevitUnitUtils.FeetToMillimeters(h);
                        bhLabel = $"b×h={bMm:0.#}×{hMm:0.#}mm";
                    }

                    sb.AppendLine($"- {id.IntegerValue} | {familyType} | {xyLabel} | {bhLabel}");
                }

                sb.AppendLine();
                sb.AppendLine("楼板：");
                foreach (var id in floorIds)
                {
                    var e = doc.GetElement(id);
                    sb.AppendLine($"- {id.IntegerValue} | {e?.Name}");
                }
                
                // TaskDialog.Show("AILayoutAgent - Dify", sb.ToString());

                StatusCallback?.Invoke("正在调用 Dify（blocking）...");
                
                var answer = DifyClient.SendChatMessageBlocking(sb.ToString());

                TaskDialog.Show("AILayoutAgent - Dify", answer);
                StatusCallback?.Invoke("完成。");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("AILayoutAgent", "执行异常：\n" + ex);
            }
        }

        public string GetName() => "AILayoutAgent - SelectAndShow";

        private static string TryGetFamilyTypeLabel(Element element)
        {
            if (element is not FamilyInstance fi)
            {
                return element?.Name ?? "?";
            }

            try
            {
                var familyName = fi.Symbol?.FamilyName ?? fi.Symbol?.Family?.Name ?? "?";
                var typeName = fi.Symbol?.Name ?? "?";
                return $"{familyName}:{typeName}";
            }
            catch
            {
                return element?.Name ?? "?";
            }
        }
    }
}
