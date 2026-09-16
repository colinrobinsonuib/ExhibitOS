using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ExhibitOS.Core.ProcessSupervision;

public class WindowsJobObject : IJobObject
{
    private readonly SafeFileHandle _jobHandle;
    private bool _disposed;

    public string Name { get; }

    public WindowsJobObject(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));

        _jobHandle = CreateJobObject(IntPtr.Zero, null);
        if (_jobHandle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to create Job Object '{name}'.");
        }

        ConfigureKillOnJobClose();
    }

    private void ConfigureKillOnJobClose()
    {
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        var length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
        var ptr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(_jobHandle, JobObjectInfoClass.JobObjectExtendedLimitInformation, ptr, (uint)length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to set JobObjectExtendedLimitInformation.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public bool AssignProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return AssignProcess(process.Handle);
    }

    public bool AssignProcess(nint processHandle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!AssignProcessToJobObject(_jobHandle, processHandle))
        {
            var err = Marshal.GetLastWin32Error();
            // Process may already have terminated or access denied
            return false;
        }
        return true;
    }

    public IReadOnlyList<int> GetActiveProcessIds()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Allocate buffer for up to 1024 process IDs
        const int maxPids = 1024;
        var bufferSize = Marshal.SizeOf(typeof(JOBOBJECT_BASIC_PROCESS_ID_LIST_HEADER)) + (IntPtr.Size * maxPids);
        var buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            if (!QueryInformationJobObject(_jobHandle, JobObjectInfoClass.JobObjectBasicProcessIdList, buffer, (uint)bufferSize, out _))
            {
                return Array.Empty<int>();
            }

            var header = Marshal.PtrToStructure<JOBOBJECT_BASIC_PROCESS_ID_LIST_HEADER>(buffer);
            var count = (int)header.NumberOfProcessIdsInList;
            if (count <= 0)
            {
                return Array.Empty<int>();
            }

            var pids = new List<int>(count);
            var offset = Marshal.SizeOf(typeof(JOBOBJECT_BASIC_PROCESS_ID_LIST_HEADER));

            for (int i = 0; i < count; i++)
            {
                var pidPtr = Marshal.ReadIntPtr(buffer, offset + (i * IntPtr.Size));
                pids.Add(pidPtr.ToInt32());
            }

            return pids;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public bool IsActive
    {
        get
        {
            if (_disposed || _jobHandle.IsInvalid || _jobHandle.IsClosed)
            {
                return false;
            }
            return GetActiveProcessIds().Count > 0;
        }
    }

    public void Terminate(uint exitCode = 1)
    {
        if (_disposed || _jobHandle.IsInvalid || _jobHandle.IsClosed)
        {
            return;
        }
        TerminateJobObject(_jobHandle, exitCode);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _jobHandle.Dispose();
        }
    }

    #region Win32 Interop

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    private enum JobObjectInfoClass
    {
        JobObjectBasicProcessIdList = 3,
        JobObjectExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_PROCESS_ID_LIST_HEADER
    {
        public uint NumberOfAssignedProcesses;
        public uint NumberOfProcessIdsInList;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle hJob,
        JobObjectInfoClass JobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(
        SafeFileHandle hJob,
        JobObjectInfoClass JobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength,
        out uint lpReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeFileHandle hJob, uint uExitCode);

    #endregion
}
