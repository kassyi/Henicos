namespace Kassyi.GSharp.Runtime.Aot;

using System;
using System.ComponentModel;
using System.Threading;
using Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// The device error records of the launches through generated CUDA launchers, one per launch epoch.
/// </summary>
/// <remarks>
/// <para>
/// Every launch takes an epoch (<see cref="NextLaunchEpoch"/>) and passes it to the kernel as its hidden last
/// parameter. A failing thread claims the record of its epoch (one of <see cref="Records"/> in pinned host-mapped
/// memory, indexed by
/// <c>epoch % Records</c>) and writes its epoch into the abort word of the launch (an array in device memory of each
/// module), which the waiters of the same launch at a barrier that counts a fixed number of arrivals (the grid barrier,
/// an mbarrier) read to leave.
/// </para>
/// <para>
/// A launcher that reads the record of its launch itself owns the epoch from <see cref="NextLaunchEpoch"/> to
/// <see cref="ThrowIfLaunchError"/> (or <see cref="ReleaseLaunchEpoch"/> when the launch fails earlier), and a stream
/// synchronization does not take an owned record (<see cref="DeviceErrorSlot.ThrowIfDeviceError"/>), so the launcher
/// throws its own error. The record of a launch that no launcher reads (a <c>Ptr</c> launch outside the checked build)
/// is thrown by the next synchronization, or by the launch that takes its record <see cref="Records"/> epochs later.
/// </para>
/// <para>
/// Generated launchers call these members; code that launches a kernel itself does not need them.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static unsafe class LaunchErrorRecords
{
    /// <summary>The number of records (<c>__GSHARP_LAUNCH_RECORDS</c> of the CUDA prelude).</summary>
    internal const uint Records = 256u;

    // The records (Prelude.cu, GSharpLaunchErrorRing)
    private static readonly int s_bytes = (int)Records * sizeof(GSharpDeviceErrorSlot);

    private static IntPtr s_host;
    private static ulong s_device;
    private static int s_lastEpoch;

    // The epoch whose launcher reads the record of each index itself, or 0
    private static readonly uint[] s_owners = new uint[Records];

    /// <summary>The device address of the records, or 0 when they could not be allocated.</summary>
    internal static ulong DevicePointer => s_device;

    // Called under the lock of DeviceErrorSlot
    internal static void Allocate()
    {
        if (s_host != IntPtr.Zero)
        {
            return;
        }

        if (!DeviceErrorSlot.TryAllocateMapped(s_bytes, out var host, out var device, out var reason))
        {
            // [WHY] Every launch then has epoch 0 and shares the record of DeviceErrorSlot: an error can be thrown by
            // the synchronization of another launch
            Logger.Warning(nameof(LaunchErrorRecords),
                $"The per-launch device error records could not be allocated ({reason}); " +
                "every launch shares one record.");
            return;
        }

        s_device = device;
        Volatile.Write(ref s_host, host);
    }

    // Called under the lock of DeviceErrorSlot
    internal static void Free()
    {
        var host = Volatile.Read(ref s_host);
        if (host == IntPtr.Zero)
        {
            return;
        }

        Volatile.Write(ref s_host, IntPtr.Zero);
        s_device = 0;
        CudaDriverApi.cuMemFreeHost(host);
    }

    /// <summary>
    /// Takes the epoch of a kernel launch on <paramref name="stream"/>, which a generated launcher passes to the kernel
    /// as its hidden last parameter: the kernel records a device error under it.
    /// </summary>
    /// <param name="stream">The stream the kernel is launched on.</param>
    /// <param name="launcherConsumes">
    /// Whether the launcher reads the record of the launch itself (<see cref="ThrowIfLaunchError"/>); it then owns the
    /// epoch until it does, or until <see cref="ReleaseLaunchEpoch"/>.
    /// </param>
    /// <returns>
    /// A new nonzero epoch, or 0 when the stream is being captured into a CUDA graph or when there are no per-launch
    /// records.
    /// </returns>
    /// <exception cref="Exception">
    /// A device error of an earlier launch that nothing consumed is recorded where this launch would record its own:
    /// that error is thrown, before the launch.
    /// </exception>
    public static uint NextLaunchEpoch(IntPtr stream, bool launcherConsumes)
    {
        var ring = (GSharpDeviceErrorSlot*)Volatile.Read(ref s_host);
        if (ring == null)
        {
            return 0u;
        }

        // [WHY] A captured launch keeps its arguments in the graph and runs again at every replay, so it cannot own a
        // record: it records into the shared one, which the synchronization after cuGraphLaunch reads
        if (CudaDriverApi.cuStreamIsCapturing(stream, out var captureStatus) != CUresult.CudaSuccess ||
            captureStatus != CUstreamCaptureStatus.None)
        {
            return 0u;
        }

        // An epoch whose record a launcher still waits for is skipped, so that this launch neither throws nor
        // overwrites that launcher's error; when every record is owned, the launch shares the record of epoch 0
        for (var attempt = 0u; attempt < Records; attempt++)
        {
            var epoch = TakeEpoch();
            var index = epoch % Records;
            if (Volatile.Read(ref s_owners[index]) != 0u ||
                (launcherConsumes && Interlocked.CompareExchange(ref s_owners[index], epoch, 0u) != 0u))
            {
                continue;
            }

            if (DeviceErrorSlot.TryConsumeRecord(ring + index, DeviceErrorSlot.RecordFilter.Any(includeOwned: false),
                    out var stale))
            {
                ReleaseLaunchEpoch(epoch);
                throw GSharpErrorMapper.CreateExceptionFromSlot(in stale, innerException: null);
            }

            return epoch;
        }

        return 0u;
    }

    private static uint TakeEpoch()
    {
        var epoch = unchecked((uint)Interlocked.Increment(ref s_lastEpoch));
        if (epoch != 0u)
        {
            return epoch;
        }

        // The counter wrapped around: the epochs come back, and so would the abort words of launches that failed
        // 2^32 epochs ago
        CudaModuleCache.ClearLaunchAbortWords();
        return TakeEpoch();
    }

    /// <summary>
    /// Throws the device error that the launch with <paramref name="launchEpoch"/> recorded, if any, once the launch
    /// has completed, and ends the launcher's ownership of the epoch; the records of other launches stay for their own
    /// launchers and synchronizations. For epoch 0 it throws any recorded device error, like
    /// <see cref="DeviceErrorSlot.ThrowIfDeviceError"/>.
    /// </summary>
    /// <param name="launchEpoch">The epoch that <see cref="NextLaunchEpoch"/> returned for the launch.</param>
    public static void ThrowIfLaunchError(uint launchEpoch)
    {
        var ring = (GSharpDeviceErrorSlot*)Volatile.Read(ref s_host);
        if (launchEpoch == 0u || ring == null)
        {
            DeviceErrorSlot.ThrowIfDeviceError();
            return;
        }

        try
        {
            // A record that another launch still held when the error happened sends it to the shared record, tagged
            // with the epoch (Prelude.cu, __gsharp_record_error)
            var filter = DeviceErrorSlot.RecordFilter.Of(launchEpoch);
            if (DeviceErrorSlot.TryConsumeRecord(ring + launchEpoch % Records, filter, out var slot) ||
                DeviceErrorSlot.TryConsumeRecord(DeviceErrorSlot.SharedRecord, filter, out slot))
            {
                throw GSharpErrorMapper.CreateExceptionFromSlot(in slot, innerException: null);
            }
        }
        finally
        {
            ReleaseLaunchEpoch(launchEpoch);
        }
    }

    /// <summary>
    /// Ends the launcher's ownership of <paramref name="launchEpoch"/> without reading its record, when the launch ends
    /// before <see cref="ThrowIfLaunchError"/>; a record of the launch is then thrown by the next synchronization.
    /// </summary>
    /// <param name="launchEpoch">The epoch that <see cref="NextLaunchEpoch"/> returned for the launch.</param>
    public static void ReleaseLaunchEpoch(uint launchEpoch)
    {
        if (launchEpoch != 0u)
        {
            Interlocked.CompareExchange(ref s_owners[launchEpoch % Records], 0u, launchEpoch);
        }
    }

    // Whether a launcher reads the record of epoch itself
    internal static bool IsOwned(uint epoch) => epoch != 0u && Volatile.Read(ref s_owners[epoch % Records]) == epoch;

    // Consumes a record of any epoch (with includeOwned, also one a launcher waits for)
    internal static bool TryConsumeAny(bool includeOwned, out GSharpDeviceErrorSlot slot)
    {
        slot = default;
        var ring = (GSharpDeviceErrorSlot*)Volatile.Read(ref s_host);
        if (ring == null)
        {
            return false;
        }

        var filter = DeviceErrorSlot.RecordFilter.Any(includeOwned);
        for (var i = 0u; i < Records; i++)
        {
            if (DeviceErrorSlot.TryConsumeRecord(ring + i, filter, out slot))
            {
                return true;
            }
        }

        return false;
    }

    // Tests: the record of an index, to plant a record that a device thread is still writing
    internal static GSharpDeviceErrorSlot* RecordForTest(uint index) =>
        (GSharpDeviceErrorSlot*)Volatile.Read(ref s_host) + index % Records;
}
