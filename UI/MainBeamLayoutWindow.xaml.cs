using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitAgent.Commands;
using RevitAgent.Utils;

namespace RevitAgent.UI
{
    internal partial class MainBeamLayoutWindow : Window
    {
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;

        private readonly List<ElementId> _selectedElementIds = new List<ElementId>();

        private readonly MainBeamLayoutEventHandler _eventHandler;
        private readonly ExternalEvent _externalEvent;

        internal MainBeamLayoutWindow(UIDocument uiDoc)
        {
            InitializeComponent();

            _uiDoc = uiDoc ?? throw new ArgumentNullException(nameof(uiDoc));
            _doc = _uiDoc.Document ?? throw new ArgumentNullException(nameof(_uiDoc.Document));

            _eventHandler = new MainBeamLayoutEventHandler();
            _externalEvent = ExternalEvent.Create(_eventHandler);

            UpdateSelectedColumnsText();
        }

        internal MainBeamLayoutWindow()
        {
            InitializeComponent();
        }

        private void SelectColumnsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Hide();

                if (_doc.ActiveView is not ViewPlan)
                {
                    MessageBox.Show(this, "请先激活一个结构平面视图（ViewPlan）再进行框选。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var filter = new AllowAllSelectionFilter();
                var picked = _uiDoc.Selection.PickElementsByRectangle(filter, "框选元素（可包含柱、楼板等，ESC 取消）");
                if (picked == null || picked.Count == 0)
                {
                    return;
                }

                _selectedElementIds.Clear();
                _selectedElementIds.AddRange(
                    picked
                        .Where(e => e != null)
                        .Select(e => e.Id)
                        .Where(id => id != null && id != ElementId.InvalidElementId)
                        .Distinct());

                UpdateSelectedColumnsText();
                OkButton.IsEnabled = CountStructuralColumns() >= 3 && CountFloors() == 1;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // user cancelled
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "操作失败:\n" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Show();
                Activate();
            }
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            if (CountStructuralColumns() < 3)
            {
                MessageBox.Show(this, "请至少选择 3 个结构柱（可同时框选楼板等其他元素）。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (CountFloors() > 1)
            {
                MessageBox.Show(this, "请只选择一个楼板", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (CountFloors() < 1)
            {
                MessageBox.Show(this, "请至少框选 1 个楼板，用于确定主次梁生成高度并过滤柱子。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_doc.ActiveView is not ViewPlan)
            {
                MessageBox.Show(this, "请先激活一个结构平面视图（ViewPlan）。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _eventHandler.Doc = _doc;
            _eventHandler.PickedElementIds = new List<ElementId>(_selectedElementIds);
            _eventHandler.DuplicateActivePlanView = false;
            _eventHandler.DuplicateViewNamePrefix = string.Empty;

            _externalEvent.Raise();
            
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            
            Close();
        }

        private void UpdateSelectedColumnsText()
        {
            if (SelectedColumnsTextBlock == null)
            {
                return;
            }

            if (_selectedElementIds.Count == 0)
            {
                SelectedColumnsTextBlock.Text = "尚未选择元素";
                return;
            }

            int total = _selectedElementIds.Count;
            int columns = CountStructuralColumns();
            int floors = CountFloors();
            SelectedColumnsTextBlock.Text = $"已选择元素: {total}，柱: {columns}，楼板: {floors}";
        }

        private int CountStructuralColumns()
        {
            int count = 0;
            foreach (var id in _selectedElementIds)
            {
                var element = _doc.GetElement(id);
                if (ElementClassifier.IsStructuralColumn(element))
                {
                    count++;
                }
            }
            return count;
        }

        private int CountFloors()
        {
            int count = 0;
            foreach (var id in _selectedElementIds)
            {
                var element = _doc.GetElement(id);
                if (ElementClassifier.IsFloor(element))
                {
                    count++;
                }
            }
            return count;
        }

    }
}
