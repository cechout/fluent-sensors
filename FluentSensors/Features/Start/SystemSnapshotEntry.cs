using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Common.Sensors;


namespace FluentSensors.Features.Start
{
    // one snapshot tile:
    // category, device, a few static facts and the LHM sensor count; a list, since a category can
    // hold two GPUs or three drives
    // a class, since the sensor count (LHM keeps discovering after the splash) and the icon colour (the setting)
    // move while the page is open
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
        public string Category { get; } // the HardwareGroupInfo label, "CPU"
        public string Title { get; } // the device name
        public IReadOnlyList<string> Details { get; }

        // follows the colour setting, see StartViewModel.IconBrushFor
        public SolidColorBrush IconBrush
        {
            get => _iconBrush;
            set { if (_iconBrush == value) return; _iconBrush = value; OnPropertyChanged(); }
        }


        // === sensor pairing ===

        // the LHM match, not a resolved instance; LHM can report hardware long after the tile was built
        public HardwareGroupKind MatchKind { get; }
        public string MatchName { get; }

        public string SensorCountText
        {
            get => _sensorCountText;
            set { if (_sensorCountText == value) return; _sensorCountText = value; OnPropertyChanged(); }
        }

        // false disables the count button, there is no group to open
        public bool HasSensors
        {
            get => _hasSensors;
            set { if (_hasSensors == value) return; _hasSensors = value; OnPropertyChanged(); }
        }

        // the raw names counted, which the sensors page matches against
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
