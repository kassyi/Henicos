namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>The state of one group of overlapping arguments of a launch (<see cref="HostAliasGroups"/>).</summary>
internal struct HostAliasGroup
{
    // The member with the lowest address, whose address is the start of the group
    public int Leader;
    public nint Length;
    public nint Shift;
    public int Alignment;
    public bool CopiesIn;
    public bool CopiesBack;

    // Every member is copied in and none is copied back ([In] on the CPU)
    public bool KernelGetsCopy;
    public string Names;
    public IntPtr Region;
    public IntPtr Base;
    public Array? HostCopy;

    // Every member is a span (HostAliasGroups.SpanView), so the CPU copy is one byte buffer that each views, and the
    // byte at which the copy of the group starts in it
    public bool ViewsOnly;
    public int CopyStart;
}
