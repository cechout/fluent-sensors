using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;


namespace FluentSensors.Features.Start
{
    // one row of the system snapshot: which hardware category it belongs to, the device that was found, and a
    // handful of static facts about it
    //
    // a list rather than one computed property per fact (the idiom the hardware detail views use), because
    // several categories produce more than one row on a real machine: two GPUs, three drives, four adapters
    public record SystemSnapshotEntry(
        string IconGlyph,
        SolidColorBrush IconBrush,
        string Category, // the shared HardwareGroupInfo label, e.g. "CPU"
        string Title, // the device name itself
        IReadOnlyList<string> Details
    );
}
