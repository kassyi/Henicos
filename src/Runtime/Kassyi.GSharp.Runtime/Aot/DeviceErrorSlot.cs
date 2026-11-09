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
/// When a device check fails, the failing warp elects a leader that atomically claims an error record and writes the
/// error code, site ID, module ID and coordinates; the failing threads then exit and the kernel completes. A launch
/// through a generated launcher records under its epoch (<see cref="LaunchErrorRecords"/>), and its launcher throws its
/// own record. This class owns the record of the launches without an epoch (a launch captured into a CUDA graph, or
/// every launch when the per-launch records could not be allocated), which they share.
/// </para>
/// <para>
/// A stream synchronization (<see cref="ThrowIfDeviceError"/>) consumes any record that no launcher is about to read,
/// and a kernel fault that the driver reports (<see cref="CreateExceptionForDriverError"/>) consumes any record.
/// </para>
/// </remarks>
public static unsafe class DeviceErrorSlot
{
    private static readonly object s_errorSlotLock = new();
    private static GSharpDeviceErrorSlot* s_hostErrorSlot;
    private static ulong s_deviceErrorSlotPtr;

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
            LaunchErrorRecords.Allocate();
        }
    }

    // A zeroed block of pinned host memory mapped into the device address space
    internal static bool TryAllocateMapped(int bytes, out IntPtr hostPtr, out ulong devicePtr, out string reason)
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
            $"The device error slot could not be allocated ({reason}); " +
            "a failing device check traps and fails the launch with a CudaException.");

    /// <summary>
    /// Injects the device error record pointers and module ID into the loaded CUDA module's globals.
    /// </summary>
    /// <param name="module">The native CUDA module handle.</param>
    /// <param name="moduleId">The 1-based error module ID allocated for this module.</param>
    public static void ConfigureModule(IntPtr module, uint moduleId)
    {
        SetPointerGlobal(module, "__gsharp_error_slot", s_deviceErrorSlotPtr);
        SetPointerGlobal(module, "__gsharp_launch_errors", LaunchErrorRecords.DevicePointer);

        if (moduleId != 0 &&
            CudaDriverApi.cuModuleGetGlobal_v2(out var idGlobalDptr, out var idGlobalSize, module,
                "__gsharp_error_module_id") == CUresult.CudaSuccess &&
            idGlobalSize == sizeof(uint))
        {
            CudaDriverApi.cuMemcpyHtoD_v2(idGlobalDptr, (IntPtr)(&moduleId), sizeof(uint));
        }
    }

    private static void SetPointerGlobal(IntPtr module, string name, ulong value)
    {
        if (value != 0 &&
            CudaDriverApi.cuModuleGetGlobal_v2(out var globalDptr, out var globalSize, module, name) ==
            CUresult.CudaSuccess &&
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
    /// Consumes a recorded device error and throws the corresponding standard .NET exception.
    /// </summary>
    /// <param name="innerException">Optional inner <see cref="CudaException"/> from the CUDA driver.</param>
    /// <remarks>
    /// <b>[NOTE]:</b> This consumes the record of any launch whose launcher is not about to read it itself, also of a
    /// launch on another stream that has not been synchronized yet.
    /// </remarks>
    public static void ThrowIfDeviceError(Exception? innerException = null)
    {
        if (TryConsumeSlot(out var slot))
        {
            throw GSharpErrorMapper.CreateExceptionFromSlot(in slot, innerException);
        }
    }

    /// <summary>
    /// Creates a managed exception for a non-success <see cref="CUresult"/>, translating any recorded device error into
    /// the matching standard .NET exception.
    /// </summary>
    /// <param name="result">The non-success CUDA driver result code.</param>
    /// <param name="fallbackMessage">An optional fallback error message.</param>
    /// <returns>The constructed standard .NET exception or <see cref="CudaException"/>.</returns>
    public static Exception CreateExceptionForDriverError(CUresult result, string? fallbackMessage = null)
    {
        var cudaEx = new CudaException(result, fallbackMessage);
        // [WHY] Only a fault of a running kernel can come with a record (a thread that traps after recording its
        // error), and it ends every launch of the context, so any record may be taken, also one a launcher waits for.
        // Any other failure, such as a launch the device rejects, would take the record of an unrelated launch.
        if (IsKernelFault(result) && TryConsume(includeOwned: true, out var slot))
        {
            return GSharpErrorMapper.CreateExceptionFromSlot(
                in slot,
                innerException: cudaEx);
        }

        return cudaEx;
    }

    // The results by which the driver reports that a running kernel faulted: illegal address (700), launch timeout
    // (702), device assert (710), hardware stack error (714), illegal instruction (715, a trap), misaligned address
    // (716), invalid address space (717), invalid PC (718) and launch failure (719)
    private static bool IsKernelFault(CUresult result) =>
        (int)result is 700 or 702 or 710 or (>= 714 and <= 719);

    /// <summary>
    /// Attempts to consume and reset a recorded device error of a launch whose launcher is not about to read it itself.
    /// </summary>
    /// <param name="slot">When this method returns <see langword="true"/>, contains the consumed error record.</param>
    /// <returns><see langword="true"/> if a device error was recorded and consumed; otherwise, <see langword="false"/>.</returns>
    public static bool TryConsumeSlot(out GSharpDeviceErrorSlot slot) => TryConsume(includeOwned: false, out slot);

    private static bool TryConsume(bool includeOwned, out GSharpDeviceErrorSlot slot) =>
        TryConsumeRecord(s_hostErrorSlot, RecordFilter.Any(includeOwned), out slot) ||
        LaunchErrorRecords.TryConsumeAny(includeOwned, out slot);

    /// <summary>Which records <see cref="TryConsumeRecord"/> takes.</summary>
    /// <param name="Epoch">The epoch the record must carry when <paramref name="MatchEpoch"/> is set.</param>
    /// <param name="MatchEpoch">Take only the record of <paramref name="Epoch"/>.</param>
    /// <param name="IncludeOwned">Take also a record whose launcher is about to read it (LaunchErrorRecords).</param>
    internal readonly record struct RecordFilter(uint Epoch, bool MatchEpoch, bool IncludeOwned)
    {
        public static RecordFilter Any(bool includeOwned) => new(0u, false, includeOwned);

        public static RecordFilter Of(uint epoch) => new(epoch, true, true);
    }

    /// <summary>
    /// Consumes a complete record that <paramref name="filter"/> accepts and clears it for the next claim.
    /// </summary>
    /// <remarks>
    /// A record whose completion bit is not set yet is left alone: its writer is a thread of a launch that is still
    /// running (a launcher reads its own record only after the launch completed). Consuming it would take a payload
    /// that is still being written and clear the claim, and the writer would then complete a record with no epoch.
    /// </remarks>
    internal static bool TryConsumeRecord(GSharpDeviceErrorSlot* record, RecordFilter filter,
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
            var epoch = (uint)(word >> 32);
            if ((rawCode & GSharpErrorMapper.ErrorCodeMask) == 0u ||
                (rawCode & GSharpErrorMapper.CompletionBit) == 0u ||
                (filter.MatchEpoch && epoch != filter.Epoch) ||
                (!filter.IncludeOwned && LaunchErrorRecords.IsOwned(epoch)))
            {
                return false;
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

    // The record of the launches without an epoch, for LaunchErrorRecords (a record that another launch held when the
    // error happened goes there, tagged with the epoch)
    internal static GSharpDeviceErrorSlot* SharedRecord => s_hostErrorSlot;

    /// <summary>
    /// Frees the pinned host-mapped error records during runtime shutdown.
    /// </summary>
    public static void FreeInCurrentContext()
    {
        lock (s_errorSlotLock)
        {
            LaunchErrorRecords.Free();
            if (s_hostErrorSlot != null)
            {
                CudaDriverApi.cuMemFreeHost((IntPtr)s_hostErrorSlot);
                s_hostErrorSlot = null;
                s_deviceErrorSlotPtr = 0;
            }
        }
    }
}
