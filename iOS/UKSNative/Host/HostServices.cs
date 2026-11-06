// Desktop services the module sources call (MessageBox, app.config settings, dialog
// statics) mapped onto the iOS host: messages queue up for SwiftUI to show as alerts,
// settings come from the app (uks_set_setting), counters are plain statics.
using System.Collections.Concurrent;
using System.Collections.Specialized;

namespace BrainSimulator
{
    /// <summary>Notices raised by modules (MessageBox.Show, status text) for the UI to drain.</summary>
    public static class HostNotices
    {
        public static readonly ConcurrentQueue<(string Title, string Text)> Pending = new();
        public static void Post(string title, string text) => Pending.Enqueue((title ?? "", text ?? ""));
    }
}

namespace System.Windows
{
    public enum MessageBoxButton { OK, OKCancel, YesNoCancel, YesNo }
    public enum MessageBoxImage { None, Error, Hand, Stop, Question, Exclamation, Warning, Asterisk, Information }
    public enum MessageBoxResult { None, OK, Cancel, Yes, No }

    /// <summary>Non-blocking on iOS: the message is shown as an alert and the default answer returned.</summary>
    public static class MessageBox
    {
        public static MessageBoxResult Show(string text) => Show(text, "");
        public static MessageBoxResult Show(string text, string caption) => Show(text, caption, MessageBoxButton.OK);
        public static MessageBoxResult Show(string text, string caption, MessageBoxButton button, MessageBoxImage icon = MessageBoxImage.None)
        {
            BrainSimulator.HostNotices.Post(caption, text);
            return button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxResult.No : MessageBoxResult.OK;
        }
    }
}

namespace System.Configuration
{
    /// <summary>app.config appSettings; the iOS app fills these from its Settings screen.</summary>
    public static class ConfigurationManager
    {
        public static NameValueCollection AppSettings { get; } = new();
    }
}

namespace BrainSimulator.Modules
{
    /// <summary>The statics ModuleGPTInfo updates on its desktop dialog class.</summary>
    public static class ModuleGPTInfoDlg
    {
        public static int errorCount;
        public static int linkCount;
    }
}
