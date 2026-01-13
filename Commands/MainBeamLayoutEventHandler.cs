using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitAgent.MainProcesser;

namespace RevitAgent.Commands
{
    public sealed class MainBeamLayoutEventHandler : IExternalEventHandler
    {
        public Document Doc { get; set; }
        public List<ElementId> PickedElementIds { get; set; } = new List<ElementId>();

        public bool DuplicateActivePlanView { get; set; } = false;
        public string DuplicateViewNamePrefix { get; set; } = string.Empty;

        public string GetName() => nameof(MainBeamLayoutEventHandler);

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = app?.ActiveUIDocument;
                if (uiDoc == null)
                {
                    TaskDialog.Show("RevitAgent", "未找到当前文档。");
                    return;
                }

                var doc = Doc ?? uiDoc.Document;
                if (doc == null)
                {
                    TaskDialog.Show("RevitAgent", "未找到当前文档。");
                    return;
                }

                if (doc.ActiveView is not ViewPlan activePlan)
                {
                    TaskDialog.Show("RevitAgent", "请先激活一个结构平面视图（ViewPlan）。");
                    return;
                }

                ViewPlan targetPlan = activePlan;
                if (DuplicateActivePlanView)
                {
                    targetPlan = DuplicatePlanView(doc, activePlan, DuplicateViewNamePrefix);
                    if (targetPlan == null)
                    {
                        TaskDialog.Show("RevitAgent", "复制平面视图失败。");
                        return;
                    }

                    try
                    {
                        uiDoc.ActiveView = targetPlan;
                    }
                    catch
                    {
                        // ignore UI failures
                    }
                }

                var result = BeamLayoutProcessor.LayoutMainAndSecondaryBeams(
                    doc,
                    targetPlan,
                    PickedElementIds,
                    secondarySpacingMm: 3000.0);

                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    TaskDialog.Show("RevitAgent", result.ErrorMessage);
                    return;
                }

                var missingPreview = string.IsNullOrWhiteSpace(result.MissingBeamTypeNamesPreview)
                    ? string.Empty
                    : "\n" + result.MissingBeamTypeNamesPreview;

                TaskDialog.Show(
                    "RevitAgent",
                    $"完成。\n柱: {result.ColumnCount}\n楼板: {result.FloorCount}\n面数: {result.FaceCount}\n主梁(布局): {result.MainBeamCount}\n次梁(布局): {result.SecondaryBeamCount}\n已放置梁: {result.PlacedBeamCount}\n缺少类型: {result.MissingBeamTypeCount}{missingPreview}");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                TaskDialog.Show("RevitAgent", "执行失败:\n" + ex.Message);
            }
        }

        private static ViewPlan DuplicatePlanView(Document doc, ViewPlan source, string namePrefix)
        {
            if (doc == null || source == null)
            {
                return null;
            }

            var prefix = string.IsNullOrWhiteSpace(namePrefix) ? "智能体平面" : namePrefix.Trim();

            ElementId duplicatedViewId;
            using (var tx = new Transaction(doc, "RevitAgent - Duplicate plan"))
            {
                tx.Start();
                duplicatedViewId = source.Duplicate(ViewDuplicateOption.Duplicate);
                var duplicatedView = doc.GetElement(duplicatedViewId) as View;
                if (duplicatedView != null)
                {
                    duplicatedView.Name = MakeUniqueViewName(doc, prefix);
                }
                tx.Commit();
            }

            return doc.GetElement(duplicatedViewId) as ViewPlan;
        }

        private static string MakeUniqueViewName(Document doc, string baseName)
        {
            var existing = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);

            if (!existing.Contains(baseName))
            {
                return baseName;
            }

            for (int i = 1; i < 10000; i++)
            {
                string candidate = baseName + i;
                if (!existing.Contains(candidate))
                {
                    return candidate;
                }
            }

            return baseName + Guid.NewGuid().ToString("N").Substring(0, 6);
        }
    }
}

