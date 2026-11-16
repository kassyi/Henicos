#nullable enable
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public sealed partial class DiffPointerScope
{
    private GpuKernelLoader.CudaContextScope _contextScope;
    private readonly List<(DiffBuffer Buffer, GpuBuffer<byte> DeviceBuffer)> _deviceAllocations = [];
    private readonly Dictionary<object, GpuBuffer<byte>> _sharedDeviceBuffers = new(ReferenceEqualityComparer.Instance);

    private partial void InitializeStorage()
    {
        CudaCapability.EnsureGpuAvailable();
        _contextScope = new GpuKernelLoader.CudaContextScope();
    }

    private partial IntPtr AllocateAndUpload(DiffBuffer buffer)
    {
        var rawArray = buffer.RawArray;
        if (!_sharedDeviceBuffers.TryGetValue(rawArray, out var deviceBuffer))
        {
            var hostBytes = buffer.AsReadOnlyBytes();
            deviceBuffer = new GpuBuffer<byte>(hostBytes.Length);
            deviceBuffer.CopyToDevice(hostBytes);
            _sharedDeviceBuffers.Add(rawArray, deviceBuffer);
            _deviceAllocations.Add((buffer, deviceBuffer));
        }

        return deviceBuffer.DevicePointer;
    }

    private partial void DisposeStorage()
    {
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
    }
}
