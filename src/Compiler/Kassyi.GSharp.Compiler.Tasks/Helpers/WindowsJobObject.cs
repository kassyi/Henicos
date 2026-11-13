using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Kassyi.GSharp.Compiler.Tasks.Helpers;

/// <summary>
/// A Windows job object for one started process: every process it starts afterwards joins the job, so terminating the
/// job stops the whole tree.
/// </summary>
/// <remarks>
/// While the run lasts the job kills its processes when its last handle closes, so a build host that dies takes the
/// compilers with it. <see cref="Dispose"/> lifts that limit before closing the handle: after a normal end the
/// descendants that outlive the process (the PDB server <c>mspdbsrv.exe</c> that later compiles share) keep running.
/// </remarks>
internal sealed class WindowsJobObject : IDisposable
{
    private const uint KillOnJobClose = 0x2000;
    private const int ExtendedLimitInformationClass = 9;

    private readonly JobHandle _handle;

    private WindowsJobObject(JobHandle handle)
    {
        _handle = handle;
    }

    /// <summary>Creates an unnamed job that kills its processes on close, or returns <see langword="null"/>.</summary>
    public static WindowsJobObject? TryCreate()
    {
        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        var job = new WindowsJobObject(handle);
        if (!job.SetKillOnClose(true))
        {
            job.Dispose();
            return null;
        }

        return job;
    }

    /// <summary>Puts a started process into the job; <see langword="false"/> when Windows refuses it.</summary>
    public bool TryAssign(Process process) => AssignProcessToJobObject(_handle, process.Handle);

    /// <summary>Terminates every process of the job.</summary>
    public bool Terminate() => TerminateJobObject(_handle, uint.MaxValue);

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_handle.IsInvalid && !_handle.IsClosed)
        {
            // A failure leaves the limit set, so the descendants stop with the handle; nothing else can be done.
            SetKillOnClose(false);
        }

        _handle.Dispose();
    }

    private bool SetKillOnClose(bool kill)
    {
        var information = new ExtendedLimitInformation
        {
            BasicLimitInformation = new BasicLimitInformation { LimitFlags = kill ? KillOnJobClose : 0 }
        };
        return SetInformationJobObject(
            _handle,
            ExtendedLimitInformationClass,
            ref information,
            Marshal.SizeOf<ExtendedLimitInformation>()
        );
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern JobHandle CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        JobHandle job,
        int informationClass,
        ref ExtendedLimitInformation information,
        int length
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(JobHandle job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(JobHandle job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    // JOBOBJECT_BASIC_LIMIT_INFORMATION; the pointer-sized fields keep the native layout on 32- and 64-bit hosts.
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
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

    // IO_COUNTERS
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    // JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private sealed class JobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public JobHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }
}
