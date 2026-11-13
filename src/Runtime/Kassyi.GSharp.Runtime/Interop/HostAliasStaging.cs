using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// The conversion of a group of overlapping arguments whose elements the launcher converts between the host and the
/// device layout (<see cref="HostAliasGroupLayout.ConversionKey"/>): the elements of the whole group on the host, and
/// one pinned buffer of them in the device layout, which the launcher fills element by element with the generated
/// <c>Marshal_</c> methods and reads back with <c>Unmarshal_</c>. The CPU kernel reads the buffer in place; the CUDA
/// launcher copies it to and from the group's region.
/// </summary>
/// <remarks>
/// Every member of such a group has the same element type and starts at an element boundary
/// (<see cref="HostAliasGroups.Find"/> checks it), so the members address the buffer at their element offsets, and a
/// write through one member is seen through the others, as in C#.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class HostAliasStaging
{
    // The alignment that the buffer gives its first element: the widest device element alignment
    private const int BufferAlignment = 16;

    /// <summary>The host elements of group <paramref name="group"/>, from its member with the lowest address.</summary>
    /// <param name="groups">The groups of the launch.</param>
    /// <param name="group">The group.</param>
    /// <param name="ranges">The range list of <see cref="HostAliasGroups.Find"/>, with the arguments pinned.</param>
    /// <typeparam name="T">The host element type of the group.</typeparam>
    public static unsafe Span<T> GetHostElements<T>(HostAliasGroups groups, int group, ReadOnlySpan<nint> ranges)
    {
        ref var state = ref groups.GroupAt(group);
        var start = (void*)ranges[state.Leader * HostAliasGroups.RangeFields];
        var count = checked((int)(state.Length / state.HostElementSize));
        return MemoryMarshal.CreateSpan(ref Unsafe.AsRef<T>(start), count);
    }

    /// <summary>
    /// The elements of group <paramref name="group"/> in the device layout, in a pinned buffer made on the first call
    /// and kept until the launch ends.
    /// </summary>
    /// <param name="groups">The groups of the launch.</param>
    /// <param name="group">The group.</param>
    /// <typeparam name="TShadow">The device layout of the element (the native shadow struct).</typeparam>
    public static Span<TShadow> GetBuffer<TShadow>(HostAliasGroups groups, int group)
    {
        ref var state = ref groups.GroupAt(group);
        var buffer = state.Staging ?? CreateBuffer(ref state);
        ref var first = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(buffer), state.StagingStart);
        var count = checked((int)(state.DeviceLength / state.DeviceElementSize));
        return MemoryMarshal.CreateSpan(ref Unsafe.As<byte, TShadow>(ref first), count);
    }

    /// <summary>
    /// CPU: the address in the buffer of member <paramref name="index"/>, which the native kernel takes instead of a
    /// shadow of the member's own.
    /// </summary>
    /// <param name="groups">The groups of the launch.</param>
    /// <param name="index">The member.</param>
    public static unsafe IntPtr GetBufferPointer(HostAliasGroups groups, int index)
    {
        ref var state = ref groups.GroupAt(groups.GetGroup(index));
        var buffer = state.Staging ?? CreateBuffer(ref state);
        var start = (IntPtr)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(buffer));
        return start + state.StagingStart + groups.GetDeviceOffset(index);
    }

    /// <summary>CUDA: copies the buffer of group <paramref name="group"/> to its region and waits.</summary>
    /// <param name="groups">The groups of the launch, after <see cref="HostAliasGroups.CopyToDevice"/>.</param>
    /// <param name="group">The group.</param>
    /// <param name="stream">The stream of the launch.</param>
    public static void CopyToDevice(HostAliasGroups groups, int group, IntPtr stream)
    {
        ref var state = ref groups.GroupAt(group);
        CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoDAsync_v2(
            state.Base, GetBufferStart(ref state), state.DeviceLength, stream));
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(stream));
    }

    /// <summary>CUDA: copies the region of group <paramref name="group"/> into its buffer and waits.</summary>
    /// <param name="groups">The groups of the launch.</param>
    /// <param name="group">The group.</param>
    /// <param name="stream">The stream of the launch.</param>
    public static void CopyToHost(HostAliasGroups groups, int group, IntPtr stream)
    {
        ref var state = ref groups.GroupAt(group);
        CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2(
            GetBufferStart(ref state), state.Base, state.DeviceLength, stream));
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(stream));
    }

    private static unsafe IntPtr GetBufferStart(ref HostAliasGroup state)
    {
        var buffer = state.Staging ?? CreateBuffer(ref state);
        return (IntPtr)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(buffer)) + state.StagingStart;
    }

    // A pinned buffer whose group starts past an address aligned to BufferAlignment by the group's shift, so that every
    // member is aligned for its elements, as in the device region
    private static unsafe byte[] CreateBuffer(ref HostAliasGroup state)
    {
        var length = checked((int)state.DeviceLength);
        var buffer = GC.AllocateArray<byte>(length + (2 * BufferAlignment), pinned: true);
        var address = (nint)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(buffer));
        var aligned = (address + (BufferAlignment - 1)) & ~(nint)(BufferAlignment - 1);
        state.Staging = buffer;
        state.StagingStart = checked((int)(aligned - address + state.Shift));
        return buffer;
    }
}
