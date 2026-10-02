using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;


namespace FluentSensors.Core.StaticInfo
{
    // the cpu topology:
    // physical cores, SMT and the EfficiencyClass of Intel hybrid CPUs from
    // GetLogicalProcessorInformationEx, the source of the Windows scheduler and Task Manager; works without
    // SMT too (Arrow Lake has none); AMD hybrid is untested
    // the buffer is a run of variable-length records (a union with a trailing array), which struct marshaling handles
    // poorly, so it is read as raw bytes at the documented offsets:
    // https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-system_logical_processor_information_ex
    // https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-processor_relationship
    // https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getlogicalprocessorinformationex
    // the null-buffer-first pattern, worked through:
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

        // one entry per physical core; an empty list when the call fails, read as "topology unknown"
        public static IReadOnlyList<WinCpuCoreTopologyEntry> ReadCoreTopology()
        {
            uint length = 0;

            // the first call fails on purpose, for the size
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

        // the documented offsets, 64-bit layout (the shipped builds are x64):
        //
        // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX header:
        //   +0: DWORD Relationship (4 bytes)
        //   +4: DWORD Size         (4 bytes) - total size of this record, used to step to the next one
        //   +8: PROCESSOR_RELATIONSHIP (only RelationProcessorCore is requested)
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
                if (recordSize <= 0) break; // a malformed buffer

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
                            // group-relative; (one group on consumer CPUs, 64+ logical processors would need Group too)
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
