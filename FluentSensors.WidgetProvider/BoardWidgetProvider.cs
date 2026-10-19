using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;
using Windows.Storage;

using FluentSensors.Features.WidgetBoard;


namespace FluentSensors.WidgetProvider
{
    // the widgets on the board:
    // tracks every widget with its size and whether the board shows it, and pushes the card the app sent for that
    // size, or the not running note while no app is connected
    // the board calls in on its own threads, the pipe on its thread; one lock keeps both apart
    //
    // without GeneratedWinRTExposedType the CsWinRT generator writes no vtable for the class under NativeAOT, and the
    // board gets E_NOINTERFACE for IWidgetProvider, which it reports as a failed add without a reason
    [WinRT.GeneratedWinRTExposedType]
    public sealed partial class BoardWidgetProvider : IWidgetProvider
    {
        // === fields ===

        private sealed class BoardWidget
        {
            public byte Size;
            public bool IsActive;
            public string? SentTemplate; // the host keeps the last template, only a change is sent again
        }

        private readonly object _gate = new();
        private readonly Dictionary<string, BoardWidget> _widgets = new();
        private readonly Dictionary<byte, (string Template, string Data)> _cards = new(); // the latest card per size from the app

        private string _notRunningText;
        private static readonly string NotRunningTextPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "widget-provider-text.txt");
        private const string DefaultNotRunningText = "Fluent Sensors is not running";

        private const string NotRunningTemplate =
            "{\"type\":\"AdaptiveCard\",\"version\":\"1.5\",\"verticalContentAlignment\":\"Center\",\"body\":[" +
            "{\"type\":\"TextBlock\",\"text\":\"${text}\",\"wrap\":true,\"isSubtle\":true,\"horizontalAlignment\":\"Center\"}]}";


        // === singleton instance ===

        // after every other static, the constructor reads NotRunningTextPath
        public static BoardWidgetProvider Instance { get; } = new BoardWidgetProvider();


        // === constructor ===

        private BoardWidgetProvider()
        {
            _notRunningText = LoadNotRunningText();
            RecoverWidgets();
        }


        // === IWidgetProvider ===

        public void CreateWidget(WidgetContext context)
        {
            Track(context);
            Push(context.Id);
        }

        public void DeleteWidget(string widgetId, string customState)
        {
            lock (_gate) _widgets.Remove(widgetId);
            ReportActiveSizes();
        }

        public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs) { }

        public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
        {
            Track(contextChangedArgs.WidgetContext);
            Push(contextChangedArgs.WidgetContext.Id);
        }

        public void Activate(WidgetContext widgetContext)
        {
            lock (_gate)
            {
                // the board may have dropped the template while the widget was hidden
                if (_widgets.TryGetValue(widgetContext.Id, out var widget)) widget.SentTemplate = null;
            }
            Track(widgetContext, isActive: true);
            Push(widgetContext.Id);
        }

        public void Deactivate(string widgetId)
        {
            lock (_gate)
            {
                if (_widgets.TryGetValue(widgetId, out var widget)) widget.IsActive = false;
            }
            ReportActiveSizes();
        }


        // === from the app ===

        public void ApplyCard(byte size, string template, string data)
        {
            List<string> ids;
            lock (_gate)
            {
                _cards[size] = (template, data);
                ids = _widgets.Where(w => w.Value.Size == size && w.Value.IsActive).Select(w => w.Key).ToList();
            }

            foreach (var id in ids) Push(id);
        }

        public void ApplyNotRunningText(string text)
        {
            lock (_gate) _notRunningText = text;

            try { File.WriteAllText(NotRunningTextPath, text); }
            catch (Exception ex) { Log.Write("saving the not running text failed " + ex.Message); }
        }

        // the app went away: every widget falls back to the note until it connects again
        public void ClearCards()
        {
            List<string> ids;
            lock (_gate)
            {
                _cards.Clear();
                ids = _widgets.Where(w => w.Value.IsActive).Select(w => w.Key).ToList();
            }

            foreach (var id in ids) Push(id);
        }

        public byte[] GetActiveSizes()
        {
            lock (_gate) return _widgets.Values.Where(w => w.IsActive).Select(w => w.Size).Distinct().OrderBy(s => s).ToArray();
        }


        // === private helpers ===

        // a restarted provider gets no CreateWidget for the widgets the board already has
        private void RecoverWidgets()
        {
            try
            {
                var infos = WidgetManager.GetDefault().GetWidgetInfos(); // null, not empty, without widgets
                foreach (var info in infos ?? Array.Empty<WidgetInfo>())
                {
                    Track(info.WidgetContext);
                }
            }
            catch (Exception ex)
            {
                Log.Write("recovering the widgets failed " + ex.Message);
            }
        }

        private void Track(WidgetContext context, bool? isActive = null)
        {
            lock (_gate)
            {
                if (!_widgets.TryGetValue(context.Id, out var widget))
                {
                    widget = new BoardWidget();
                    _widgets[context.Id] = widget;
                }

                widget.Size = ToProtocolSize(context.Size);
                widget.IsActive = isActive ?? context.IsActive;
            }

            ReportActiveSizes();
        }

        private void Push(string widgetId)
        {
            string template, data;
            BoardWidget? widget;

            lock (_gate)
            {
                if (!_widgets.TryGetValue(widgetId, out widget) || !widget.IsActive) return;

                if (_cards.TryGetValue(widget.Size, out var card))
                {
                    (template, data) = card;
                }
                else
                {
                    template = NotRunningTemplate;
                    data = "{\"text\":\"" + JsonEncodedText.Encode(_notRunningText) + "\"}";
                }
            }

            var options = new WidgetUpdateRequestOptions(widgetId) { Data = data };
            bool isNewTemplate = template != widget.SentTemplate;
            if (isNewTemplate) options.Template = template;

            try
            {
                WidgetManager.GetDefault().UpdateWidget(options);
                if (isNewTemplate) widget.SentTemplate = template;
            }
            catch (Exception ex)
            {
                Log.Write($"update failed 0x{ex.HResult:X8} {ex.Message}");
            }
        }

        private void ReportActiveSizes() => AppPipe.SendActiveSizes(GetActiveSizes());

        private static byte ToProtocolSize(WidgetSize size) => size switch
        {
            WidgetSize.Small => WidgetBoardProtocol.Small,
            WidgetSize.Medium => WidgetBoardProtocol.Medium,
            _ => WidgetBoardProtocol.Large
        };

        private static string LoadNotRunningText()
        {
            try
            {
                if (File.Exists(NotRunningTextPath)) return File.ReadAllText(NotRunningTextPath);
            }
            catch { /* the english default below */ }

            return DefaultNotRunningText;
        }
    }
}
