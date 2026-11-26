using System.Buffers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// The arrays and views that the struct arguments of a launch hold in their fields, which the launcher compares by
/// address with its other arguments beside <see cref="HostAliasGroups"/>: a slice of an array or view that a struct
/// argument holds is one memory with it, as in C#, but a <c>Span&lt;T&gt;</c> or view does not name an array
/// object, so it must be found by its address.
/// </summary>
/// <remarks>
/// The generated <c>AliasHeldArrays_</c> methods add the arrays (<see cref="Add{T}"/>) and views
/// (<see cref="AddView{T}"/>) of each struct argument, <see cref="Pin"/> pins them for the launch, and every method
/// that takes the range list takes the one of <see cref="Combine(ReadOnlySpan{nint})"/>, which appends one entry per
/// held array or view to the entries of the arguments. A held array or view that lands in a group takes the group's
/// region (<see cref="AddRegions"/>), so the <c>Marshal_</c> of its struct finds it instead of allocating another one.
/// The launcher calls <see cref="Free"/> in its <c>finally</c> block.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class HostAliasHeldArrays
{
    // The first conversion number of a held array: the launcher numbers the converted element types of its own
    // arguments from 1, so a held array never shares a number with an argument, and a group that holds both is
    // reported as one of converted members of different element types
    internal const int HeldConversionKey = 1000;

    private readonly List<HeldEntry> _entries = [];
    private readonly List<GCHandle> _pins = [];
    private readonly List<MemoryHandle> _viewPins = [];
    private readonly List<string> _convertedTypes = [];
    private Dictionary<Type, (int Key, int DeviceSize)>? _mappedConversions;
    private nint[] _heldRanges = [];
    private byte[] _heldTraits = [];
    private string _heldNames = "";
    private nint[]? _combined;

    // Gives the held array or view the copy of its group, typed by its element type
    private delegate void CopySharer(HostAliasGroups groups, int member, ReadOnlySpan<nint> ranges,
        HostArrayCopies copies, in HeldEntry entry);

    private enum ShareEntryKind
    {
        WholeArray,
        TypedArrayView,
        MemoryView,
    }

    private struct HeldEntry
    {
        public Array? Array;
        public nint Address;
        public nint Length;
        public nint HostSize;
        public nint DeviceSize;
        public nint Alignment;
        public int Conversion;
        public bool CopiesBack;
        public bool RequiresTypedArray;
        public CopySharer Sharer;
    }

    /// <summary>The number of arrays and views held.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Whether the argument whose arrays and views are being added copies them back to the caller. An array or view
    /// that two arguments hold is copied back when either does (provided the view itself is writable).
    /// </summary>
    public bool CopiesBack { get; set; }

    /// <summary>
    /// Maps a converted host element type to the launcher's conversion key and device element size, so that a held
    /// array or view of that element shares a group with a candidate parameter of the same element type.
    /// </summary>
    /// <param name="hostElement">The host element type.</param>
    /// <param name="key">The launcher's conversion key for <paramref name="hostElement"/>.</param>
    /// <param name="deviceSize">The element size on the device.</param>
    public void MapConversion(Type hostElement, int key, int deviceSize)
    {
        ArgumentNullException.ThrowIfNull(hostElement);
        (_mappedConversions ??= [])[hostElement] = (key, deviceSize);
    }

    /// <summary>
    /// Adds an array that a struct argument holds. A null or empty array, an array that was added already (two fields
    /// that hold one array), and an array whose elements hold references (no address can be kept of it) are left out.
    /// </summary>
    /// <param name="array">The array held in a field.</param>
    /// <param name="deviceAlignment">The device alignment of the element.</param>
    /// <param name="deviceSize">The size of the element on the device; 0 when it is the host size.</param>
    /// <param name="converted">Whether the host and device layouts of the element differ.</param>
    /// <typeparam name="T">The element type.</typeparam>
    public void Add<T>(T[]? array, int deviceAlignment, int deviceSize = 0, bool converted = false)
    {
        if (array is null || array.Length == 0 || RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            return;
        }

        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Array is { } existing && ReferenceEquals(existing, array))
            {
                CollectionsMarshal.AsSpan(_entries)[i].CopiesBack |= CopiesBack;
                return;
            }
        }

        var hostSize = Unsafe.SizeOf<T>();
        var (conversionKey, resolvedDeviceSize) = ResolveConversion(typeof(T), deviceSize, hostSize, converted);
        _entries.Add(new HeldEntry
        {
            Array = array,
            Length = (nint)array.Length * hostSize,
            HostSize = hostSize,
            DeviceSize = resolvedDeviceSize,
            Alignment = deviceAlignment,
            Conversion = conversionKey,
            CopiesBack = CopiesBack,
            Sharer = ShareCopy<T>
        });
    }

    /// <summary>
    /// Adds a non-array view (<c>Memory&lt;T&gt;</c>, <c>ReadOnlyMemory&lt;T&gt;</c>, <c>ArraySegment&lt;T&gt;</c>,
    /// <c>ImmutableArray&lt;T&gt;</c>) that a struct argument holds. An empty view and a view whose elements hold
    /// references are left out; every other view is pinned and kept as its own entry so that identical views form a
    /// group in <see cref="HostAliasGroups"/> and register their element type in <see cref="HostArrayCopies"/>.
    /// </summary>
    /// <param name="view">The view held in a field.</param>
    /// <param name="writable">Whether the view itself is writable (<see langword="false"/> for read-only views).</param>
    /// <param name="deviceAlignment">The device alignment of the element.</param>
    /// <param name="deviceSize">The size of the element on the device; 0 when it is the host size.</param>
    /// <param name="converted">Whether the host and device layouts of the element differ.</param>
    /// <param name="requiresTypedArray">
    /// Whether the view's container (<c>ArraySegment&lt;T&gt;</c>, <c>ImmutableArray&lt;T&gt;</c>) requires a typed
    /// array copy on the CPU.
    /// </param>
    /// <typeparam name="T">The element type.</typeparam>
    public unsafe void AddView<T>(ReadOnlyMemory<T> view, bool writable, int deviceAlignment, int deviceSize = 0,
        bool converted = false, bool requiresTypedArray = false)
    {
        if (view.IsEmpty || RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            return;
        }

        var hostSize = Unsafe.SizeOf<T>();
        var (conversionKey, resolvedDeviceSize) = ResolveConversion(typeof(T), deviceSize, hostSize, converted);
        var byteLength = (nint)view.Length * hostSize;
        var handle = view.Pin();
        _viewPins.Add(handle);
        _entries.Add(new HeldEntry
        {
            Address = (nint)handle.Pointer,
            Length = byteLength,
            HostSize = hostSize,
            DeviceSize = resolvedDeviceSize,
            Alignment = deviceAlignment,
            Conversion = conversionKey,
            CopiesBack = CopiesBack && writable,
            RequiresTypedArray = requiresTypedArray,
            Sharer = ShareCopyView<T>
        });
    }

    private (int Key, nint DeviceSize) ResolveConversion(Type element, int deviceSize, nint hostSize, bool converted)
    {
        if (!converted)
        {
            return (0, deviceSize == 0 ? hostSize : deviceSize);
        }

        if (_mappedConversions is not null && _mappedConversions.TryGetValue(element, out var mapped))
        {
            return (mapped.Key, deviceSize == 0 ? mapped.DeviceSize : deviceSize);
        }

        return (GetConversionKey(element), deviceSize == 0 ? hostSize : deviceSize);
    }

    // The conversion number of an element type, numbered on its first use
    private int GetConversionKey(Type element)
    {
        var name = element.FullName ?? element.Name;
        var index = _convertedTypes.IndexOf(name);
        if (index < 0)
        {
            index = _convertedTypes.Count;
            _convertedTypes.Add(name);
        }

        return HeldConversionKey + index;
    }

    // A copy that starts at the array and covers it is the array's copy; a group of writers has none, and its holders
    // then use the array itself, as they use the memory that the writer writes
    private static void ShareCopy<T>(HostAliasGroups groups, int member, ReadOnlySpan<nint> ranges,
        HostArrayCopies copies, in HeldEntry entry)
    {
        if (entry.Array is not { } held)
        {
            return;
        }

        if (!HostAliasCopies.TryGetHostCopy<T>(groups, member, ranges, out var copy, out var start))
        {
            copies.Keep(held);
        }
        else if (start == 0 && copy.Length == held.Length)
        {
            copies.Share(held, copy);
        }
    }

    // A view in a group of [In] arguments takes a view over the group's copy; a group with a writer keeps the original
    private static void ShareCopyView<T>(HostAliasGroups groups, int member, ReadOnlySpan<nint> ranges,
        HostArrayCopies copies, in HeldEntry entry)
    {
        if (!HostAliasCopies.TryGetHostMemory<T>(groups, member, ranges, out var memory))
        {
            copies.KeepView<T>(entry.Address, entry.Length);
        }
        else
        {
            copies.ShareView(entry.Address, entry.Length, memory);
        }
    }

    /// <summary>
    /// Pins the arrays added (views were pinned in <see cref="AddView{T}"/>), until <see cref="Free"/>, and reads
    /// their addresses and the traits and names that <see cref="Combine(byte[])"/> and <see cref="Combine(string)"/>
    /// append.
    /// </summary>
    public void Pin()
    {
        _heldRanges = new nint[_entries.Count * HostAliasGroups.RangeFields];
        _heldTraits = new byte[_entries.Count];
        var names = new System.Text.StringBuilder();
        for (var i = 0; i < _entries.Count; i++)
        {
            if (i > 0)
            {
                names.Append(',');
            }

            ref readonly var entry = ref CollectionsMarshal.AsSpan(_entries)[i];
            nint address;
            int whole;
            if (entry.Array is { } array)
            {
                names.Append("an array held by a struct argument");
                var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
                _pins.Add(handle);
                address = handle.AddrOfPinnedObject();
                whole = HostAliasGroups.WholeArray;
            }
            else
            {
                names.Append("a view held by a struct argument");
                address = entry.Address;
                whole = 0;
            }

            var at = i * HostAliasGroups.RangeFields;
            _heldRanges[at] = address;
            _heldRanges[at + 1] = entry.Length;
            _heldRanges[at + 2] = entry.HostSize;
            _heldRanges[at + 3] = entry.DeviceSize;
            _heldRanges[at + 4] = entry.Alignment;
            _heldRanges[at + 5] = entry.Conversion;
            var back = entry.CopiesBack ? HostAliasGroups.CopiesBack : 0;
            _heldTraits[i] = (byte)(HostAliasGroups.CopiesIn | back | whole);
        }

        _heldNames = names.ToString();
    }

    /// <summary>The range list of the arguments followed by one entry per held array or view.</summary>
    /// <param name="ranges">The range list of the arguments, with the arguments still pinned.</param>
    public ReadOnlySpan<nint> Combine(ReadOnlySpan<nint> ranges)
    {
        if (_heldRanges.Length == 0)
        {
            return ranges;
        }

        if (_combined is null || _combined.Length != ranges.Length + _heldRanges.Length)
        {
            _combined = new nint[ranges.Length + _heldRanges.Length];
        }

        ranges.CopyTo(_combined);
        _heldRanges.CopyTo(_combined, ranges.Length);
        return _combined;
    }

    /// <summary>
    /// The traits of the arguments followed by those of the held arrays and views (copied in, copied back when an
    /// argument that holds it does and the view is writable, whole for <c>T[]</c> arrays).
    /// </summary>
    /// <param name="traits">The traits of the arguments.</param>
    public byte[] Combine(byte[] traits)
    {
        if (_heldTraits.Length == 0)
        {
            return traits;
        }

        var combined = new byte[traits.Length + _heldTraits.Length];
        traits.CopyTo(combined, 0);
        _heldTraits.CopyTo(combined, traits.Length);
        return combined;
    }

    /// <summary>
    /// The names of the arguments followed by a name for each held array or view, for the exception messages.
    /// </summary>
    /// <param name="names">The names of the arguments, separated by commas (empty when there are none).</param>
    public string Combine(string names)
    {
        if (_heldNames.Length == 0)
        {
            return names;
        }

        return names.Length == 0 ? _heldNames : $"{names},{_heldNames}";
    }

    /// <summary>
    /// CUDA: records the region of each held array or view that is a member of a group in <paramref name="regions"/>,
    /// so that the <c>Marshal_</c> of its struct takes the group's region instead of allocating another one.
    /// </summary>
    /// <param name="groups">The groups of the launch, after <see cref="HostAliasGroups.CopyToDevice"/>.</param>
    /// <param name="first">The number of arguments, which is the member number of the first held entry.</param>
    /// <param name="regions">The launch's table of device regions.</param>
    public void AddRegions(HostAliasGroups groups, int first, DeviceArrayRegions? regions)
    {
        if (regions is null)
        {
            return;
        }

        for (var i = 0; i < _entries.Count; i++)
        {
            if (groups.IsMember(first + i))
            {
                var region = groups.GetDevicePointer(first + i);
                ref readonly var entry = ref CollectionsMarshal.AsSpan(_entries)[i];
                if (entry.Array is { } array)
                {
                    regions.AddShared(array, region);
                }
                else
                {
                    regions.AddSharedView(entry.Address, entry.Length, region);
                }
            }
        }
    }

    /// <summary>
    /// CPU: gives each held array or view that is a member of a group of [In] arguments the copy of its group in
    /// <paramref name="copies"/>, so that the copies of the struct arguments that hold it take it.
    /// </summary>
    /// <param name="groups">The groups of the launch.</param>
    /// <param name="first">The number of arguments, which is the member number of the first held entry.</param>
    /// <param name="ranges">The combined range list, pinned.</param>
    /// <param name="copies">The launch's table of copies.</param>
    public void ShareCopies(HostAliasGroups groups, int first, ReadOnlySpan<nint> ranges, HostArrayCopies copies)
    {
        // T[] arrays and typed-array views (ArraySegment, ImmutableArray) establish the group's CLR array copy before
        // Memory/ReadOnlyMemory views attach to it, matching AliasGroupEmitter.WriteCpuTypedCopiesFirst
        ShareCopiesPass(groups, first, ranges, copies, ShareEntryKind.WholeArray);
        ShareCopiesPass(groups, first, ranges, copies, ShareEntryKind.TypedArrayView);
        ShareCopiesPass(groups, first, ranges, copies, ShareEntryKind.MemoryView);
    }

    private void ShareCopiesPass(HostAliasGroups groups, int first, ReadOnlySpan<nint> ranges,
        HostArrayCopies copies, ShareEntryKind kind)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (!groups.IsMember(first + i))
            {
                continue;
            }

            ref readonly var entry = ref CollectionsMarshal.AsSpan(_entries)[i];
            var entryKind = entry.Array is not null
                ? ShareEntryKind.WholeArray
                : entry.RequiresTypedArray
                    ? ShareEntryKind.TypedArrayView
                    : ShareEntryKind.MemoryView;
            if (entryKind == kind)
            {
                entry.Sharer(groups, first + i, ranges, copies, in entry);
            }
        }
    }

    // A launch that failed before Free leaves its pins to the finalizer
    ~HostAliasHeldArrays() => Free();

    /// <summary>Unpins the arrays and views and forgets them.</summary>
    public void Free()
    {
        GC.SuppressFinalize(this);
        foreach (var pin in _pins)
        {
            pin.Free();
        }

        foreach (var pin in _viewPins)
        {
            pin.Dispose();
        }

        _pins.Clear();
        _viewPins.Clear();
        _entries.Clear();
        _mappedConversions?.Clear();
        _heldRanges = [];
        _heldTraits = [];
        _heldNames = "";
        _combined = null;
    }
}
