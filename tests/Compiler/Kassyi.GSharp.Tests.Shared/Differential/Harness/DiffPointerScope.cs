#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if GSHARP_CUDA
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
#endif

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Allocates and uploads device buffers (on <c>GSHARP_CUDA</c>) or unmanaged staging buffers (on CPU) for
/// <c>Launch{Kernel}Ptr</c> invocations, copying results back to the underlying <see cref="DiffBuffers"/>
/// and freeing resources upon disposal.
/// </summary>
public sealed class DiffPointerScope : IDisposable
{
    private readonly DiffBuffers _buffers;
    private readonly Dictionary<string, IntPtr> _pointers = new(StringComparer.Ordinal);
#if GSHARP_CUDA
    private readonly GpuKernelLoader.CudaContextScope _contextScope;
    private readonly List<(DiffBuffer Buffer, GpuBuffer<byte> DeviceBuffer)> _deviceAllocations = [];
#else
    private readonly List<(DiffBuffer Buffer, IntPtr StagingPointer, int ByteLength)> _hostAllocations = [];
#endif
    private bool _disposed;

    internal unsafe DiffPointerScope(DiffBuffers buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        _buffers = buffers;

#if GSHARP_CUDA
        CudaCapability.EnsureGpuAvailable();
        _contextScope = new GpuKernelLoader.CudaContextScope();
        var sharedDeviceBuffers = new Dictionary<object, GpuBuffer<byte>>(ReferenceEqualityComparer.Instance);
#else
        var sharedStagingPointers = new Dictionary<object, IntPtr>(ReferenceEqualityComparer.Instance);
#endif

        foreach (var buffer in _buffers.All)
        {
            if (buffer.IsNullArray || buffer.Length == 0 || !buffer.CanPinAsBytes)
            {
                _pointers[buffer.Name] = IntPtr.Zero;
                continue;
            }

            var rawArray = buffer.RawArray;
#if GSHARP_CUDA
            if (!sharedDeviceBuffers.TryGetValue(rawArray, out var deviceBuffer))
            {
                var hostBytes = buffer.AsReadOnlyBytes();
                deviceBuffer = new GpuBuffer<byte>(hostBytes.Length);
                deviceBuffer.CopyToDevice(hostBytes);
                sharedDeviceBuffers.Add(rawArray, deviceBuffer);
                _deviceAllocations.Add((buffer, deviceBuffer));
            }

            _pointers[buffer.Name] = deviceBuffer.DevicePointer;
#else
            if (!sharedStagingPointers.TryGetValue(rawArray, out var stagingPtr))
            {
                var hostBytes = buffer.AsReadOnlyBytes();
                stagingPtr = Marshal.AllocHGlobal(hostBytes.Length);
                hostBytes.CopyTo(new Span<byte>((void*)stagingPtr, hostBytes.Length));
                sharedStagingPointers.Add(rawArray, stagingPtr);
                _hostAllocations.Add((buffer, stagingPtr, hostBytes.Length));
            }

            _pointers[buffer.Name] = stagingPtr;
#endif
        }
    }

    /// <summary>Gets the device (or staging) pointer for the buffer named <paramref name="name"/>.</summary>
    public IntPtr Get(string name) =>
        _pointers.TryGetValue(name, out var ptr)
            ? ptr
            : throw new ArgumentException($"No device pointer registered for buffer '{name}'.", nameof(name));

    /// <summary>Gets the device (or staging) pointer for the unmanaged buffer named <paramref name="name"/>.</summary>
    public IntPtr PinOrUpload<T>(string name)
        where T : unmanaged => Get(name);

    /// <summary>Copies device/staging memory back to the host buffers and releases all allocations.</summary>
    public unsafe void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

#if GSHARP_CUDA
        try
        {
            foreach (var (buffer, deviceBuffer) in _deviceAllocations)
            {
                try
                {
                    var writable = buffer.AsWritableBytes();
                    if (!writable.IsEmpty)
                    {
                        deviceBuffer.CopyToHost(writable);
                    }
                }
                finally
                {
                    deviceBuffer.Dispose();
                }
            }
        }
        finally
        {
            _contextScope.Dispose();
        }
#else
        foreach (var (buffer, stagingPtr, byteLength) in _hostAllocations)
        {
            try
            {
                var writable = buffer.AsWritableBytes();
                if (!writable.IsEmpty)
                {
                    new ReadOnlySpan<byte>((void*)stagingPtr, byteLength).CopyTo(writable);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(stagingPtr);
            }
        }

        _hostAllocations.Clear();
#endif
    }
}
