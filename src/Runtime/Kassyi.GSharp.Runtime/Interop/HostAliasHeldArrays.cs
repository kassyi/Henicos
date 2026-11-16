using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// The arrays that the struct arguments of a launch hold in their fields, which the launcher compares by address
/// with its other arguments beside <see cref="HostAliasGroups"/>: a slice of an array that a struct argument holds is
/// one memory with that array, as in C#, but a <c>Span&lt;T&gt;</c> does not name its array, so the array must be
/// found by its address.
/// </summary>
/// <remarks>
/// The generated <c>AliasHeldArrays_</c> methods add the arrays of each struct argument (<see cref="Add{T}"/>),
/// <see cref="Pin"/> pins them for the launch, and every method that takes the range list takes the one of
/// <see cref="Combine(ReadOnlySpan{nint})"/>, which appends one entry per held array to the entries of the arguments. A held array that
/// lands in a group takes the group's region (<see cref="AddRegions"/>), so the <c>Marshal_</c> of its struct finds it
/// by identity as it finds the region of a <c>T[]</c> argument. The launcher calls <see cref="Free"/> in its
/// <c>finally</c> block.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class HostAliasHeldArrays
{
    // The first conversion number of a held array: the launcher numbers the converted element types of its own
    // arguments from 1, so a held array never shares a number with an argument, and a group that holds both is
    // reported as one of converted members of different element types
    private const int HeldConversionKey = 1000;

    private readonly List<Array> _arrays = [];
    private readonly List<(nint Length, nint HostSize, nint DeviceSize, nint Alignment, int Conversion)> _layouts = [];
    private readonly List<bool> _copiesBack = [];
    private readonly List<CopySharer> _sharers = [];
    private readonly List<GCHandle> _pins = [];
    private readonly List<string> _convertedTypes = [];
    private nint[] _heldRanges = [];
    private byte[] _heldTraits = [];
    private string _heldNames = "";
    private nint[]? _combined;

    // Gives the held array the copy of its group, typed by the array's element type
    private delegate void CopySharer(HostAliasGroups groups, int member, ReadOnlySpan<nint> ranges,
        HostArrayCopies copies, Array array);

    /// <summary>The number of arrays held.</summary>
    public int Count => _arrays.Count;

    /// <summary>
    /// Whether the argument whose arrays are being added copies them back to the caller. An array that two arguments
    /// hold is copied back when either does.
    /// </summary>
    public bool CopiesBack { get; set; }

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

        for (var i = 0; i < _arrays.Count; i++)
        {
            if (ReferenceEquals(_arrays[i], array))
            {
                _copiesBack[i] |= CopiesBack;
                return;
            }
        }

        var hostSize = Unsafe.SizeOf<T>();
        var key = 0;
        if (converted)
        {
            var name = typeof(T).FullName ?? typeof(T).Name;
            var index = _convertedTypes.IndexOf(name);
            if (index < 0)
            {
                index = _convertedTypes.Count;
                _convertedTypes.Add(name);
            }

            key = HeldConversionKey + index;
        }

        _arrays.Add(array);
        _copiesBack.Add(CopiesBack);
        _sharers.Add(static (groups, member, ranges, copies, held) =>
        {
            // A copy that starts at the array and covers it is the array's copy; a group of writers has none, and
            // its holders then use the array itself, as they use the memory that the writer writes
            if (!HostAliasCopies.TryGetHostCopy<T>(groups, member, ranges, out var copy, out var start))
            {
                copies.Keep(held);
            }
            else if (start == 0 && copy.Length == held.Length)
            {
                copies.Share(held, copy);
            }
        });
        _layouts.Add(((nint)array.Length * hostSize, hostSize, deviceSize == 0 ? hostSize : deviceSize,
            deviceAlignment, key));
    }

    /// <summary>
    /// Pins the arrays added, until <see cref="Free"/>, and reads their addresses and the traits and names that
    /// <see cref="Combine(byte[])"/> and <see cref="Combine(string)"/> append.
    /// </summary>
    public void Pin()
    {
        _heldRanges = new nint[_arrays.Count * HostAliasGroups.RangeFields];
        _heldTraits = new byte[_arrays.Count];
        var names = new System.Text.StringBuilder();
        for (var i = 0; i < _arrays.Count; i++)
        {
            names.Append(",an array held by a struct argument");
            var handle = GCHandle.Alloc(_arrays[i], GCHandleType.Pinned);
            _pins.Add(handle);
            var at = i * HostAliasGroups.RangeFields;
            var layout = _layouts[i];
            _heldRanges[at] = handle.AddrOfPinnedObject();
            _heldRanges[at + 1] = layout.Length;
            _heldRanges[at + 2] = layout.HostSize;
            _heldRanges[at + 3] = layout.DeviceSize;
            _heldRanges[at + 4] = layout.Alignment;
            _heldRanges[at + 5] = layout.Conversion;
            var back = _copiesBack[i] ? HostAliasGroups.CopiesBack : 0;
            _heldTraits[i] = (byte)(HostAliasGroups.CopiesIn | back | HostAliasGroups.WholeArray);
        }

        _heldNames = names.ToString();
    }

    /// <summary>The range list of the arguments followed by one entry per held array.</summary>
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
    /// The traits of the arguments followed by those of the held arrays (copied in, copied back when an argument that
    /// holds it does, whole).
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
    /// The names of the arguments followed by a name for each held array, for the exception messages.
    /// </summary>
    /// <param name="names">The names of the arguments, separated by commas.</param>
    public string Combine(string names) => names + _heldNames;

    /// <summary>
    /// CUDA: records the region of each held array that is a member of a group in <paramref name="regions"/>, so that
    /// the <c>Marshal_</c> of its struct takes the group's region instead of allocating another one.
    /// </summary>
    /// <param name="groups">The groups of the launch, after <see cref="HostAliasGroups.CopyToDevice"/>.</param>
    /// <param name="first">The number of arguments, which is the member number of the first held array.</param>
    /// <param name="regions">The launch's table of device regions.</param>
    public void AddRegions(HostAliasGroups groups, int first, DeviceArrayRegions? regions)
    {
        if (regions is null)
        {
            return;
        }

        for (var i = 0; i < _arrays.Count; i++)
        {
            if (groups.IsMember(first + i))
            {
                regions.AddShared(_arrays[i], groups.GetDevicePointer(first + i));
            }
        }
    }

    /// <summary>
    /// CPU: gives each held array that is a member of a group of [In] arguments the copy of its group in
    /// <paramref name="copies"/>, so that the copies of the struct arguments that hold the array take it.
    /// </summary>
    /// <param name="groups">The groups of the launch.</param>
    /// <param name="first">The number of arguments, which is the member number of the first held array.</param>
    /// <param name="ranges">The combined range list, pinned.</param>
    /// <param name="copies">The launch's table of copies.</param>
    public void ShareCopies(HostAliasGroups groups, int first, ReadOnlySpan<nint> ranges, HostArrayCopies copies)
    {
        for (var i = 0; i < _arrays.Count; i++)
        {
            if (groups.IsMember(first + i))
            {
                _sharers[i](groups, first + i, ranges, copies, _arrays[i]);
            }
        }
    }

    // A launch that failed before Free leaves its pins to the finalizer
    ~HostAliasHeldArrays() => Free();

    /// <summary>Unpins the arrays and forgets them.</summary>
    public void Free()
    {
        GC.SuppressFinalize(this);
        foreach (var pin in _pins)
        {
            pin.Free();
        }

        _pins.Clear();
        _arrays.Clear();
        _layouts.Clear();
        _copiesBack.Clear();
        _sharers.Clear();
        _heldRanges = [];
        _heldTraits = [];
        _heldNames = "";
        _combined = null;
    }
}
