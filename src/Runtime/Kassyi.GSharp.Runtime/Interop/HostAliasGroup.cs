namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>The state of one group of overlapping arguments of a launch (<see cref="HostAliasGroups"/>).</summary>
internal struct HostAliasGroup
{
    // The member with the lowest address, whose address is the start of the group
    public int Leader;

    // The bytes of the group on the host, and in its device region (more for a converted group)
    public nint Length;
    public nint DeviceLength;
    public nint Shift;
    public int Alignment;
    public bool CopiesIn;
    public bool CopiesBack;

    // 0 when the device reads the bytes as they are; otherwise the converted element type of every member, whose host
    // and device sizes these are
    public int ConversionKey;
    public nint HostElementSize;
    public nint DeviceElementSize;

    // Every member is copied in and none is copied back ([In] on the CPU)
    public bool KernelGetsCopy;
    public string Names;
    public IntPtr Region;
    public IntPtr Base;
    public Array? HostCopy;

    // The element size of a typed HostCopy, and whether its elements hold references, so that a member of another
    // element type of the same size can take it reinterpreted
    public int HostCopyElementSize;
    public bool HostCopyHasReferences;

    // Every member is a span (HostAliasGroups.SpanView), so the CPU copy is one byte buffer that each views, and the
    // byte at which the copy of the group starts in it
    public bool ViewsOnly;
    public int CopyStart;

    // A converted group: the pinned buffer of its elements in the device layout, which the CPU kernel reads in place
    // and the CUDA launcher copies to and from the region, and the byte at which the group starts in it
    public byte[]? Staging;
    public int StagingStart;
}
