using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Vortice.DXGI;

using FluentSensors.Core.Startup;


namespace FluentSensors.Core
{
    // the gpu side of this process, as the task manager shows it:
    // the gpu counters of windows (GPU Engine, GPU Process Memory) for every process, filtered to this one; the
    // adapter is the one with the most running time, the usage the busiest engine, the memory dedicated plus shared
    // a full read walks every engine of every process, so it runs off the UI thread and only while someone shows it
    public record AppGpuData(
        string? AdapterName, // null until the app has run on a gpu
        double UsagePercent,
        long MemoryBytes,
        bool IsDisplayAdapter, // false on a hybrid laptop rendering on the discrete gpu, which copies every frame
        IReadOnlyList<int> RefreshRatesHz // one per refresh rate in use, highest first
    );

    public sealed partial class WinAppGpuMonitor : IDisposable
    {
        // === fields ===

        // english names, the localized ones differ per windows language
        private const string EngineUsagePath = @"\GPU Engine(*)\Utilization Percentage";
        private const string EngineRunningTimePath = @"\GPU Engine(*)\Running Time";
        private const string DedicatedMemoryPath = @"\GPU Process Memory(*)\Dedicated Usage";
        private const string SharedMemoryPath = @"\GPU Process Memory(*)\Shared Usage";

        private readonly string _instancePrefix = $"pid_{Environment.ProcessId}_";
        private readonly object _lock = new object();
        private readonly Dictionary<long, string> _adapterNames = new Dictionary<long, string>();

        private IntPtr _query;
        private bool _isDisposed;
        private IntPtr _usageCounter;
        private IntPtr _runningTimeCounter;
        private IntPtr _dedicatedCounter;
        private IntPtr _sharedCounter;


        // === public api ===

        // the usage is a rate, so the first read after opening has none yet; null when the counters are missing
        public AppGpuData? Read()
        {
            lock (_lock)
            {
                if (_isDisposed) return null;
                if (_query == IntPtr.Zero && !Open()) return null;
                if (PdhCollectQueryData(_query) != 0) return null;

                double usage = 0;
                foreach (var (name, value) in ReadDoubles(_usageCounter))
                {
                    if (name.StartsWith(_instancePrefix, StringComparison.Ordinal)) usage = Math.Max(usage, value);
                }

                var runningTimeByAdapter = new Dictionary<long, long>();
                foreach (var (name, value) in ReadLongs(_runningTimeCounter))
                {
                    if (!name.StartsWith(_instancePrefix, StringComparison.Ordinal)) continue;
                    long? luid = ParseLuid(name);
                    if (luid == null) continue;
                    runningTimeByAdapter[luid.Value] = runningTimeByAdapter.GetValueOrDefault(luid.Value) + value;
                }

                long memory = 0;
                foreach (var counter in new[] { _dedicatedCounter, _sharedCounter })
                {
                    foreach (var (name, value) in ReadLongs(counter))
                    {
                        if (name.StartsWith(_instancePrefix, StringComparison.Ordinal)) memory += value;
                    }
                }

                long? adapter = null;
                long mostRunningTime = 0;
                foreach (var (luid, runningTime) in runningTimeByAdapter)
                {
                    if (runningTime > mostRunningTime) { adapter = luid; mostRunningTime = runningTime; }
                }

                var screens = WinGpuPreference.ScreenAdapters();
                bool isDisplayAdapter = adapter == null || screens == null || screens.Exists(s => s.AdapterLuid == adapter);

                var refreshRates = new List<int>();
                foreach (var screen in screens ?? new List<WinScreenPath>())
                {
                    if (screen.RefreshRateHz > 0 && !refreshRates.Contains(screen.RefreshRateHz)) refreshRates.Add(screen.RefreshRateHz);
                }
                refreshRates.Sort((a, b) => b.CompareTo(a));

                return new AppGpuData(
                    adapter == null ? null : AdapterName(adapter.Value),
                    Math.Min(usage, 100),
                    memory,
                    isDisplayAdapter,
                    refreshRates);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_query != IntPtr.Zero) PdhCloseQuery(_query);
                _query = IntPtr.Zero;
                _isDisposed = true;
            }
        }


        // === private helpers ===

        private bool Open()
        {
            if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0) return false;

            if (PdhAddEnglishCounterW(_query, EngineUsagePath, IntPtr.Zero, out _usageCounter) != 0
                || PdhAddEnglishCounterW(_query, EngineRunningTimePath, IntPtr.Zero, out _runningTimeCounter) != 0
                || PdhAddEnglishCounterW(_query, DedicatedMemoryPath, IntPtr.Zero, out _dedicatedCounter) != 0
                || PdhAddEnglishCounterW(_query, SharedMemoryPath, IntPtr.Zero, out _sharedCounter) != 0)
            {
                PdhCloseQuery(_query);
                _query = IntPtr.Zero;
                return false;
            }

            return true;
        }

        // pid_1234_luid_0x00000000_0x000108E7_phys_0_eng_0_engtype_3D, the high part first
        private static long? ParseLuid(string instance)
        {
            int start = instance.IndexOf("_luid_0x", StringComparison.Ordinal);
            if (start < 0 || instance.Length < start + 27) return null;

            if (!uint.TryParse(instance.AsSpan(start + 8, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint high)) return null;
            if (!uint.TryParse(instance.AsSpan(start + 19, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint low)) return null;
            return (long)high << 32 | low;
        }

        // the DXGI description, cached; the preference of this process is long read by then, see WinGpuPreference
        private string? AdapterName(long luid)
        {
            if (_adapterNames.TryGetValue(luid, out var cached)) return cached;

            try
            {
                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1 adapter).Success; i++)
                {
                    using (adapter)
                    {
                        var desc = adapter.Description1;
                        _adapterNames[desc.Luid] = desc.Description;
                    }
                }
            }
            catch
            {
                // no DXGI; the tile shows no name
            }

            return _adapterNames.GetValueOrDefault(luid);
        }

        private static List<(string Name, double Value)> ReadDoubles(IntPtr counter)
        {
            var result = new List<(string, double)>();
            ReadArray(counter, PdhFmtDouble | PdhFmtNoCap100, (name, item) => result.Add((name, Marshal.PtrToStructure<double>(item))));
            return result;
        }

        private static List<(string Name, long Value)> ReadLongs(IntPtr counter)
        {
            var result = new List<(string, long)>();
            ReadArray(counter, PdhFmtLarge, (name, item) => result.Add((name, Marshal.ReadInt64(item))));
            return result;
        }

        // PDH_FMT_COUNTERVALUE_ITEM_W: the name pointer, then CStatus and the 8 byte aligned value union
        private static void ReadArray(IntPtr counter, uint format, Action<string, IntPtr> add)
        {
            uint size = 0;
            int status = PdhGetFormattedCounterArrayW(counter, format, ref size, out uint count, IntPtr.Zero);
            if (status != PdhMoreData || size == 0) return;

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArrayW(counter, format, ref size, out count, buffer) != 0) return;

                for (int i = 0; i < count; i++)
                {
                    IntPtr item = buffer + i * CounterValueItemSize;
                    if (Marshal.ReadInt32(item, IntPtr.Size) != 0) continue; // CStatus, a vanished instance

                    string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item));
                    if (name != null) add(name, item + CounterValueOffset);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }


        // === native ===

        private const uint PdhFmtDouble = 0x00000200;
        private const uint PdhFmtLarge = 0x00000400;
        private const uint PdhFmtNoCap100 = 0x00008000;
        private const int PdhMoreData = unchecked((int)0x800007D2);
        private static readonly int CounterValueOffset = (IntPtr.Size + 4 + 7) & ~7;
        private static readonly int CounterValueItemSize = CounterValueOffset + 8;

        // https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhopenqueryw
        [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

        // https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhaddenglishcounterw
        [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int PdhAddEnglishCounterW(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

        // https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhcollectquerydata
        [LibraryImport("pdh.dll")]
        private static partial int PdhCollectQueryData(IntPtr query);

        // https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhgetformattedcounterarrayw
        [LibraryImport("pdh.dll")]
        private static partial int PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

        // https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhclosequery
        [LibraryImport("pdh.dll")]
        private static partial int PdhCloseQuery(IntPtr query);
    }
}
