using System;
using System.Diagnostics;


namespace FluentSensors.Diagnostics
{
    // ad-hoc timing helper for empirically isolating slow spots, wrap a block in a using statement and the
    // elapsed time gets logged via Debug.WriteLine when the block ends
    // Nested Scope() calls indent automatically so a sub-step reads as nested under whichever Scope() its running
    // inside
    public sealed class DebugTiming : IDisposable
    {
        // === Fields ===

        [ThreadStatic]
        private static int _depth;

        private readonly string _label;
        private readonly Stopwatch _stopwatch;


        // === Constructor ===

        private DebugTiming(string label)
        {
            _label = label;
            _stopwatch = Stopwatch.StartNew();

            Debug.WriteLine($"{Indent()}-> {_label}");
            _depth++;
        }


        // === Public Binding Surface ===

        // usage: using (DebugTiming.Scope("PerformancePage load")) { ... }
        public static DebugTiming Scope(string label) => new DebugTiming(label);

        // single timestamped checkpoint, for pinpointing when a specific line runs relative to everything else,
        // without measuring a duration
        public static void Mark(string label) => Debug.WriteLine($"{Indent()}. {label}");

        public void Dispose()
        {
            _depth--;
            _stopwatch.Stop();
            Debug.WriteLine($"{Indent()}<- {_label}: {_stopwatch.ElapsedMilliseconds}ms");
        }


        // === Private Helpers ===

        private static string Indent() => new string(' ', Math.Max(_depth, 0) * 2);
    }
}
