using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
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
                StatusCallback = SetStatus
            };
            _externalEvent = ExternalEvent.Create(_handler);

            SetStatus("准备就绪。");
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
                StatusText.Text = text ?? string.Empty;
            }
            catch
            {
                // ignore
            }
        }
    }
}
