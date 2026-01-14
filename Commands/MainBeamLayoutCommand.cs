using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitAgent.UI;

namespace RevitAgent.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class MainBeamLayoutCommand : IExternalCommand
    {
        private const string DefaultLayoutPlanName = "布置方案平面";
        private static MainBeamLayoutWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiDoc = commandData?.Application?.ActiveUIDocument;
                if (uiDoc == null)
                {
                    message = "No active document.";
                    return Result.Failed;
                }

                var layoutPlan = PrepareLayoutPlanView(uiDoc, DefaultLayoutPlanName);
                if (layoutPlan == null)
                {
                    return Result.Cancelled;
                }

                if (_window != null)
                {
                    try
                    {
                        if (_window.WindowState == WindowState.Minimized)
                        {
                            _window.WindowState = WindowState.Normal;
                        }

                        _window.Topmost = false;
                        _window.Activate();
                        return Result.Succeeded;
                    }
                    catch
                    {
                        _window = null;
                    }
                }

                var win = new MainBeamLayoutWindow(uiDoc)
                {
                    Title = "主次梁布置",
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Topmost = false,
                    ShowInTaskbar = false,
                };

                win.Closed += (_, __) => _window = null;

                TryAttachOwner(win);

                // Modeless: allow interacting with Revit while this window is open.
                _window = win;
                win.Show();
                win.Activate();
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static ViewPlan PrepareLayoutPlanView(UIDocument uiDoc, string targetViewName)
        {
            if (uiDoc == null)
            {
                return null;
            }

            var doc = uiDoc.Document;
            if (doc == null)
            {
                TaskDialog.Show("RevitAgent", "未找到当前文档。");
                return null;
            }

            if (doc.ActiveView is not ViewPlan activePlan)
            {
                TaskDialog.Show("RevitAgent", "请先激活一个结构平面视图（ViewPlan），再运行主次梁布置。");
                return null;
            }

            if (string.IsNullOrWhiteSpace(targetViewName))
            {
                targetViewName = DefaultLayoutPlanName;
            }

            ViewPlan duplicatedPlan;
            using (var tx = new Transaction(doc, "RevitAgent - Duplicate layout plan"))
            {
                tx.Start();

                var duplicatedViewId = activePlan.Duplicate(ViewDuplicateOption.Duplicate);
                duplicatedPlan = doc.GetElement(duplicatedViewId) as ViewPlan;

                if (duplicatedPlan == null)
                {
                    tx.RollBack();
                    TaskDialog.Show("RevitAgent", "复制平面视图失败。");
                    return null;
                }

                tx.Commit();
            }

            try
            {
                uiDoc.ActiveView = duplicatedPlan;
            }
            catch
            {
                // ignore UI failures
            }

            using (var tx = new Transaction(doc, "RevitAgent - Recreate layout plan"))
            {
                tx.Start();

                var existingSameNameViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v != null)
                    .Where(v => v.Id != duplicatedPlan.Id)
                    .Where(v => string.Equals(v.Name, targetViewName, StringComparison.OrdinalIgnoreCase))
                    .Select(v => v.Id)
                    .ToList();

                if (existingSameNameViews.Count > 0)
                {
                    doc.Delete(existingSameNameViews);
                }

                duplicatedPlan.Name = targetViewName;

                tx.Commit();
            }

            return duplicatedPlan;
        }

        private static void TryAttachOwner(Window window)
        {
            if (window == null)
            {
                return;
            }

            // Prefer AdWindows (ComponentManager.ApplicationWindow) when present, but avoid a hard compile-time dependency.
            try
            {
                var componentManagerType = Type.GetType("Autodesk.Windows.ComponentManager, AdWindows");
                var appWindowProp = componentManagerType?.GetProperty("ApplicationWindow", BindingFlags.Public | BindingFlags.Static);
                if (appWindowProp != null)
                {
                    var hwndObj = appWindowProp.GetValue(null, null);
                    if (hwndObj is IntPtr hwnd && hwnd != IntPtr.Zero)
                    {
                        new WindowInteropHelper(window).Owner = hwnd;
                        return;
                    }
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                var hwnd = Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                {
                    new WindowInteropHelper(window).Owner = hwnd;
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}

