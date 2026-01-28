using System;
using System.Reflection;
using Autodesk.Revit.UI;
using AILayoutAgent.Commands;
using AILayoutAgent.UI;

namespace AILayoutAgent
{
    public sealed class Application : IExternalApplication
    {
        private const string TabName = "AILayoutAgent";
        private const string PanelName = "Commands";

        private static AItestMainWindow _mainWindow;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(TabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Tab already exists.
            }

            var panel = application.CreateRibbonPanel(TabName, PanelName);
            var assemblyPath = Assembly.GetExecutingAssembly().Location;

            var windowButton = new PushButtonData(
                "AILayoutAgent_ShowWindow",
                "AI\n框选筛选",
                assemblyPath,
                typeof(ShowWindowCommand).FullName);

            panel.AddItem(windowButton);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                if (_mainWindow != null)
                {
                    _mainWindow.Close();
                }
            }
            catch
            {
                // ignore
            }

            _mainWindow = null;
            return Result.Succeeded;
        }

        public static void ShowMainWindow(UIApplication uiApp)
        {
            if (uiApp == null)
            {
                return;
            }

            if (_mainWindow == null || !_mainWindow.IsLoaded)
            {
                _mainWindow = new AItestMainWindow();
                _mainWindow.Closed += (_, __) => { _mainWindow = null; };
                _mainWindow.SetUIApplication(uiApp);
                _mainWindow.Show();
            }
            else
            {
                _mainWindow.Activate();
            }
        }
    }
}
