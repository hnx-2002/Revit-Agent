using System;
using System.Reflection;
using Autodesk.Revit.UI;

namespace RevitAgent
{
    public class Application : IExternalApplication
    {
        private const string TabName = "RevitAgent";
        private const string PanelName = "Commands";

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

            RibbonPanel panel = application.CreateRibbonPanel(TabName, PanelName);
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            PushButtonData buttonData = new PushButtonData(
                "RevitAgent_ReadCoords",
                "主梁\n布置",
                assemblyPath,
                "RevitAgent.Commands.MainBeamLayoutCommand");

            panel.AddItem(buttonData);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
