using System.Buffers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// Pins the memory of an array-like struct field that is not an array (<c>Memory&lt;T&gt;</c>,
/// <c>ReadOnlyMemory&lt;T&gt;</c>, <c>ArraySegment&lt;T&gt;</c>, <c>ImmutableArray&lt;T&gt;</c>) for the CPU
/// launch, which passes the address of the view's first element to the native kernel (#1267).
/// </summary>
/// <remarks>
/// The launcher releases every handle it records with <c>GCHandle.Free</c> after the launch. A view over an array or a
/// string pins that object; a view over memory a <c>MemoryManager&lt;T&gt;</c> owns keeps the manager's
/// <c>MemoryHandle</c> in a holder, which releases it when the holder is collected after the launch freed its handle.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class HostArrayViews
{
    /// <summary>
    /// The address of the first element of <paramref name="memory"/>, pinned until the launcher frees the handle it
    /// adds to <paramref name="handles"/>; <see cref="IntPtr.Zero"/> for a view over no memory (<c>default</c>).
    /// </summary>
    /// <param name="memory">The view.</param>
    /// <param name="handles">The handles that the launcher frees after the launch.</param>
    /// <typeparam name="T">The element type.</typeparam>
    /// <returns>The address of the view's first element, also for an empty view over an array.</returns>
    public static unsafe IntPtr Pin<T>(ReadOnlyMemory<T> memory, List<IntPtr> handles)
    {
        ArgumentNullException.ThrowIfNull(handles);

        // [WHY] A default view has no memory; TryGetArray would answer with the shared empty array, which the device
        // would read as an empty array instead of a null one
        if (ArrayPinning.IsNull(memory.Span))
        {
            return IntPtr.Zero;
        }

        if (MemoryMarshal.TryGetArray(memory, out var segment))
        {
            return segment.Array is null
                ? IntPtr.Zero
                : PinObject(segment.Array, segment.Offset, Unsafe.SizeOf<T>(), handles);
        }

        if (typeof(T) == typeof(char) &&
            MemoryMarshal.TryGetString(Unsafe.As<ReadOnlyMemory<T>, ReadOnlyMemory<char>>(ref memory), out var text,
                out var start, out _))
        {
            var handle = GCHandle.Alloc(text, GCHandleType.Pinned);
            handles.Add(GCHandle.ToIntPtr(handle));
            return handle.AddrOfPinnedObject() + (start * sizeof(char));
        }

        var pinned = memory.Pin();
        var holder = new PinnedMemory(pinned);
        handles.Add(GCHandle.ToIntPtr(GCHandle.Alloc(holder)));
        return (IntPtr)pinned.Pointer;
    }

    private static IntPtr PinObject(Array array, int offset, int elementSize, List<IntPtr> handles)
    {
        var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
        handles.Add(GCHandle.ToIntPtr(handle));
        return handle.AddrOfPinnedObject() + (offset * elementSize);
    }

    // Keeps a MemoryManager's pin until the launcher freed the handle of the holder and the holder is collected
    private sealed class PinnedMemory(MemoryHandle handle)
    {
        private MemoryHandle _handle = handle;

        ~PinnedMemory() => _handle.Dispose();
    }
}
