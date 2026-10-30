namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// The device regions that one CUDA launch allocates for the arrays its struct arguments hold, one region per host array.
/// </summary>
/// <remarks>
/// In C# two fields that hold the same array refer to one object, so a write through one of them is seen through the
/// other. The generated <c>Marshal_</c> methods therefore look an array up here by reference identity before they copy
/// it, and give every field that holds it the region of its first copy (#707). The launcher frees each region once after
/// the launch (<see cref="Free"/>).
/// </remarks>
public sealed class DeviceArrayRegions
{
    private readonly List<IntPtr> _regions = [];
    private Dictionary<object, IntPtr>? _byArray;

    /// <summary>Gets the region already allocated for <paramref name="array"/> in this launch.</summary>
    /// <param name="array">The host array, compared by reference.</param>
    /// <param name="region">The device region of <paramref name="array"/>, or <see cref="IntPtr.Zero"/>.</param>
    /// <returns>Whether <paramref name="array"/> already has a region.</returns>
    public bool TryGetRegion(object array, out IntPtr region)
    {
        region = IntPtr.Zero;
        return _byArray is not null && _byArray.TryGetValue(array, out region);
    }

    /// <summary>Records the region allocated for <paramref name="array"/>, which <see cref="Free"/> releases.</summary>
    /// <param name="array">The host array, compared by reference.</param>
    /// <param name="region">The device region that holds the copy of <paramref name="array"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="array"/> already has a region.</exception>
    public void Add(object array, IntPtr region)
    {
        _byArray ??= new Dictionary<object, IntPtr>(ReferenceEqualityComparer.Instance);
        _byArray.Add(array, region);
        _regions.Add(region);
    }

    /// <summary>Frees every recorded region on <paramref name="hStream"/> and forgets them.</summary>
    /// <param name="hStream">The stream of the launch.</param>
    public void Free(IntPtr hStream)
    {
        foreach (var region in _regions)
        {
            // [WHY] Runs in the launcher's finally block: a failed free must not hide the launch's own exception
            _ = CudaDriverApi.cuMemFreeAsync(region, hStream);
        }

        _regions.Clear();
        _byArray?.Clear();
    }
}
