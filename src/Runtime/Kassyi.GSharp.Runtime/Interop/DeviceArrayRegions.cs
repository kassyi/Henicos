using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// The device regions that one CUDA launch allocates for the arrays and views it copies, one region per host array or
/// shared view: the array arguments, the arrays and views that struct arguments hold, and the arrays that converted
/// elements hold.
/// </summary>
/// <remarks>
/// In C# two holders of the same array (two arguments, two fields, an argument and a field) refer to one object, so a
/// write through one of them is seen through the others. The launcher and the generated <c>Marshal_</c> methods
/// therefore look an array up here by reference identity before they copy it, and give every holder the region of its
/// first copy (#707, #721). A view held in a struct field that overlaps another argument shares that group's region by
/// pinned address and byte length (<see cref="AddSharedView"/>, <see cref="TryGetViewRegion{T}"/>, #1849). An array
/// argument copies its region back only once (<see cref="TryClaimCopyBack"/>). The launcher frees each owned region
/// once after the launch (<see cref="Free"/>). A launch whose only holders are array arguments compares them pairwise
/// instead and has no table.
/// </remarks>
public sealed class DeviceArrayRegions
{
    // Created on the first Add, so that a launch whose arrays are all null or empty allocates only this object
    private List<IntPtr>? _regions;
    private Dictionary<object, Entry>? _byArray;
    private List<(IntPtr HostAddress, nint ByteLength, IntPtr Region)>? _sharedViews;

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
        (_regions ??= []).Add(region);
    }

    /// <summary>
    /// Records a region that this launch allocated for a view that no array identity names (the slice that a
    /// <c>Memory&lt;T&gt;</c> or <c>ArraySegment&lt;T&gt;</c> field covers), which <see cref="Free"/> releases.
    /// </summary>
    /// <param name="region">The device region.</param>
    public void AddOwned(IntPtr region) => (_regions ??= []).Add(region);

    /// <summary>
    /// Records a region that another owner allocated and frees for <paramref name="array"/>: the region of a group
    /// of overlapping arguments (<see cref="HostAliasGroups"/>), so that a struct field that holds the same array
    /// takes it. Two <c>T[]</c> arguments that are one array record it once: both start the group's region at the
    /// same address.
    /// </summary>
    /// <param name="array">The host array, compared by reference.</param>
    /// <param name="region">The device address of the array's first element.</param>
    public void AddShared(object array, IntPtr region)
    {
        _byArray ??= new Dictionary<object, Entry>(ReferenceEqualityComparer.Instance);
        _byArray.TryAdd(array, new Entry(region));
    }

    /// <summary>
    /// Records a region that another owner allocated and frees for a view held in a struct field
    /// (<see cref="HostAliasGroups"/>), keyed by pinned host address and byte length while the launcher's
    /// <see cref="HostAliasHeldArrays"/> keeps the view pinned.
    /// </summary>
    /// <param name="hostAddress">The pinned start address of the view.</param>
    /// <param name="byteLength">The length of the view in bytes.</param>
    /// <param name="region">The device address of the view's first element.</param>
    public void AddSharedView(IntPtr hostAddress, nint byteLength, IntPtr region)
    {
        _sharedViews ??= [];
        var entry = (hostAddress, byteLength, region);
        if (!_sharedViews.Contains(entry))
        {
            _sharedViews.Add(entry);
        }
    }

    /// <summary>
    /// Gets the shared group region recorded for <paramref name="view"/> by its pinned host address and byte length.
    /// </summary>
    /// <param name="view">The view's span, pinned during lookup.</param>
    /// <param name="region">The device region of the view, or <see cref="IntPtr.Zero"/>.</param>
    /// <typeparam name="T">The element type of the view.</typeparam>
    /// <returns>Whether <paramref name="view"/> has a shared group region.</returns>
    public unsafe bool TryGetViewRegion<T>(ReadOnlySpan<T> view, out IntPtr region)
    {
        if (_sharedViews is null || view.IsEmpty || RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            region = IntPtr.Zero;
            return false;
        }

        fixed (byte* ptr = &Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(view)))
        {
            var hostAddress = (IntPtr)ptr;
            var byteLength = (nint)view.Length * Unsafe.SizeOf<T>();
            foreach (var entry in _sharedViews)
            {
                if (entry.HostAddress == hostAddress && entry.ByteLength == byteLength)
                {
                    region = entry.Region;
                    return true;
                }
            }
        }

        region = IntPtr.Zero;
        return false;
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
        if (Unsafe.IsNullRef(ref entry))
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
        if (_regions is not null)
        {
            foreach (var region in _regions)
            {
                // [WHY] Runs in the launcher's finally block: a failed free must not hide the launch's own exception
                _ = CudaDriverApi.cuMemFreeAsync(region, hStream);
            }

            _regions.Clear();
        }

        _byArray?.Clear();
        _sharedViews?.Clear();
    }

    // The region of one host array, and whether an array argument already copied it back
    private struct Entry(IntPtr region)
    {
        public readonly IntPtr Region = region;
        public bool CopiedBack;
    }
}
