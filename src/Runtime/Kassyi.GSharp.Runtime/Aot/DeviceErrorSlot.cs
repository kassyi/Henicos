namespace Kassyi.GSharp.Runtime.Aot;

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// Manages the process-wide pinned host-mapped error slot shared between the host and device kernels (#729).
/// </summary>
/// <remarks>
/// <para>
/// <b>[SINGLE-SLOT DESIGN]:</b> A single host-mapped error slot is shared across all CUDA modules, streams, and contexts
/// within the process. When a device trap occurs on any stream, the trapping warp elects a leader that atomically records
/// the error code, site ID, module ID, and coordinates before trapping. Any subsequent host check
/// (<see cref="ThrowIfDeviceError"/>, <see cref="CreateExceptionForDriverError"/>, or stream synchronization) consumes and
/// resets the slot.
/// </para>
/// </remarks>
public static unsafe class DeviceErrorSlot
{
    private static readonly object s_errorSlotLock = new();
    private static GSharpDeviceErrorSlot* s_hostErrorSlot;
    private static ulong s_deviceErrorSlotPtr;

    // Module ID cache indexed by 256-bit signature hash for ErrorSiteTable correlation
    private static readonly ConcurrentDictionary<AotSignatureValidator.Hash256, uint> s_moduleIds = new();

    /// <summary>Gets the device pointer to the pinned error slot.</summary>
    public static ulong DeviceErrorSlotPtr => s_deviceErrorSlotPtr;

    internal static uint GetOrAssignModuleId(AotSignatureValidator.Hash256 hash) =>
        s_moduleIds.GetOrAdd(hash, static _ => GSharpErrorMapper.AllocateModuleId());

    /// <summary>
    /// Allocates the pinned host-mapped error slot in the current CUDA context if not already allocated.
    /// </summary>
    public static void AllocateInCurrentContext()
    {
        lock (s_errorSlotLock)
        {
            if (s_hostErrorSlot != null)
            {
                return;
            }

            var flags = CudaDriverApi.MemHostAllocPortable | CudaDriverApi.MemHostAllocDeviceMap;
            var allocResult = CudaDriverApi.cuMemHostAlloc(out var hostPtr, sizeof(GSharpDeviceErrorSlot), flags);
            if (allocResult != CUresult.CudaSuccess || hostPtr == IntPtr.Zero)
            {
                return;
            }

            var mapResult = CudaDriverApi.cuMemHostGetDevicePointer(out var devPtr, hostPtr, 0);
            if (mapResult != CUresult.CudaSuccess)
            {
                CudaDriverApi.cuMemFreeHost(hostPtr);
                return;
            }

            s_hostErrorSlot = (GSharpDeviceErrorSlot*)hostPtr;
            *s_hostErrorSlot = default;
            s_deviceErrorSlotPtr = (ulong)devPtr;
        }
    }

    /// <summary>
    /// Injects the device error slot pointer and module ID into the loaded CUDA module's globals.
    /// </summary>
    /// <param name="module">The native CUDA module handle.</param>
    /// <param name="moduleId">The 1-based error module ID allocated for this module.</param>
    public static void ConfigureModule(IntPtr module, uint moduleId)
    {
        var devSlotPtr = s_deviceErrorSlotPtr;
        if (devSlotPtr != 0 &&
            CudaDriverApi.cuModuleGetGlobal_v2(out var slotGlobalDptr, out var slotGlobalSize, module, "__gsharp_error_slot") == CUresult.CudaSuccess &&
            slotGlobalSize == (nuint)sizeof(ulong))
        {
            CudaDriverApi.cuMemcpyHtoD_v2(slotGlobalDptr, (IntPtr)(&devSlotPtr), sizeof(ulong));
        }

        if (moduleId != 0 &&
            CudaDriverApi.cuModuleGetGlobal_v2(out var idGlobalDptr, out var idGlobalSize, module, "__gsharp_error_module_id") == CUresult.CudaSuccess &&
            idGlobalSize == sizeof(uint))
        {
            CudaDriverApi.cuMemcpyHtoD_v2(idGlobalDptr, (IntPtr)(&moduleId), sizeof(uint));
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
    /// Inspects the pinned device error slot and throws the corresponding standard .NET exception if a device error was recorded.
    /// </summary>
    /// <param name="innerException">Optional inner <see cref="CudaException"/> from the CUDA driver.</param>
    /// <remarks>
    /// <b>[NOTE]:</b> The error slot is shared process-wide across all streams and modules. When multiple kernels are in flight
    /// and a trap occurs, whichever thread/call executes this method first consumes and clears the slot.
    /// </remarks>
    public static void ThrowIfDeviceError(Exception? innerException = null)
    {
        if (TryConsumeSlot(out var slot))
        {
            throw GSharpErrorMapper.CreateExceptionFromSlot(in slot, innerException);
        }
    }

    /// <summary>
    /// Creates a managed exception for a non-success <see cref="CUresult"/>, translating any recorded device trap slot into the matching standard .NET exception.
    /// </summary>
    /// <param name="result">The non-success CUDA driver result code.</param>
    /// <param name="fallbackMessage">An optional fallback error message.</param>
    /// <returns>The constructed standard .NET exception or <see cref="CudaException"/>.</returns>
    public static Exception CreateExceptionForDriverError(CUresult result, string? fallbackMessage = null)
    {
        var cudaEx = new CudaException(result, fallbackMessage);
        if (TryConsumeSlot(out var slot))
        {
            return GSharpErrorMapper.CreateExceptionFromSlot(
                in slot,
                innerException: cudaEx);
        }

        return cudaEx;
    }

    /// <summary>
    /// Attempts to consume and atomically reset the recorded device error slot.
    /// </summary>
    /// <param name="slot">When this method returns <see langword="true"/>, contains the consumed error slot.</param>
    /// <returns><see langword="true"/> if a device error was recorded and consumed; otherwise, <see langword="false"/>.</returns>
    public static bool TryConsumeSlot(out GSharpDeviceErrorSlot slot)
    {
        lock (s_errorSlotLock)
        {
            if (s_hostErrorSlot == null)
            {
                slot = default;
                return false;
            }

            var rawCode = Volatile.Read(ref s_hostErrorSlot->Code);
            if ((rawCode & GSharpErrorMapper.ErrorCodeMask) == 0u)
            {
                slot = default;
                return false;
            }

            // Wait briefly if the device winner set the error code but PCIe completion bit is still in flight
            for (var spin = 0; spin < 1000 && (rawCode & GSharpErrorMapper.CompletionBit) == 0u; spin++)
            {
                Thread.SpinWait(16);
                rawCode = Volatile.Read(ref s_hostErrorSlot->Code);
            }

            slot = *s_hostErrorSlot;
            slot.Code = rawCode;
            *s_hostErrorSlot = default;
            Volatile.Write(ref s_hostErrorSlot->Code, 0u);
            return true;
        }
    }

    /// <summary>
    /// Frees the pinned host-mapped error slot during runtime shutdown.
    /// </summary>
    public static void FreeInCurrentContext()
    {
        lock (s_errorSlotLock)
        {
            if (s_hostErrorSlot != null)
            {
                CudaDriverApi.cuMemFreeHost((IntPtr)s_hostErrorSlot);
                s_hostErrorSlot = null;
                s_deviceErrorSlotPtr = 0;
            }
        }
    }
}
