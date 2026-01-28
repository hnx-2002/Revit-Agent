using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using AILayoutAgent.Commands;

namespace AILayoutAgent.UI
{
    public partial class AItestMainWindow : Window
    {
        private ExternalEvent _externalEvent;
        private Commands.AILayoutAgentHandler _handler;
        private bool _windowOwnerInitialized;

        public AItestMainWindow()
        {
            InitializeComponent();
        }

        public void SetUIApplication(UIApplication uiApp)
        {
            TryInitOwner(uiApp);
            if (_externalEvent != null)
            {
                return;
            }

            _handler = new Commands.AILayoutAgentHandler
            {
                StatusCallback = SetStatus,
                DifyDeltaCallback = AppendDifyOutput,
                UiStateCallback = ApplyUiState
            };
            _externalEvent = ExternalEvent.Create(_handler);

            SetStatus("准备就绪。");
            ApplyUiState(new AILayoutAgentUiState(AILayoutAgentState.Idle, false));
        }

        private void TryInitOwner(UIApplication uiApp)
        {
            if (_windowOwnerInitialized || uiApp == null)
            {
                return;
            }

            try
            {
                var helper = new WindowInteropHelper(this);
                helper.Owner = Process.GetCurrentProcess().MainWindowHandle;
                _windowOwnerInitialized = true;
            }
            catch
            {
                // ignore
            }
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ClearDifyOutput();
                _handler?.SetNextAction(AILayoutAgentAction.Select);
                _externalEvent?.Raise();
            }
            catch (Exception ex)
            {
                SetStatus("Raise 失败：" + ex.Message);
            }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ClearDifyOutput();
                _handler?.SetNextAction(AILayoutAgentAction.Confirm);
                _externalEvent?.Raise();
            }
            catch (Exception ex)
            {
                SetStatus("Raise 失败：" + ex.Message);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _handler?.SetNextAction(AILayoutAgentAction.Cancel);
                _externalEvent?.Raise();
            }
            catch (Exception ex)
            {
                SetStatus("Raise 失败：" + ex.Message);
            }
        }

        private void DrawButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _handler?.SetNextAction(AILayoutAgentAction.Draw);
                _externalEvent?.Raise();
            }
            catch (Exception ex)
            {
                SetStatus("Raise 失败：" + ex.Message);
            }
        }

        private void SetStatus(string text)
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => SetStatus(text)));
                    return;
                }

                StatusText.Text = text ?? string.Empty;
            }
            catch
            {
                // ignore
            }
        }

        private void ApplyUiState(AILayoutAgentUiState state)
        {
            try
            {
                if (state == null) return;

                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => ApplyUiState(state)));
                    return;
                }

                SelectButton.IsEnabled = state.CanSelect;
                ConfirmButton.IsEnabled = state.CanConfirm;
                CancelButton.IsEnabled = state.CanCancel;
                DrawButton.IsEnabled = state.CanDraw;
            }
            catch
            {
                // ignore
            }
        }

        private void AppendDifyOutput(string delta)
        {
            try
            {
                if (string.IsNullOrEmpty(delta)) return;

                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => AppendDifyOutput(delta)));
                    return;
                }

                DifyOutput.AppendText(delta);
                DifyOutput.ScrollToEnd();
            }
            catch
            {
                // ignore
            }
        }

        private void ClearDifyOutput()
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ClearDifyOutput));
                    return;
                }

                DifyOutput.Clear();
            }
            catch
            {
                // ignore
            }
        }
    }
}
