namespace Kassyi.GSharp.Runtime.Aot;

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// Manages the pinned host-mapped error records shared between the host and device kernels (#729).
/// </summary>
/// <remarks>
/// <para>
/// <b>[PER-LAUNCH RECORDS]:</b> Every launch through a generated launcher gets an epoch
/// (<see cref="GpuKernelLoader.NextLaunchEpoch"/>), passed to the kernel as its hidden last parameter. When a device check
/// fails, the failing warp elects a leader that atomically claims the record of its epoch (one of
/// <see cref="LaunchRecords"/>, indexed by <c>epoch % LaunchRecords</c>) and writes the error code, site ID, module ID
/// and coordinates; the failing threads then exit and the kernel completes. The thread also writes its epoch into the
/// abort word of the record, which the waiters of the same launch at a barrier that counts a fixed number of arrivals
/// (the grid barrier, an mbarrier) read to leave the wait.
/// </para>
/// <para>
/// The launcher consumes the record of its own epoch after it synchronizes (<see cref="GpuKernelLoader.ThrowIfLaunchError"/>),
/// so a launch on another stream does not throw it. A stream synchronization (<see cref="ThrowIfDeviceError"/>) and a
/// kernel fault that the driver reports (<see cref="CreateExceptionForDriverError"/>) consume any record, as does a launch
/// that reuses the record of an earlier launch that nothing consumed yet.
/// </para>
/// <para>
/// A launch captured into a CUDA graph, and every launch when the records could not be allocated, has epoch 0: it writes
/// the one record of the process that such launches share, and its waiters have no abort word.
/// </para>
/// </remarks>
public static unsafe class DeviceErrorSlot
{
    /// <summary>The number of per-launch records (<c>__GSHARP_LAUNCH_RECORDS</c> of the CUDA prelude).</summary>
    internal const uint LaunchRecords = 256u;

    // The records of the launches with an epoch, followed by one abort word per record (Prelude.cu, GSharpLaunchErrorRing)
    private static readonly int s_launchErrorsBytes = (int)LaunchRecords * (sizeof(GSharpDeviceErrorSlot) + sizeof(uint));

    private static readonly object s_errorSlotLock = new();
    private static GSharpDeviceErrorSlot* s_hostErrorSlot;
    private static ulong s_deviceErrorSlotPtr;
    private static IntPtr s_hostLaunchErrors;
    private static ulong s_deviceLaunchErrorsPtr;
    private static int s_lastEpoch;

    // Module ID cache indexed by 256-bit signature hash for ErrorSiteTable correlation
    private static readonly ConcurrentDictionary<AotSignatureValidator.Hash256, uint> s_moduleIds = new();

    /// <summary>Gets the device pointer to the pinned error record of the launches without an epoch.</summary>
    public static ulong DeviceErrorSlotPtr => s_deviceErrorSlotPtr;

    internal static uint GetOrAssignModuleId(AotSignatureValidator.Hash256 hash) =>
        s_moduleIds.GetOrAdd(hash, static _ => GSharpErrorMapper.AllocateModuleId());

    /// <summary>
    /// Allocates the pinned host-mapped error records in the current CUDA context if not already allocated.
    /// </summary>
    public static void AllocateInCurrentContext()
    {
        lock (s_errorSlotLock)
        {
            if (s_hostErrorSlot != null)
            {
                return;
            }

            if (!TryAllocateMapped(sizeof(GSharpDeviceErrorSlot), out var hostPtr, out var devPtr, out var reason))
            {
                ReportUnavailable(reason);
                return;
            }

            s_hostErrorSlot = (GSharpDeviceErrorSlot*)hostPtr;
            s_deviceErrorSlotPtr = devPtr;

            if (!TryAllocateMapped(s_launchErrorsBytes, out var ringHostPtr, out var ringDevPtr, out var ringReason))
            {
                // [WHY] Every launch then has epoch 0 and shares the record above: an error can be thrown by the
                // synchronization of another launch
                Logger.Warning(nameof(DeviceErrorSlot),
                    $"The per-launch device error records could not be allocated ({ringReason}); every launch shares one record.");
                return;
            }

            s_deviceLaunchErrorsPtr = ringDevPtr;
            Volatile.Write(ref s_hostLaunchErrors, ringHostPtr);
        }
    }

    // A zeroed block of pinned host memory mapped into the device address space
    private static bool TryAllocateMapped(int bytes, out IntPtr hostPtr, out ulong devicePtr, out string reason)
    {
        devicePtr = 0;
        var flags = CudaDriverApi.MemHostAllocPortable | CudaDriverApi.MemHostAllocDeviceMap;
        var allocResult = CudaDriverApi.cuMemHostAlloc(out hostPtr, bytes, flags);
        if (allocResult != CUresult.CudaSuccess || hostPtr == IntPtr.Zero)
        {
            reason = $"cuMemHostAlloc returned {allocResult}";
            hostPtr = IntPtr.Zero;
            return false;
        }

        var mapResult = CudaDriverApi.cuMemHostGetDevicePointer(out var devPtr, hostPtr, 0);
        if (mapResult != CUresult.CudaSuccess)
        {
            CudaDriverApi.cuMemFreeHost(hostPtr);
            reason = $"cuMemHostGetDevicePointer returned {mapResult}";
            hostPtr = IntPtr.Zero;
            return false;
        }

        NativeMemory.Clear((void*)hostPtr, (nuint)bytes);
        devicePtr = (ulong)devPtr;
        reason = "";
        return true;
    }

    // [WHY] Without the record a failing device check traps instead of reporting its exception (Prelude.cu,
    // __gsharp_trap_site): the error then surfaces as a CudaException that leaves the context unusable
    private static void ReportUnavailable(string reason) =>
        Logger.Warning(nameof(DeviceErrorSlot),
            $"The device error slot could not be allocated ({reason}); a failing device check traps and fails the launch with a CudaException.");

    /// <summary>
    /// Injects the device error record pointers and module ID into the loaded CUDA module's globals.
    /// </summary>
    /// <param name="module">The native CUDA module handle.</param>
    /// <param name="moduleId">The 1-based error module ID allocated for this module.</param>
    public static void ConfigureModule(IntPtr module, uint moduleId)
    {
        SetPointerGlobal(module, "__gsharp_error_slot", s_deviceErrorSlotPtr);
        SetPointerGlobal(module, "__gsharp_launch_errors", s_deviceLaunchErrorsPtr);

        if (moduleId != 0 &&
            CudaDriverApi.cuModuleGetGlobal_v2(out var idGlobalDptr, out var idGlobalSize, module, "__gsharp_error_module_id") == CUresult.CudaSuccess &&
            idGlobalSize == sizeof(uint))
        {
            CudaDriverApi.cuMemcpyHtoD_v2(idGlobalDptr, (IntPtr)(&moduleId), sizeof(uint));
        }
    }

    private static void SetPointerGlobal(IntPtr module, string name, ulong value)
    {
        if (value != 0 &&
            CudaDriverApi.cuModuleGetGlobal_v2(out var globalDptr, out var globalSize, module, name) == CUresult.CudaSuccess &&
            globalSize == (nuint)sizeof(ulong))
        {
            CudaDriverApi.cuMemcpyHtoD_v2(globalDptr, (IntPtr)(&value), sizeof(ulong));
        }
    }

    /// <summary>
    /// Registers an error site table for the specified AOT fatbin binary.
    /// </summary>
    /// <param name="aotFatbin">The raw byte span of the AOT Fatbin binary.</param>
    /// <param name="sites">The 1-based error site table.</param>
    public static void RegisterErrorSites(ReadOnlySpan<byte> aotFatbin, ReadOnlySpan<GSharpErrorSite> sites)
    {
        if (aotFatbin.Length < AotSignatureValidator.SignatureSize)
        {
            return;
        }

        var embeddedHash = new AotSignatureValidator.Hash256(
            aotFatbin.Slice(aotFatbin.Length - AotSignatureValidator.SignatureSize, 32));
        var moduleId = GetOrAssignModuleId(embeddedHash);
        GSharpErrorMapper.RegisterModuleErrorSites(moduleId, sites);
    }

    /// <summary>
    /// Consumes any recorded device error and throws the corresponding standard .NET exception.
    /// </summary>
    /// <param name="innerException">Optional inner <see cref="CudaException"/> from the CUDA driver.</param>
    /// <remarks>
    /// <b>[NOTE]:</b> This consumes the record of any launch, also of a launch on another stream that has not been
    /// synchronized yet; a launcher consumes only the record of its own launch.
    /// </remarks>
    public static void ThrowIfDeviceError(Exception? innerException = null)
    {
        if (TryConsumeSlot(out var slot))
        {
            throw GSharpErrorMapper.CreateExceptionFromSlot(in slot, innerException);
        }
    }

    /// <summary>
    /// Creates a managed exception for a non-success <see cref="CUresult"/>, translating any recorded device error into the matching standard .NET exception.
    /// </summary>
    /// <param name="result">The non-success CUDA driver result code.</param>
    /// <param name="fallbackMessage">An optional fallback error message.</param>
    /// <returns>The constructed standard .NET exception or <see cref="CudaException"/>.</returns>
    public static Exception CreateExceptionForDriverError(CUresult result, string? fallbackMessage = null)
    {
        var cudaEx = new CudaException(result, fallbackMessage);
        // [WHY] Only a fault of a running kernel can come with a record (a thread that traps after recording its error).
        // Any other failure, such as a launch the device rejects, would take the record of an unrelated launch that its
        // own launcher or synchronization is about to throw.
        if (IsKernelFault(result) && TryConsumeSlot(out var slot))
        {
            return GSharpErrorMapper.CreateExceptionFromSlot(
                in slot,
                innerException: cudaEx);
        }

        return cudaEx;
    }

    // The results by which the driver reports that a running kernel faulted: illegal address (700), launch timeout (702),
    // device assert (710), hardware stack error (714), illegal instruction (715, a trap), misaligned address (716),
    // invalid address space (717), invalid PC (718) and launch failure (719)
    private static bool IsKernelFault(CUresult result) =>
        (int)result is 700 or 702 or 710 or (>= 714 and <= 719);

    /// <summary>
    /// Attempts to consume and reset a recorded device error of any launch.
    /// </summary>
    /// <param name="slot">When this method returns <see langword="true"/>, contains the consumed error record.</param>
    /// <returns><see langword="true"/> if a device error was recorded and consumed; otherwise, <see langword="false"/>.</returns>
    public static bool TryConsumeSlot(out GSharpDeviceErrorSlot slot)
    {
        if (TryConsumeRecord(s_hostErrorSlot, 0u, matchEpoch: false, out slot))
        {
            return true;
        }

        var ring = (GSharpDeviceErrorSlot*)Volatile.Read(ref s_hostLaunchErrors);
        if (ring == null)
        {
            return false;
        }

        for (var i = 0u; i < LaunchRecords; i++)
        {
            if (TryConsumeRecord(ring + i, 0u, matchEpoch: false, out slot))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Takes the epoch of a launch on <paramref name="stream"/>: a new nonzero epoch, or 0 for a launch that is captured
    /// into a CUDA graph or when there are no per-launch records.
    /// </summary>
    /// <exception cref="Exception">
    /// The record of the new epoch holds an error that nothing consumed yet (of the launch <see cref="LaunchRecords"/>
    /// epochs before): its exception is thrown, and no epoch is taken.
    /// </exception>
    internal static uint NextLaunchEpoch(IntPtr stream)
    {
        var ring = (GSharpDeviceErrorSlot*)Volatile.Read(ref s_hostLaunchErrors);
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

        uint epoch;
        do
        {
            epoch = unchecked((uint)Interlocked.Increment(ref s_lastEpoch));
        } while (epoch == 0u);

        var index = epoch % LaunchRecords;
        if (TryConsumeRecord(ring + index, 0u, matchEpoch: false, out var stale))
        {
            throw GSharpErrorMapper.CreateExceptionFromSlot(in stale, innerException: null);
        }

        // The abort word still holds this epoch only when a launch 2^32 epochs ago failed; it would make the waiters of
        // this launch leave a barrier. A word that a running launch with the same index writes now holds another epoch
        // and stays.
        ref var abortWord = ref AbortWord(ring, index);
        if (Volatile.Read(ref abortWord) == epoch)
        {
            Interlocked.CompareExchange(ref abortWord, 0u, epoch);
        }

        return epoch;
    }

    /// <summary>
    /// Consumes the record of the launch with <paramref name="epoch"/> and throws its exception; a launch with epoch 0
    /// consumes any record (<see cref="ThrowIfDeviceError"/>).
    /// </summary>
    internal static void ThrowIfLaunchError(uint epoch)
    {
        var ring = (GSharpDeviceErrorSlot*)Volatile.Read(ref s_hostLaunchErrors);
        if (epoch == 0u || ring == null)
        {
            ThrowIfDeviceError();
            return;
        }

        // A record that another launch still held when the error happened sends it to the shared record, tagged with the
        // epoch (Prelude.cu, __gsharp_record_error)
        if (TryConsumeRecord(ring + epoch % LaunchRecords, epoch, matchEpoch: true, out var slot) ||
            TryConsumeRecord(s_hostErrorSlot, epoch, matchEpoch: true, out slot))
        {
            throw GSharpErrorMapper.CreateExceptionFromSlot(in slot, innerException: null);
        }
    }

    private static ref uint AbortWord(GSharpDeviceErrorSlot* ring, uint index) =>
        ref ((uint*)(ring + LaunchRecords))[index];

    // Consumes a record that holds an error (of the launch with epoch, when matchEpoch) and clears it for the next claim
    private static bool TryConsumeRecord(GSharpDeviceErrorSlot* record, uint epoch, bool matchEpoch,
        out GSharpDeviceErrorSlot slot)
    {
        slot = default;
        if (record == null || (Volatile.Read(ref record->Code) & GSharpErrorMapper.ErrorCodeMask) == 0u)
        {
            return false;
        }

        lock (s_errorSlotLock)
        {
            // Code and Epoch are the 8-byte word that the device claims with one CAS, so they are read together
            ref var claim = ref *(ulong*)record;
            var word = Volatile.Read(ref claim);
            var rawCode = (uint)word;
            if ((rawCode & GSharpErrorMapper.ErrorCodeMask) == 0u || (matchEpoch && (uint)(word >> 32) != epoch))
            {
                return false;
            }

            // Wait briefly if the device winner set the error code but PCIe completion bit is still in flight
            for (var spin = 0; spin < 1000 && (rawCode & GSharpErrorMapper.CompletionBit) == 0u; spin++)
            {
                Thread.SpinWait(16);
                rawCode = Volatile.Read(ref record->Code);
            }

            slot = *record;
            slot.Code = rawCode;
            // [WHY] The payload is cleared before the claim word, so a device thread that claims the record as soon as
            // the word is 0 writes its payload after these stores
            record->SiteId = 0u;
            record->ModuleId = 0u;
            record->ThreadLinear = 0u;
            record->BlockX = 0u;
            record->BlockYZ = 0u;
            Volatile.Write(ref claim, 0ul);
            return true;
        }
    }

    /// <summary>
    /// Frees the pinned host-mapped error records during runtime shutdown.
    /// </summary>
    public static void FreeInCurrentContext()
    {
        lock (s_errorSlotLock)
        {
            var ring = Volatile.Read(ref s_hostLaunchErrors);
            if (ring != IntPtr.Zero)
            {
                Volatile.Write(ref s_hostLaunchErrors, IntPtr.Zero);
                s_deviceLaunchErrorsPtr = 0;
                CudaDriverApi.cuMemFreeHost(ring);
            }

            if (s_hostErrorSlot != null)
            {
                CudaDriverApi.cuMemFreeHost((IntPtr)s_hostErrorSlot);
                s_hostErrorSlot = null;
                s_deviceErrorSlotPtr = 0;
            }
        }
    }
}
