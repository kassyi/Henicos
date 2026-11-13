namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// Represents a 32-byte pinned host-mapped device error record shared with CUDA modules (#729).
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct GSharpDeviceErrorSlot
{
    /// <summary>
    /// Low 16 bits: <see cref="GSharpErrorCode"/>; bit 16 (<c>0x10000u</c>): payload write completion flag.
    /// </summary>
    public uint Code;

    /// <summary>
    /// The epoch of the launch that recorded the error
    /// (<see cref="LaunchErrorRecords.NextLaunchEpoch(IntPtr, bool, IntPtr)"/>; bit 31 set for a launch captured into a
    /// CUDA graph), or 0 for a captured launch of a kernel that waits at no barrier counting a fixed number of
    /// arrivals.
    /// </summary>
    public uint Epoch;

    /// <summary>
    /// Local compile-time site ID (1-based), or builtin prelude message ID when high bit is set (<c>0x80000000u</c>).
    /// </summary>
    public uint SiteId;

    /// <summary>
    /// Explicit 1-based module ID assigned to the containing CUDA module or dynamic library.
    /// </summary>
    public uint ModuleId;

    /// <summary>
    /// Packed thread coordinates: <c>(threadIdx.x &amp; 0x3FFu) | ((threadIdx.y &amp; 0x3FFu) &lt;&lt; 10) | ((threadIdx.z &amp; 0x3FFu) &lt;&lt; 20)</c>.
    /// </summary>
    public uint ThreadLinear;

    /// <summary>
    /// Block X coordinate (<c>blockIdx.x</c>). Preserves large grid coordinates up to 2^31 - 1.
    /// </summary>
    public uint BlockX;

    /// <summary>
    /// Packed Block Y and Z coordinates: <c>(blockIdx.y &amp; 0xFFFFu) | ((blockIdx.z &amp; 0xFFFFu) &lt;&lt; 16)</c>.
    /// </summary>
    public uint BlockYZ;
}
