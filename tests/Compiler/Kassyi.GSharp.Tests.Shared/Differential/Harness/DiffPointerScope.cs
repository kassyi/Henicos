#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Allocates and uploads device buffers (on CUDA) or unmanaged staging buffers (on CPU) for
/// <c>Launch{Kernel}Ptr</c> invocations, copying results back to the underlying <see cref="DiffBuffers"/>
/// and freeing resources upon disposal.
/// </summary>
public sealed partial class DiffPointerScope : IDisposable
{
    private readonly DiffBuffers _buffers;
    private readonly Dictionary<string, IntPtr> _pointers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _lengths = new(StringComparer.Ordinal);
    private bool _disposed;

    internal DiffPointerScope(DiffBuffers buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        _buffers = buffers;
        InitializeStorage();

        foreach (var buffer in _buffers.All)
        {
            _lengths[buffer.Name] = buffer.IsNullArray ? -1 : buffer.Length;
            if (buffer.IsNullArray || buffer.Length == 0 || !buffer.CanPinAsBytes)
            {
                _pointers[buffer.Name] = IntPtr.Zero;
                continue;
            }

            _pointers[buffer.Name] = AllocateAndUpload(buffer);
        }
    }

    private partial void InitializeStorage();
    private partial IntPtr AllocateAndUpload(DiffBuffer buffer);
    private partial void DisposeStorage();

    /// <summary>Gets the device (or staging) pointer for the buffer named <paramref name="name"/>.</summary>
    public IntPtr Get(string name) =>
        _pointers.TryGetValue(name, out var ptr)
            ? ptr
            : throw new ArgumentException($"No device pointer registered for buffer '{name}'.", nameof(name));

    /// <summary>
    /// Gets the buffer named <paramref name="name"/> as an array argument of a <c>Launch{Kernel}Ptr</c> launcher: its
    /// device (or staging) pointer and its element count, the null proxy for a null array.
    /// </summary>
    public GSharpArrayProxy GetArray(string name)
    {
        var pointer = Get(name);
        return _lengths[name] switch
        {
            < 0 => default,
            0 => new GSharpArrayProxy(GSharpArrayProxy.EmptyArrayData, 0),
            var length => new GSharpArrayProxy(pointer, length)
        };
    }

    /// <summary>Gets the device (or staging) pointer for the unmanaged buffer named <paramref name="name"/>.</summary>
    public IntPtr PinOrUpload<T>(string name)
        where T : unmanaged => Get(name);

    /// <summary>Copies device/staging memory back to the host buffers and releases all allocations.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeStorage();
    }
}
