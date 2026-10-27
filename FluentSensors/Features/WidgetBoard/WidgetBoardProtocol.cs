using System.Diagnostics;


namespace FluentSensors.Features.WidgetBoard
{
    // the pipe between FluentSensors.exe and FluentSensors.WidgetProvider.exe:
    // one message is a type byte and its fields, strings as BinaryWriter writes them
    // the provider hosts the pipe; the elevated app is the client, since a pipe it created would carry a high
    // integrity label and refuse the writes of the medium provider (no-write-up):
    // https://learn.microsoft.com/en-us/windows/win32/secauthz/mandatory-integrity-control
    // (compiled into both projects)
    public static class WidgetBoardProtocol
    {
        // per session, two signed in users each run their own pair
        public static string PipeName => $"FluentSensors.WidgetBoard.{Process.GetCurrentProcess().SessionId}";

        // in bytes; the buffers keep a write from waiting on a reader that is busy
        public const int PipeBufferSize = 64 * 1024;

        // --- message types ---
        public const byte ActiveSizes = 1; // provider to app: one byte per size shown on the board right now
        public const byte Card = 2; // app to provider: size, template, data
        public const byte NotRunningText = 3; // app to provider: the note shown while the app is closed
        public const byte ActionInvoked = 4; // provider to app: the verb of a card action

        // --- card actions ---
        public const string OpenSensorsVerb = "openSensors"; // the links under the graphs
        public const string OpenSettingsVerb = "openSettings";

        // --- sizes ---
        // the values of Microsoft.Windows.Widgets.WidgetSize, which the app does not reference
        public const byte Small = 0;
        public const byte Medium = 1;
        public const byte Large = 2;
    }
}
