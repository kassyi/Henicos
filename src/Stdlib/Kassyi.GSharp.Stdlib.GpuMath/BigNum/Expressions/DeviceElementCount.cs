namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The number of elements of a vector expression after evaluation: a host value, or a device word with
/// a host-side offset and limit, <c>count = clamp(*Base - Skip, 0, Limit)</c>.
/// </summary>
/// <remarks>
/// Skip and Take compose in closed form: <c>clamp(clamp(b - s, 0, L) - n, 0, ...)</c> is
/// <c>clamp(b - (s + n), 0, max(L - n, 0))</c>, and <c>min(clamp(b - s, 0, L), t)</c> is <c>clamp(b - s, 0, min(L, t))</c>,
/// so any number of them needs neither a kernel nor a word of memory.
/// </remarks>
/// <param name="Base">The device count word written by a <c>Where</c>, or <see cref="IntPtr.Zero"/> for a host count.</param>
/// <param name="Skip">The number of elements skipped after the device count.</param>
/// <param name="Limit">The upper bound of the count; the count itself when <paramref name="Base"/> is zero.</param>
internal readonly record struct DeviceElementCount(IntPtr Base, int Skip, int Limit)
{
    /// <summary>A count known on the host.</summary>
    public static DeviceElementCount Static(int length) => new(IntPtr.Zero, 0, length);

    /// <summary>Gets whether the count is known on the host.</summary>
    public bool IsStatic => Base == IntPtr.Zero;

    /// <summary>Gets the upper bound of the count, known on the host.</summary>
    public int Capacity => Limit;

    /// <summary>The count after skipping <paramref name="n"/> elements.</summary>
    public DeviceElementCount SkipFirst(int n) =>
        IsStatic ? Static(Math.Max(Limit - n, 0)) : new(Base, Skip + n, Math.Max(Limit - n, 0));

    /// <summary>The count after taking at most <paramref name="n"/> elements.</summary>
    public DeviceElementCount TakeFirst(int n) =>
        IsStatic ? Static(Math.Min(Limit, n)) : new(Base, Skip, Math.Min(Limit, n));

    /// <summary>
    /// Reads the count on the host: one synchronization of the workspace stream when the count is on the device.
    /// </summary>
    /// <exception cref="InvalidOperationException">The count is on the device and the stream is capturing a graph.</exception>
    public int Read(GpuWorkspace workspace)
    {
        if (IsStatic)
        {
            return Limit;
        }

        // Every host read goes through here (Materialize, the host element loops), so a capture is refused once
        DeviceCounted.ThrowIfCapturing(workspace);
        var n = (long)workspace.Provider.ReadWord(Base, 0, workspace.Stream) - Skip;
        return (int)Math.Clamp(n, 0, Limit);
    }

    // The one count word as a kernel array, or a null array for a host count
    private GSharpArrayProxy BaseProxy => IsStatic ? default : new GSharpArrayProxy(Base, 1);

    /// <summary>The kernel argument for this count.</summary>
    public GpuBigIntVectorKernels.AotContexts.ElementCount ToContext() =>
        new() { Base = BaseProxy, Skip = Skip, Limit = Limit };

    /// <summary>
    /// The kernel argument for the scan and scatter kernels, whose class has its own host copy of the same context.
    /// </summary>
    public GpuStreamCompactionKernels.AotContexts.ElementCount ToScanContext() =>
        new() { Base = BaseProxy, Skip = Skip, Limit = Limit };
}
