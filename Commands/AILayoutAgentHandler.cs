using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using AILayoutAgent.Utils;
using AILayoutAgent.Client;

namespace AILayoutAgent.Commands
{
    public enum AILayoutAgentAction
    {
        Select = 0,
        Confirm = 1,
        Cancel = 2,
        Draw = 3
    }

    public enum AILayoutAgentState
    {
        Idle = 0,
        PendingConfirm = 1,
        Running = 2
    }

    public sealed class AILayoutAgentUiState
    {
        public AILayoutAgentState State { get; }
        public bool CanSelect { get; }
        public bool CanConfirm { get; }
        public bool CanCancel { get; }
        public bool CanDraw { get; }

        public AILayoutAgentUiState(AILayoutAgentState state, bool hasDrawableOutput)
        {
            State = state;
            CanSelect = state != AILayoutAgentState.Running;
            CanConfirm = state == AILayoutAgentState.PendingConfirm;
            CanCancel = state != AILayoutAgentState.Idle;
            CanDraw = state == AILayoutAgentState.Idle && hasDrawableOutput;
        }
    }

    public sealed class AILayoutAgentHandler : IExternalEventHandler
    {
        [DataContract]
        private sealed class MainBeamsRoot
        {
            [DataMember(Name = "main_beams")]
            public List<BeamSegment> MainBeams { get; set; }
        }

        [DataContract]
        private sealed class BeamSegment
        {
            [DataMember(Name = "start")]
            public Point2D Start { get; set; }

            [DataMember(Name = "end")]
            public Point2D End { get; set; }
        }

        [DataContract]
        private sealed class Point2D
        {
            [DataMember(Name = "x")]
            public double X { get; set; }

            [DataMember(Name = "y")]
            public double Y { get; set; }
        }

        public Action<string> StatusCallback { get; set; }
        public Action<string> DifyDeltaCallback { get; set; }
        public Action<AILayoutAgentUiState> UiStateCallback { get; set; }

        private readonly object _gate = new object();
        private AILayoutAgentAction _nextAction = AILayoutAgentAction.Select;
        private AILayoutAgentState _state = AILayoutAgentState.Idle;
        private string _pendingQuery;
        private double _pendingLayoutZ;
        private CancellationTokenSource _runCts;
        private List<BeamSegment> _lastMainBeams;
        private double _lastLayoutZ;

        public void SetNextAction(AILayoutAgentAction action)
        {
            lock (_gate)
            {
                _nextAction = action;
            }
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var action = ConsumeNextAction();
                if (action == AILayoutAgentAction.Cancel)
                {
                    CancelCurrentOrPending();
                    return;
                }

                if (action == AILayoutAgentAction.Draw)
                {
                    DrawLastMainBeams(app);
                    return;
                }

                if (action == AILayoutAgentAction.Confirm)
                {
                    ConfirmAndRunDify();
                    return;
                }

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

                if (GetState() == AILayoutAgentState.Running)
                {
                    StatusCallback?.Invoke("正在运行中，请等待完成或点击“取消”。");
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
                
                SetPendingQuery(sb.ToString(), layoutZ);

                StatusCallback?.Invoke(
                    "已生成请求。\n" +
                    "点击“确定”开始调用 Dify（streaming），或点击“取消”丢弃。\n" +
                    $"柱：{columnIds.Count}；楼板：{floorIds.Count}；洞口线：{openingLineIds.Count}；荷载线：{loadLineIds.Count}");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("AILayoutAgent", "执行异常：\n" + ex);
            }
        }

        public string GetName() => "AILayoutAgent - SelectAndShow";

        private AILayoutAgentAction ConsumeNextAction()
        {
            lock (_gate)
            {
                var a = _nextAction;
                _nextAction = AILayoutAgentAction.Select;
                return a;
            }
        }

        private AILayoutAgentState GetState()
        {
            lock (_gate)
            {
                return _state;
            }
        }

        private void NotifyUiState()
        {
            AILayoutAgentState state;
            bool hasOutput;
            lock (_gate)
            {
                state = _state;
                hasOutput = _lastMainBeams != null && _lastMainBeams.Count > 0;
            }

            try { UiStateCallback?.Invoke(new AILayoutAgentUiState(state, hasOutput)); } catch { /* ignore */ }
        }

        private void SetPendingQuery(string query, double layoutZ)
        {
            lock (_gate)
            {
                _pendingQuery = query;
                _pendingLayoutZ = layoutZ;
                _lastMainBeams = null;
                _lastLayoutZ = 0;
                _state = AILayoutAgentState.PendingConfirm;
            }

            NotifyUiState();
        }

        private void CancelCurrentOrPending()
        {
            CancellationTokenSource cts;
            lock (_gate)
            {
                cts = _runCts;
                _runCts = null;
                _pendingQuery = null;
                _pendingLayoutZ = 0;
                _state = AILayoutAgentState.Idle;
            }

            try { cts?.Cancel(); } catch { /* ignore */ }
            StatusCallback?.Invoke("已取消。");
            NotifyUiState();
        }

        private void ConfirmAndRunDify()
        {
            string query;
            double layoutZ;
            CancellationTokenSource cts;

            lock (_gate)
            {
                if (_state != AILayoutAgentState.PendingConfirm || string.IsNullOrWhiteSpace(_pendingQuery))
                {
                    StatusCallback?.Invoke("当前没有待确认的请求，请先框选。");
                    return;
                }

                query = _pendingQuery;
                layoutZ = _pendingLayoutZ;
                _pendingQuery = null;
                _pendingLayoutZ = 0;

                try { _runCts?.Cancel(); } catch { /* ignore */ }
                _runCts = new CancellationTokenSource();
                cts = _runCts;

                _state = AILayoutAgentState.Running;
            }

            NotifyUiState();
            StatusCallback?.Invoke("正在调用 Dify（streaming）...");

            Task.Run(async () =>
            {
                var anyDelta = false;
                var parsedOk = false;
                string result;
                try
                {
                    result = await DifyClient.SendChatMessageStreamingAsync(
                        query,
                        delta =>
                        {
                            if (string.IsNullOrEmpty(delta)) return;
                            anyDelta = true;
                            DifyDeltaCallback?.Invoke(delta);
                        },
                        cts.Token);
                }
                catch (OperationCanceledException)
                {
                    result = null;
                }
                catch (Exception ex)
                {
                    result = "Dify 调用异常：\n" + ex;
                }

                if (!cts.IsCancellationRequested && !string.IsNullOrWhiteSpace(result) && !anyDelta)
                {
                    try { DifyDeltaCallback?.Invoke(result); } catch { /* ignore */ }
                }

                if (!cts.IsCancellationRequested && !string.IsNullOrWhiteSpace(result))
                {
                    parsedOk = TryParseAndStoreMainBeams(result, layoutZ);
                }

                lock (_gate)
                {
                    if (ReferenceEquals(_runCts, cts))
                    {
                        _runCts = null;
                    }

                    _state = AILayoutAgentState.Idle;
                }

                NotifyUiState();

                if (cts.IsCancellationRequested)
                {
                    StatusCallback?.Invoke("已取消运行。");
                }
                else if (!string.IsNullOrWhiteSpace(result) &&
                         (result.StartsWith("Dify 请求失败") ||
                          result.StartsWith("Dify 流式返回错误") ||
                          result.StartsWith("Dify 调用异常")))
                {
                    StatusCallback?.Invoke("Dify 返回错误，详见输出。");
                }
                else
                {
                    if (!parsedOk)
                    {
                        StatusCallback?.Invoke("完成。");
                    }
                }
            });
        }

        private bool TryParseAndStoreMainBeams(string text, double layoutZ)
        {
            try
            {
                var json = TryExtractFirstJsonObject(text);
                if (string.IsNullOrWhiteSpace(json))
                {
                    StatusCallback?.Invoke("未找到可解析的 JSON（需要包含 main_beams）。");
                    return false;
                }

                var root = Deserialize<MainBeamsRoot>(json);
                var beams = root?.MainBeams;
                if (beams == null || beams.Count == 0)
                {
                    StatusCallback?.Invoke("已完成，但未解析到 main_beams。");
                    return false;
                }

                lock (_gate)
                {
                    _lastMainBeams = beams;
                    _lastLayoutZ = layoutZ;
                }

                NotifyUiState();
                StatusCallback?.Invoke($"已解析 main_beams：{beams.Count} 条，可点击“画线”。");
                return true;
            }
            catch (Exception ex)
            {
                StatusCallback?.Invoke("解析 main_beams 失败：" + ex.Message);
                return false;
            }
        }

        private void DrawLastMainBeams(UIApplication app)
        {
            try
            {
                List<BeamSegment> beams;
                double z;
                lock (_gate)
                {
                    beams = _lastMainBeams;
                    z = _lastLayoutZ;
                }

                if (beams == null || beams.Count == 0)
                {
                    StatusCallback?.Invoke("没有可画的 main_beams，请先运行 Dify。");
                    return;
                }

                var uiDoc = app?.ActiveUIDocument;
                var doc = uiDoc?.Document;
                var view = uiDoc?.ActiveView;
                if (doc == null || view == null)
                {
                    StatusCallback?.Invoke("当前没有可用的 Revit 文档/视图。");
                    return;
                }

                if (view is not ViewPlan)
                {
                    StatusCallback?.Invoke("请在平面视图（ViewPlan）中画线。");
                    return;
                }

                var created = 0;
                using (var t = new Transaction(doc, "AILayoutAgent - Draw main_beams"))
                {
                    t.Start();
                    foreach (var b in beams)
                    {
                        if (b?.Start == null || b.End == null) continue;

                        var x0 = RevitUnitUtils.MillimetersToFeet(b.Start.X);
                        var y0 = RevitUnitUtils.MillimetersToFeet(b.Start.Y);
                        var x1 = RevitUnitUtils.MillimetersToFeet(b.End.X);
                        var y1 = RevitUnitUtils.MillimetersToFeet(b.End.Y);

                        var p0 = new XYZ(x0, y0, z);
                        var p1 = new XYZ(x1, y1, z);
                        if (p0.IsAlmostEqualTo(p1)) continue;

                        var line = Line.CreateBound(p0, p1);
                        doc.Create.NewDetailCurve(view, line);
                        created++;
                    }
                    t.Commit();
                }

                StatusCallback?.Invoke($"已画线：{created} 条。");
            }
            catch (Exception ex)
            {
                StatusCallback?.Invoke("画线失败：" + ex.Message);
            }
        }

        private static string TryExtractFirstJsonObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var start = text.IndexOf('{');
            if (start < 0) return null;

            var depth = 0;
            var inString = false;
            var escaped = false;

            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return text.Substring(start, i - start + 1);
                    }
                }
            }

            return null;
        }

        private static T Deserialize<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var serializer = new DataContractJsonSerializer(typeof(T));
            using var ms = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(json));
            return serializer.ReadObject(ms) as T;
        }

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
