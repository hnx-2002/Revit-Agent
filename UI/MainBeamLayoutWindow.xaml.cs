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

                if (_doc.ActiveView is not ViewPlan activePlan)
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

                if (!ViewPlanUtils.TryGetPlanViewZ(_doc, activePlan, out double viewZ))
                {
                    MessageBox.Show(this, "无法从当前平面视图获取高度（Level 解析失败）。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                const double zTol = 1e-6;
                _selectedElementIds.Clear();
                foreach (var element in picked.Where(e => e != null))
                {
                    if (ElementClassifier.IsFloor(element))
                    {
                        _selectedElementIds.Add(element.Id);
                        continue;
                    }

                    if (!ElementClassifier.IsConcreteRectColumn(element))
                    {
                        continue;
                    }

                    if (!ElementClassifier.TryGetColumnVerticalRange(_doc, element, out double minZ, out double maxZ))
                    {
                        continue;
                    }

                    if (viewZ < minZ - zTol || viewZ > maxZ + zTol)
                    {
                        continue;
                    }

                    _selectedElementIds.Add(element.Id);
                }

                _selectedElementIds.RemoveAll(id => id == null || id == ElementId.InvalidElementId);
                var distinct = _selectedElementIds.Distinct().ToList();
                _selectedElementIds.Clear();
                _selectedElementIds.AddRange(distinct);

                UpdateSelectedColumnsText();
                OkButton.IsEnabled = CountStructuralColumns() >= 3 && CountFloors() >= 1;
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
                MessageBox.Show(this, "请至少选择 3 个混凝土矩形柱（族名以“结构_柱_矩形混凝土柱”开头，且当前平面视图高度需落在柱底/柱顶标高范围内）。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (CountFloors() < 1)
            {
                MessageBox.Show(this, "请至少框选 1 个楼板，用于提取外边界与孔洞。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                if (ElementClassifier.IsConcreteRectColumn(element))
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
