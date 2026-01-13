# RevitAgent

提供一个“主梁布置”命令：
- UI（`UI/MainBeamLayoutWindow.xaml`）负责交互与选择结构柱
- 后端逻辑通过 `ExternalEvent`（`Commands/MainBeamLayoutEventHandler.cs`）在 Revit 上下文中创建视图副本并绘制模型线

## Build
1. Open `RevitAgent.sln` in Visual Studio.
2. Ensure the Revit API references point to your install path:
   - `...\RevitAPI.dll`
   - `...\RevitAPIUI.dll`
3. Build the project.

## Install
1. Copy `RevitAgent.addin` to:
   - `%AppData%\Autodesk\Revit\Addins\<YourRevitVersion>`
2. Update the `<Assembly>` path in `RevitAgent.addin` to the built DLL location.
3. Start Revit and run the command from the Add-Ins tab.
