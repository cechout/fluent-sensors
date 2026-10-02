using System;
using System.Diagnostics;


namespace FluentSensors.Diagnostics
{
    // the debug timer:
    // ad hoc timing of slow spots; a using block logs its elapsed time through Debug.WriteLine, nested scopes indent
    public sealed class DebugTiming : IDisposable
    {
        // === fields ===

        [ThreadStatic]
        private static int _depth;

        private readonly string _label;
        private readonly Stopwatch _stopwatch;


        // === constructor ===

        private DebugTiming(string label)
        {
            _label = label;
            _stopwatch = Stopwatch.StartNew();

            Debug.WriteLine($"{Indent()}-> {_label}");
            _depth++;
        }


        // === public api ===

        // usage: using (DebugTiming.Scope("PerformancePage load")) { ... }
        public static DebugTiming Scope(string label) => new DebugTiming(label);

        // one checkpoint without a duration, for when a line runs relative to the rest
        public static void Mark(string label) => Debug.WriteLine($"{Indent()}. {label}");

        public void Dispose()
        {
            _depth--;
            _stopwatch.Stop();
            Debug.WriteLine($"{Indent()}<- {_label}: {_stopwatch.ElapsedMilliseconds}ms");
        }


        // === private helpers ===

        private static string Indent() => new string(' ', Math.Max(_depth, 0) * 2);
    }
}
