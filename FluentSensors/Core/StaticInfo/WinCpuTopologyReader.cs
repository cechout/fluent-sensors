using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;


namespace FluentSensors.Core.StaticInfo
{
    // reads CPU core topology
    // (physical core boundaries, SMT, and the Windows-native "EfficiencyClass" hint that distinguishes performance
    // vs. efficiency cores on Intel hybrid CPUs) via the native GetLogicalProcessorInformationEx Win32 API; the same
    // mechanism Windows own scheduler and tools like Task Manager/HWiNFO use
    // Unlike a name-based heuristic, it works regardless of whether SMT/Hyper-Threading exists on the chip at all
    // (relevant since Arrow Lake and newer Intel CPUs have no threads on any core)
    // AMD hybrid CPUs are not confirmed to populate EfficiencyClass reliably; untested here
    //
    // problem: the buffer this API returns is a sequence of variable-length records (a C union with a
    // trailing variable-size array), which C# automatic struct marshaling handles poorly and unsafely for
    // this shape
    // fix: the buffer is read manually as raw bytes at fixed offsets documented by the Win32 API, instead of
    // marshaling it onto a C# struct
    //
    // official struct docs the manual offsets below are derived from:
    // https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-system_logical_processor_information_ex
    // https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-processor_relationship
    // https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getlogicalprocessorinformationex
    // worked example of the same "call with null buffer first, then enumerate variable-length records" pattern:
    // https://devblogs.microsoft.com/oldnewthing/using-getlogicalprocessorinformationex-to-see-the-relationship-between-logical-and-physical-processors
    public static partial class WinCpuTopologyReader
    {
        private const int RelationProcessorCore = 0;

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetLogicalProcessorInformationEx(
            int relationshipType,
            IntPtr buffer,
            ref uint returnedLength);

        // returns one entry per physical core; empty list (not an exception) if the call fails for any reason (non-
        // hybrid CPU, unsupported OS version, ...)
        // callers should treat an empty list as "topology unknown", not as an error
        public static IReadOnlyList<WinCpuCoreTopologyEntry> ReadCoreTopology()
        {
            uint length = 0;

            // first call deliberately fails (buffer too small) just to learn the required size
            GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref length);
            if (length == 0) return Array.Empty<WinCpuCoreTopologyEntry>();

            IntPtr buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                bool success = GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref length);
                if (!success) return Array.Empty<WinCpuCoreTopologyEntry>();

                return ParseBuffer(buffer, (int)length);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        // manual offsets, documented by the SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX / PROCESSOR_RELATIONSHIP /
        // GROUP_AFFINITY Win32 structs (sizes below are for x64, this app's only build target):
        //
        // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX header:
        //   +0: DWORD Relationship (4 bytes)
        //   +4: DWORD Size         (4 bytes) - total size of this record, used to step to the next one
        //   +8: PROCESSOR_RELATIONSHIP starts here (guaranteed, since we only request RelationProcessorCore)
        //
        // PROCESSOR_RELATIONSHIP (relative to +8):
        //   +0:  BYTE Flags           (bit 0 = LTP_PC_SMT, i.e. this core has more than one logical processor)
        //   +1:  BYTE EfficiencyClass
        //   +2:  BYTE Reserved[20]
        //   +22: WORD GroupCount
        //   +24: GROUP_AFFINITY GroupMask[GroupCount], 16 bytes each:
        //          +0: KAFFINITY Mask (8 bytes, one bit per logical processor in that group)
        //          +8: WORD Group, +10: WORD Reserved[3]
        private static List<WinCpuCoreTopologyEntry> ParseBuffer(IntPtr buffer, int totalLength)
        {
            var result = new List<WinCpuCoreTopologyEntry>();
            int offset = 0;
            int coreIndex = 0;

            while (offset < totalLength)
            {
                int recordSize = Marshal.ReadInt32(buffer, offset + 4);
                if (recordSize <= 0) break; // guard against a malformed/unexpected buffer

                IntPtr processorRelationship = IntPtr.Add(buffer, offset + 8);

                byte flags = Marshal.ReadByte(processorRelationship, 0);
                byte efficiencyClass = Marshal.ReadByte(processorRelationship, 1);
                ushort groupCount = (ushort)Marshal.ReadInt16(processorRelationship, 22);

                var logicalProcessorIndices = new List<int>();
                for (int g = 0; g < groupCount; g++)
                {
                    IntPtr groupAffinity = IntPtr.Add(processorRelationship, 24 + g * 16);
                    long mask = Marshal.ReadInt64(groupAffinity, 0);

                    for (int bit = 0; bit < 64; bit++)
                    {
                        if ((mask & (1L << bit)) != 0)
                        {
                            // group-relative bit index; fine for practically every consumer CPU (single group),
                            // would need the Group field too on 64+ logical processor systems
                            logicalProcessorIndices.Add(bit);
                        }
                    }
                }

                result.Add(new WinCpuCoreTopologyEntry(
                    CoreIndex: coreIndex,
                    EfficiencyClass: efficiencyClass,
                    HasSmt: (flags & 0x1) != 0,
                    LogicalProcessorIndices: logicalProcessorIndices
                ));

                coreIndex++;
                offset += recordSize;
            }

            return result;
        }
    }
}