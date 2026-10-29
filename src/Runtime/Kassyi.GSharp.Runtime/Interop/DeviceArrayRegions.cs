using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// The device regions that one CUDA launch allocates for the arrays it copies, one region per host array: the array
/// arguments, the arrays that struct arguments hold and the arrays that converted elements hold.
/// </summary>
/// <remarks>
/// In C# two holders of the same array (two arguments, two fields, an argument and a field) refer to one object, so a
/// write through one of them is seen through the others. The launcher and the generated <c>Marshal_</c> methods
/// therefore look an array up here by reference identity before they copy it, and give every holder the region of its
/// first copy (#707, #721). An array argument copies its region back only once (<see cref="TryClaimCopyBack"/>). The
/// launcher frees each region once after the launch (<see cref="Free"/>).
/// </remarks>
public sealed class DeviceArrayRegions
{
    private readonly List<IntPtr> _regions = [];
    private Dictionary<object, Entry>? _byArray;

    /// <summary>Gets the region already allocated for <paramref name="array"/> in this launch.</summary>
    /// <param name="array">The host array, compared by reference.</param>
    /// <param name="region">The device region of <paramref name="array"/>, or <see cref="IntPtr.Zero"/>.</param>
    /// <returns>Whether <paramref name="array"/> already has a region.</returns>
    public bool TryGetRegion(object array, out IntPtr region)
    {
        if (_byArray is not null && _byArray.TryGetValue(array, out var entry))
        {
            region = entry.Region;
            return true;
        }

        region = IntPtr.Zero;
        return false;
    }

    /// <summary>Records the region allocated for <paramref name="array"/>, which <see cref="Free"/> releases.</summary>
    /// <param name="array">The host array, compared by reference.</param>
    /// <param name="region">The device region that holds the copy of <paramref name="array"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="array"/> already has a region.</exception>
    public void Add(object array, IntPtr region)
    {
        _byArray ??= new Dictionary<object, Entry>(ReferenceEqualityComparer.Instance);
        _byArray.Add(array, new Entry(region));
        _regions.Add(region);
    }

    /// <summary>
    /// Claims the copy of <paramref name="array"/> back to the host for the caller, so that an array that several
    /// arguments hold is copied back once.
    /// </summary>
    /// <param name="array">The host array, compared by reference.</param>
    /// <returns>
    /// <see langword="true"/> for the first claim of a recorded array, and for an array without a region; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public bool TryClaimCopyBack(object array)
    {
        if (_byArray is null)
        {
            return true;
        }

        ref var entry = ref CollectionsMarshal.GetValueRefOrNullRef(_byArray, array);
        if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref entry))
        {
            return true;
        }

        if (entry.CopiedBack)
        {
            return false;
        }

        entry.CopiedBack = true;
        return true;
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

    // The region of one host array, and whether an array argument already copied it back
    private struct Entry(IntPtr region)
    {
        public readonly IntPtr Region = region;
        public bool CopiedBack;
    }
}
