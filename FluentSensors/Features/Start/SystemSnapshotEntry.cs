using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Common.Sensors;


namespace FluentSensors.Features.Start
{
    // one tile of the system snapshot: which hardware category it belongs to, the device that was found, a
    // handful of static facts about it, and how many sensors LibreHardwareMonitor currently reports for it
    //
    // a list of these rather than one computed property per fact (the idiom the hardware detail views use),
    // because several categories produce more than one tile on a real machine: two GPUs, three drives, four
    // adapters
    //
    // no longer a record: the sensor count moves because LhmHardwareTreeService keeps discovering hardware after
    // the splash is gone, and the icon colour moves because the hardware colour setting can be flipped while the
    // page is open; everything else is fixed for the lifetime of the page
    public class SystemSnapshotEntry : INotifyPropertyChanged
    {
        // === fields ===

        private SolidColorBrush _iconBrush;
        private string _sensorCountText = "";
        private IReadOnlyList<string> _matchedHardwareNames = new List<string>();
        private bool _hasSensors;


        // === constructor ===

        public SystemSnapshotEntry(
            string iconGlyph,
            SolidColorBrush iconBrush,
            string category,
            string title,
            IReadOnlyList<string> details,
            HardwareGroupKind matchKind,
            string matchName)
        {
            IconGlyph = iconGlyph;
            IconBrush = iconBrush;
            Category = category;
            Title = title;
            Details = details;
            MatchKind = matchKind;
            MatchName = matchName;
        }


        // === static facts ===

        public string IconGlyph { get; }
        public string Category { get; } // the shared HardwareGroupInfo label, e.g. "CPU"
        public string Title { get; } // the device name itself
        public IReadOnlyList<string> Details { get; }

        // the glyph stays, only its colour follows the hardware colour setting, see StartViewModel.IconBrushFor
        public SolidColorBrush IconBrush
        {
            get => _iconBrush;
            set { if (_iconBrush == value) return; _iconBrush = value; OnPropertyChanged(); }
        }


        // === sensor pairing ===

        // what this tile has to be matched against on the LHM side; kept rather than a resolved instance because
        // LHM can still report hardware for the first time long after this tile was built
        public HardwareGroupKind MatchKind { get; }
        public string MatchName { get; }

        public string SensorCountText
        {
            get => _sensorCountText;
            set { if (_sensorCountText == value) return; _sensorCountText = value; OnPropertyChanged(); }
        }

        // false disables the count button; a tile LHM reports nothing for has no group to open
        public bool HasSensors
        {
            get => _hasSensors;
            set { if (_hasSensors == value) return; _hasSensors = value; OnPropertyChanged(); }
        }

        // the raw LhmHardwareInstance names this tile counted, which is what the sensors page matches against
        public IReadOnlyList<string> MatchedHardwareNames
        {
            get => _matchedHardwareNames;
            set { _matchedHardwareNames = value ?? new List<string>(); }
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
