using System.Buffers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// A <c>Memory&lt;T&gt;</c> over the bytes of an array of another element type: the CPU copy of a group of overlapping
/// <c>[In]</c> arguments (<see cref="HostAliasCopies.TryGetHostMemory{T}"/>) that a <c>Memory&lt;T&gt;</c> member views
/// with its own element type, as C# lets a <c>MemoryManager&lt;T&gt;</c> view any memory.
/// </summary>
/// <typeparam name="T">The element type of the view, without references.</typeparam>
/// <param name="array">The array whose bytes the view covers.</param>
/// <param name="byteOffset">The byte at which the view starts in <paramref name="array"/>.</param>
/// <param name="length">The number of <typeparamref name="T"/> elements.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class HostAliasMemoryManager<T>(Array array, int byteOffset, int length) : MemoryManager<T>
{
    /// <inheritdoc/>
    public override Span<T> GetSpan() =>
        MemoryMarshal.CreateSpan(ref Unsafe.As<byte, T>(ref First()), length);

    /// <inheritdoc/>
    public override unsafe MemoryHandle Pin(int elementIndex = 0)
    {
        var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
        var pointer = Unsafe.Add<T>(Unsafe.AsPointer(ref First()), elementIndex);
        return new MemoryHandle(pointer, handle, this);
    }

    /// <inheritdoc/>
    public override void Unpin()
    {
        // The handle that Pin returns frees itself when the MemoryHandle is disposed
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        // Nothing to release: the array belongs to the launch's copies
    }

    private ref byte First() => ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), byteOffset);
}
