#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public sealed partial class DiffPointerScope
{
    private readonly List<(DiffBuffer Buffer, IntPtr StagingPointer, int ByteLength)> _hostAllocations = [];
    private readonly Dictionary<object, IntPtr> _sharedStagingPointers = new(ReferenceEqualityComparer.Instance);

    private partial void InitializeStorage()
    {
    }

    private partial IntPtr AllocateAndUpload(DiffBuffer buffer)
    {
        var rawArray = buffer.RawArray;
        if (!_sharedStagingPointers.TryGetValue(rawArray, out var stagingPtr))
        {
            var hostBytes = buffer.AsReadOnlyBytes();
            stagingPtr = Marshal.AllocHGlobal(hostBytes.Length);
            unsafe
            {
                hostBytes.CopyTo(new Span<byte>((void*)stagingPtr, hostBytes.Length));
            }
            _sharedStagingPointers.Add(rawArray, stagingPtr);
            _hostAllocations.Add((buffer, stagingPtr, hostBytes.Length));
        }

        return stagingPtr;
    }

    private partial void DisposeStorage()
    {
        foreach (var (buffer, stagingPtr, byteLength) in _hostAllocations)
        {
            try
            {
                var writable = buffer.AsWritableBytes();
                if (!writable.IsEmpty)
                {
                    unsafe
                    {
                        new ReadOnlySpan<byte>((void*)stagingPtr, byteLength).CopyTo(writable);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(stagingPtr);
            }
        }

        _hostAllocations.Clear();
    }
}
