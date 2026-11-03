#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// One named array of a differential case. An output lays its rows out as <c>r[row * Columns.Length + column]</c>, so a
/// cell names its row (the thread that wrote it) and its column.
/// </summary>
public abstract class DiffBuffer
{
    private protected DiffBuffer(
        string name,
        string[]? columns,
        OutputPolicy policy,
        bool isOutput,
        OutputPolicy[]? columnPolicies = null,
        bool isReadOnly = false,
        bool isNullArray = false
    )
    {
        if (columnPolicies is not null && (columns is null || columnPolicies.Length != columns.Length))
        {
            throw new ArgumentException("columnPolicies length must match columns length.", nameof(columnPolicies));
        }

        Name = name;
        Columns = columns;
        Policy = policy;
        ColumnPolicies = columnPolicies;
        IsOutput = isOutput;
        IsReadOnly = isReadOnly;
        IsNullArray = isNullArray;
    }

    /// <summary>Gets the name of the buffer, as the failure report prints it.</summary>
    public string Name { get; }

    /// <summary>Gets the column names of an output with several cells per row, or <see langword="null"/>.</summary>
    public string[]? Columns { get; }

    /// <summary>Gets how the cells are compared with the oracle.</summary>
    public OutputPolicy Policy { get; }

    /// <summary>Gets the per-column <see cref="OutputPolicy"/> array when columns have distinct policies, or <see langword="null"/>.</summary>
    public OutputPolicy[]? ColumnPolicies { get; }

    /// <summary>Gets a value indicating whether the kernel writes the buffer (inputs are compared too, in case it does).</summary>
    public bool IsOutput { get; }

    /// <summary>Gets a value indicating whether the input buffer is strictly read-only and must match its initial pre-launch contents.</summary>
    public bool IsReadOnly { get; }

    /// <summary>Gets a value indicating whether the buffer represents a top-level <see langword="null"/> array argument.</summary>
    public bool IsNullArray { get; }

    /// <summary>Gets the number of cells.</summary>
    public abstract int Length { get; }

    /// <summary>Gets the number of cells per row: the number of columns, or 1.</summary>
    public int ColumnCount => Columns?.Length ?? 1;

    internal abstract Array RawArray { get; }

    internal abstract bool CanPinAsBytes { get; }

    internal abstract ReadOnlySpan<byte> AsReadOnlyBytes();

    internal abstract Span<byte> AsWritableBytes();

    /// <summary>Returns the row of a cell.</summary>
    /// <param name="index">The cell.</param>
    public int RowOf(int index) => index / ColumnCount;

    /// <summary>Returns the column of a cell: its index in <see cref="Columns"/>, or 0.</summary>
    /// <param name="index">The cell.</param>
    public int ColumnOf(int index) => index % ColumnCount;

    /// <summary>
    /// Returns the column name of a cell, or the buffer name when the buffer has no columns. <see cref="KnownDivergence"/>
    /// entries use this name.
    /// </summary>
    /// <param name="index">The cell.</param>
    public string ColumnNameOf(int index) => Columns is null ? Name : Columns[ColumnOf(index)];

    /// <summary>Returns the <see cref="OutputPolicy"/> applicable to cell <paramref name="index"/>.</summary>
    /// <param name="index">The cell index.</param>
    public OutputPolicy PolicyOf(int index) =>
        ColumnPolicies is null ? Policy : ColumnPolicies[ColumnOf(index)];

    /// <summary>Returns the <see cref="CellLocation"/> of cell <paramref name="index"/>.</summary>
    /// <param name="index">The cell index.</param>
    public CellLocation LocationOf(int index) => new(Name, index, ColumnNameOf(index), ColumnCount);

    /// <summary>Returns a copy whose array is a new one with the same contents.</summary>
    public abstract DiffBuffer Clone();

    internal abstract DiffBuffer CloneWithTracking(Dictionary<object, object> objectMap);

    /// <summary>Returns whether a cell of this buffer matches the same cell of <paramref name="other"/> under <see cref="Policy"/>.</summary>
    /// <param name="other">A buffer of the same name, type and length.</param>
    /// <param name="index">The cell.</param>
    public abstract bool CellsMatch(DiffBuffer other, int index);

    /// <summary>Returns whether a cell has the same bits in both buffers (the oracle's order check).</summary>
    /// <param name="other">A buffer of the same name, type and length.</param>
    /// <param name="index">The cell.</param>
    public abstract bool SameBits(DiffBuffer other, int index);

    /// <summary>Formats a cell as its value and its bits: <c>6 (0x00000006)</c>.</summary>
    /// <param name="index">The cell.</param>
    public abstract string FormatCell(int index);

    /// <summary>Returns a copy of this buffer with each <see cref="OutputPolicyKind.Multiset"/> column sorted in total order.</summary>
    public abstract DiffBuffer CreateSortedCopy();
}

/// <summary>Settings shared across a <see cref="DiffBuffer{T}"/> and its clones.</summary>
internal readonly record struct BufferSpec<T>(
    string Name,
    string[]? Columns,
    OutputPolicy Policy,
    bool IsOutput,
    IElementComparer<T>? CustomComparer = null,
    OutputPolicy[]? ColumnPolicies = null,
    Func<T, DiffArrayCloneScope, T>? DeepCloner = null,
    bool IsReadOnly = false,
    bool IsNullArray = false,
    bool Pinned = false
);

/// <summary>A <see cref="DiffBuffer"/> of <typeparamref name="T"/> elements.</summary>
/// <typeparam name="T">The element type.</typeparam>
public sealed class DiffBuffer<T> : DiffBuffer
{
    private readonly BufferSpec<T> _spec;
    private T[] _data;

    internal DiffBuffer(BufferSpec<T> spec, T[] data)
        : base(spec.Name, spec.Columns, spec.Policy, spec.IsOutput, spec.ColumnPolicies, spec.IsReadOnly, spec.IsNullArray)
    {
        var resolvedComparer = spec.CustomComparer ?? (spec.Policy.CustomComparer as IElementComparer<T>);
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() && resolvedComparer is null && spec.Policy.Kind != OutputPolicyKind.Ignore)
        {
            throw new ArgumentException(
                $"Buffer '{spec.Name}' holds reference-containing type '{typeof(T).Name}' and requires a StructElementComparer<{typeof(T).Name}>.",
                nameof(spec));
        }

        _spec = spec with { CustomComparer = resolvedComparer };
        _data = data;
    }

    /// <summary>Gets the array that the launch and the oracle read and write.</summary>
    public T[] Data => _data;

    /// <summary>Gets a mutable reference to the backing array (used by <see cref="DiffBuffers.GetAutoResultRef{T}"/>).</summary>
    internal ref T[] DataRef => ref _data;

    /// <summary>Gets the backing array, or <see langword="null"/> when <see cref="DiffBuffer.IsNullArray"/> is <see langword="true"/>.</summary>
    public T[]? NullableData => IsNullArray ? null : _data;

    /// <summary>Gets a value indicating whether the underlying array is allocated on the pinned object heap.</summary>
    public bool IsPinned => _spec.Pinned;

    /// <inheritdoc/>
    public override int Length => _data.Length;

    internal override Array RawArray => _data;

    internal override bool CanPinAsBytes =>
        !IsNullArray && !RuntimeHelpers.IsReferenceOrContainsReferences<T>() && _data.Length > 0;

    internal override ReadOnlySpan<byte> AsReadOnlyBytes() =>
        _data.Length == 0
            ? ReadOnlySpan<byte>.Empty
            : MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<T, byte>(ref MemoryMarshal.GetArrayDataReference(_data)),
                checked(_data.Length * Unsafe.SizeOf<T>())
            );

    internal override Span<byte> AsWritableBytes() =>
        _data.Length == 0
            ? Span<byte>.Empty
            : MemoryMarshal.CreateSpan(
                ref Unsafe.As<T, byte>(ref MemoryMarshal.GetArrayDataReference(_data)),
                checked(_data.Length * Unsafe.SizeOf<T>())
            );

    /// <inheritdoc/>
    public override DiffBuffer Clone() =>
        CloneWithTracking(new Dictionary<object, object>(ReferenceEqualityComparer.Instance));

    internal override DiffBuffer CloneWithTracking(Dictionary<object, object> objectMap)
    {
        if (IsNullArray)
        {
            return new DiffBuffer<T>(_spec with { Pinned = false }, []);
        }

        T[] clonedData;
        if (objectMap.TryGetValue(_data, out var existing))
        {
            clonedData = (T[])existing;
        }
        else if (_spec.Pinned)
        {
            clonedData = GC.AllocateArray<T>(_data.Length, pinned: true);
            _data.AsSpan().CopyTo(clonedData);
            objectMap.Add(_data, clonedData);
        }
        else
        {
            clonedData = (T[])_data.Clone();
            objectMap.Add(_data, clonedData);
            if (_spec.DeepCloner is not null)
            {
                var scope = new DiffArrayCloneScope(objectMap);
                for (int i = 0; i < clonedData.Length; i++)
                {
                    clonedData[i] = _spec.DeepCloner(clonedData[i], scope);
                }
            }
        }

        return new DiffBuffer<T>(_spec, clonedData);
    }

    /// <inheritdoc/>
    public override bool CellsMatch(DiffBuffer other, int index)
    {
        var otherBuffer = Cast(other);
        if (IsNullArray || otherBuffer.IsNullArray)
        {
            return IsNullArray == otherBuffer.IsNullArray;
        }

        ref readonly var expected = ref _data[index];
        ref readonly var actual = ref otherBuffer._data[index];
        var policy = PolicyOf(index);

        if (policy.Kind == OutputPolicyKind.Ignore)
        {
            return true;
        }

        if (_spec.CustomComparer is not null)
        {
            return _spec.CustomComparer.Equals(in expected, in actual, policy);
        }

        return policy.Kind switch
        {
            OutputPolicyKind.Bitwise or OutputPolicyKind.Multiset => BitsEqual(in expected, in actual) || (IsNaN(in expected) && IsNaN(in actual)),
            OutputPolicyKind.Ulp => UlpEqualValue(in expected, in actual, policy.MaxUlp, policy.AllowInfinityBoundary),
            _ => BitsEqual(in expected, in actual),
        };
    }

    /// <inheritdoc/>
    public override DiffBuffer CreateSortedCopy()
    {
        if (IsNullArray || _data.Length == 0)
        {
            return Clone();
        }

        var sorted = (T[])_data.Clone();
        int cols = ColumnCount;
        if (cols <= 1)
        {
            if (Policy.Kind == OutputPolicyKind.Multiset)
            {
                SortSpanTotalOrder(sorted.AsSpan());
            }
        }
        else
        {
            int rows = sorted.Length / cols;
            var colScratch = new T[rows];
            for (int c = 0; c < cols; c++)
            {
                if (PolicyOf(c).Kind != OutputPolicyKind.Multiset)
                {
                    continue;
                }

                for (int r = 0; r < rows; r++)
                {
                    colScratch[r] = sorted[(r * cols) + c];
                }

                SortSpanTotalOrder(colScratch.AsSpan());
                for (int r = 0; r < rows; r++)
                {
                    sorted[(r * cols) + c] = colScratch[r];
                }
            }
        }

        return new DiffBuffer<T>(_spec with { Pinned = false }, sorted);
    }

    private static void SortSpanTotalOrder(Span<T> span)
    {
        if (typeof(T) == typeof(float))
        {
            var floatSpan = MemoryMarshal.CreateSpan(ref Unsafe.As<T, float>(ref MemoryMarshal.GetReference(span)), span.Length);
            floatSpan.Sort(static (a, b) => FloatTotalOrderKey(a).CompareTo(FloatTotalOrderKey(b)));
            return;
        }

        if (typeof(T) == typeof(double))
        {
            var doubleSpan = MemoryMarshal.CreateSpan(ref Unsafe.As<T, double>(ref MemoryMarshal.GetReference(span)), span.Length);
            doubleSpan.Sort(static (a, b) => DoubleTotalOrderKey(a).CompareTo(DoubleTotalOrderKey(b)));
            return;
        }

        if (typeof(T) == typeof(Half))
        {
            var halfSpan = MemoryMarshal.CreateSpan(ref Unsafe.As<T, Half>(ref MemoryMarshal.GetReference(span)), span.Length);
            halfSpan.Sort(static (a, b) => HalfTotalOrderKey(a).CompareTo(HalfTotalOrderKey(b)));
            return;
        }

        span.Sort();

        static ulong FloatTotalOrderKey(float f)
        {
            uint bits = BitConverter.SingleToUInt32Bits(f);
            if (float.IsNaN(f))
            {
                return (1UL << 32) | bits;
            }

            return (bits & 0x8000_0000u) != 0 ? ~bits : (bits | 0x8000_0000u);
        }

        static UInt128 DoubleTotalOrderKey(double d)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(d);
            if (double.IsNaN(d))
            {
                return (UInt128.One << 64) | bits;
            }

            return (bits & 0x8000_0000_0000_0000UL) != 0 ? ~bits : (bits | 0x8000_0000_0000_0000UL);
        }

        static uint HalfTotalOrderKey(Half h)
        {
            ushort bits = BitConverter.HalfToUInt16Bits(h);
            if (Half.IsNaN(h))
            {
                return (1u << 16) | bits;
            }

            return (bits & 0x8000u) != 0 ? (ushort)~bits : (uint)(bits | 0x8000u);
        }
    }

    /// <inheritdoc/>
    public override bool SameBits(DiffBuffer other, int index)
    {
        var otherBuffer = Cast(other);
        if (IsNullArray || otherBuffer.IsNullArray)
        {
            return IsNullArray == otherBuffer.IsNullArray;
        }

        if (_spec.CustomComparer is not null)
        {
            return _spec.CustomComparer.Equals(in _data[index], in otherBuffer._data[index], OutputPolicy.Exact);
        }

        return BitsEqual(in _data[index], in otherBuffer._data[index]);
    }

    /// <inheritdoc/>
    public override string FormatCell(int index)
    {
        if (IsNullArray)
        {
            return "null";
        }

        ref readonly var value = ref _data[index];
        if (_spec.CustomComparer is not null)
        {
            return _spec.CustomComparer.Format(in value);
        }

        return $"{string.Format(CultureInfo.InvariantCulture, "{0}", value)} ({Hex(in value)})";
    }

    private DiffBuffer<T> Cast(DiffBuffer other) =>
        other as DiffBuffer<T> ?? throw new ArgumentException($"buffer {other.Name} is not a {typeof(T).Name} buffer", nameof(other));

    private static bool BitsEqual(in T x, in T y)
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            throw new InvalidOperationException($"Raw byte comparison is invalid for reference-containing type '{typeof(T).Name}'.");
        }

        int size = Unsafe.SizeOf<T>();
        var xBytes = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in x)), size);
        var yBytes = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in y)), size);
        return xBytes.SequenceEqual(yBytes);
    }

    private static bool IsNaN(in T value) => value switch
    {
        float f => float.IsNaN(f),
        double d => double.IsNaN(d),
        Half h => Half.IsNaN(h),
        _ => false,
    };

    /// <summary>
    /// Compares <paramref name="x"/> and <paramref name="y"/> within <paramref name="maxUlp"/> using Sign-Magnitude to
    /// monotonic linear integer coordinates around <c>±0.0 = 0</c>, rejecting one-sided <c>NaN</c> and measuring across <c>±0</c>.
    /// </summary>
    internal static bool UlpEqualValue(in T x, in T y, uint maxUlp, bool allowInfinityBoundary)
    {
        if (typeof(T) == typeof(float))
        {
            float fx = Unsafe.As<T, float>(ref Unsafe.AsRef(in x));
            float fy = Unsafe.As<T, float>(ref Unsafe.AsRef(in y));
            if (float.IsNaN(fx) || float.IsNaN(fy))
            {
                return float.IsNaN(fx) && float.IsNaN(fy);
            }

            if (!allowInfinityBoundary && (float.IsInfinity(fx) || float.IsInfinity(fy)))
            {
                return fx == fy;
            }

            long lx = ToLinearIndex32(BitConverter.SingleToInt32Bits(fx));
            long ly = ToLinearIndex32(BitConverter.SingleToInt32Bits(fy));
            return (ulong)Math.Abs(lx - ly) <= maxUlp;
        }

        if (typeof(T) == typeof(double))
        {
            double dx = Unsafe.As<T, double>(ref Unsafe.AsRef(in x));
            double dy = Unsafe.As<T, double>(ref Unsafe.AsRef(in y));
            if (double.IsNaN(dx) || double.IsNaN(dy))
            {
                return double.IsNaN(dx) && double.IsNaN(dy);
            }

            if (!allowInfinityBoundary && (double.IsInfinity(dx) || double.IsInfinity(dy)))
            {
                return dx == dy;
            }

            Int128 lx = ToLinearIndex64(BitConverter.DoubleToInt64Bits(dx));
            Int128 ly = ToLinearIndex64(BitConverter.DoubleToInt64Bits(dy));
            return (UInt128)Int128.Abs(lx - ly) <= maxUlp;
        }

        if (typeof(T) == typeof(Half))
        {
            Half hx = Unsafe.As<T, Half>(ref Unsafe.AsRef(in x));
            Half hy = Unsafe.As<T, Half>(ref Unsafe.AsRef(in y));
            if (Half.IsNaN(hx) || Half.IsNaN(hy))
            {
                return Half.IsNaN(hx) && Half.IsNaN(hy);
            }

            if (!allowInfinityBoundary && (Half.IsInfinity(hx) || Half.IsInfinity(hy)))
            {
                return hx == hy;
            }

            short sx = BitConverter.HalfToInt16Bits(hx);
            short sy = BitConverter.HalfToInt16Bits(hy);
            int lx = sx < 0 ? -(sx & 0x7FFF) : sx;
            int ly = sy < 0 ? -(sy & 0x7FFF) : sy;
            return (uint)Math.Abs(lx - ly) <= maxUlp;
        }

        return BitsEqual(in x, in y);

        static long ToLinearIndex32(int bits) => bits < 0 ? -(long)(bits & 0x7FFF_FFFF) : bits;
        static Int128 ToLinearIndex64(long bits) => bits < 0 ? -(Int128)(bits & 0x7FFF_FFFF_FFFF_FFFFL) : bits;
    }

    // A primitive prints as the hex of its value (the bytes from the most significant); a struct prints its bytes in
    // memory order
    private static string Hex(in T value)
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            return "ref";
        }

        int size = Unsafe.SizeOf<T>();
        var bytes = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in value)), size);
        var text = new StringBuilder("0x", 2 + (bytes.Length * 2));
        bool primitive = typeof(T).IsPrimitive && BitConverter.IsLittleEndian;
        for (int i = 0; i < bytes.Length; i++)
        {
            text.Append(bytes[primitive ? bytes.Length - 1 - i : i].ToString("X2", CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }
}

/// <summary>
/// The named arrays of one differential run: inputs, which the case fills from the seed, and outputs, which start
/// zeroed. Every path gets its own <see cref="Clone"/> of the same initial buffers.
/// </summary>
public sealed class DiffBuffers
{
    /// <summary>The reserved buffer name used for non-void kernel return arrays.</summary>
    public const string AutoResultBufferName = "__result";

    private readonly List<DiffBuffer> _buffers = [];

    /// <summary>Gets the buffers in the order they were added.</summary>
    public IReadOnlyList<DiffBuffer> All => _buffers;

    /// <summary>Gets the active launch shape when running inside <see cref="DifferentialRunner"/>.</summary>
    public LaunchShape? Shape { get; internal set; }

    /// <summary>
    /// Adds an input. It is compared like an output (<see cref="OutputPolicy.Bitwise"/> unless given), so a kernel that
    /// writes its input is checked too. An input with one value per row is printed with each mismatching row.
    /// </summary>
    public DiffBuffers In<T>(
        string name,
        T[] values,
        OutputPolicy? policy = null,
        IElementComparer<T>? comparer = null,
        bool readOnly = false
    )
    {
        ArgumentNullException.ThrowIfNull(values);
        var spec = new BufferSpec<T>(name, null, policy ?? OutputPolicy.Bitwise, IsOutput: false, CustomComparer: comparer, IsReadOnly: readOnly);
        return Add(new DiffBuffer<T>(spec, values));
    }

    /// <summary>Adds an input with an explicit read-only immutability flag.</summary>
    public DiffBuffers In<T>(string name, T[] values, OutputPolicy? policy, bool readOnly) =>
        In(name, values, policy, comparer: null, readOnly: readOnly);

    /// <summary>Adds a GC-pinned input buffer for kernels that hold raw pointers across barriers.</summary>
    public DiffBuffers InPinned<T>(string name, ReadOnlySpan<T> initial, OutputPolicy? policy = null, bool readOnly = false)
        where T : unmanaged
    {
        var pinned = GC.AllocateArray<T>(initial.Length, pinned: true);
        initial.CopyTo(pinned);
        var spec = new BufferSpec<T>(name, null, policy ?? OutputPolicy.Exact, IsOutput: false, IsReadOnly: readOnly, Pinned: true);
        return Add(new DiffBuffer<T>(spec, pinned));
    }

    /// <summary>Adds a GC-pinned input buffer from an existing array, preserving aliasing on <see cref="Clone"/>.</summary>
    public DiffBuffers InPinned<T>(string name, T[] values, OutputPolicy? policy = null, bool readOnly = false)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(values);
        var spec = new BufferSpec<T>(name, null, policy ?? OutputPolicy.Exact, IsOutput: false, IsReadOnly: readOnly, Pinned: true);
        return Add(new DiffBuffer<T>(spec, values));
    }

    /// <summary>Adds a zeroed output of <paramref name="rows"/> rows of the named columns.</summary>
    public DiffBuffers Out<T>(string name, int rows, string[] columns, OutputPolicy policy, IElementComparer<T>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfZero(columns.Length);
        if (columns.Distinct(StringComparer.Ordinal).Count() != columns.Length)
        {
            throw new ArgumentException($"output {name} repeats a column name", nameof(columns));
        }

        var spec = new BufferSpec<T>(name, columns, policy, IsOutput: true, CustomComparer: comparer);
        return Add(new DiffBuffer<T>(spec, new T[rows * columns.Length]));
    }

    /// <summary>Adds a zeroed output of <paramref name="rows"/> rows with a distinct <see cref="OutputPolicy"/> per column.</summary>
    public DiffBuffers Out<T>(
        string name,
        int rows,
        (string Column, OutputPolicy Policy)[] columns,
        IElementComparer<T>? comparer = null
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentOutOfRangeException.ThrowIfZero(columns.Length);
        var names = new string[columns.Length];
        var policies = new OutputPolicy[columns.Length];
        for (int i = 0; i < columns.Length; i++)
        {
            names[i] = columns[i].Column;
            policies[i] = columns[i].Policy;
        }

        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
        {
            throw new ArgumentException($"output {name} repeats a column name", nameof(columns));
        }

        var spec = new BufferSpec<T>(name, names, policies[0], IsOutput: true, CustomComparer: comparer, ColumnPolicies: policies);
        return Add(new DiffBuffer<T>(spec, new T[rows * columns.Length]));
    }

    /// <summary>Adds a zeroed output of <paramref name="length"/> cells without column names.</summary>
    public DiffBuffers Out<T>(string name, int length, OutputPolicy policy, IElementComparer<T>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        var spec = new BufferSpec<T>(name, null, policy, IsOutput: true, CustomComparer: comparer);
        return Add(new DiffBuffer<T>(spec, new T[length]));
    }

    /// <summary>Adds a GC-pinned output buffer filled with <paramref name="fill"/>.</summary>
    public DiffBuffers OutPinned<T>(string name, int length, OutputPolicy? policy = null, T fill = default)
        where T : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        var pinned = GC.AllocateArray<T>(length, pinned: true);
        pinned.AsSpan().Fill(fill);
        var spec = new BufferSpec<T>(name, null, policy ?? OutputPolicy.Exact, IsOutput: true, Pinned: true);
        return Add(new DiffBuffer<T>(spec, pinned));
    }

    /// <summary>Adds a GC-pinned multi-column output buffer.</summary>
    public DiffBuffers OutPinned<T>(string name, int rows, string[] columns, OutputPolicy policy, T fill = default)
        where T : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfZero(columns.Length);
        if (columns.Distinct(StringComparer.Ordinal).Count() != columns.Length)
        {
            throw new ArgumentException($"output {name} repeats a column name", nameof(columns));
        }

        var pinned = GC.AllocateArray<T>(rows * columns.Length, pinned: true);
        pinned.AsSpan().Fill(fill);
        var spec = new BufferSpec<T>(name, columns, policy, IsOutput: true, Pinned: true);
        return Add(new DiffBuffer<T>(spec, pinned));
    }

    /// <summary>
    /// Registers an array of structs whose elements hold <c>TField[]</c> array fields, cloning nested arrays through
    /// <see cref="DiffArrayCloneScope"/> and comparing them via <paramref name="comparer"/>.
    /// </summary>
    public DiffBuffers InDeep<T>(
        string name,
        T[] values,
        Func<T, DiffArrayCloneScope, T> cloner,
        IElementComparer<T> comparer,
        OutputPolicy? policy = null,
        bool readOnly = false
    )
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(cloner);
        ArgumentNullException.ThrowIfNull(comparer);
        var spec = new BufferSpec<T>(name, null, policy ?? OutputPolicy.Exact, IsOutput: false, CustomComparer: comparer, DeepCloner: cloner, IsReadOnly: readOnly);
        return Add(new DiffBuffer<T>(spec, values));
    }

    /// <summary>
    /// Registers an array of structs whose elements hold <c>TField[]</c> array fields using a <see cref="StructFieldCheckContext"/> callback.
    /// </summary>
    public DiffBuffers InDeep<T>(
        string name,
        T[] values,
        Func<T, DiffArrayCloneScope, T> cloneElement,
        Action<StructFieldCheckContext, T, T> compareElement,
        bool readOnly = false,
        OutputPolicy? policy = null
    )
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(compareElement);
        return InDeep(name, values, cloneElement, new StructElementComparer<T>(compareElement), policy, readOnly);
    }

    /// <summary>Registers a by-value struct argument that holds one or more <c>TField[]</c> fields.</summary>
    public DiffBuffers InDeepScalar<T>(
        string name,
        T value,
        Func<T, DiffArrayCloneScope, T> cloner,
        IElementComparer<T> comparer,
        OutputPolicy? policy = null,
        bool readOnly = false
    )
        where T : struct =>
        InDeep(name, [value], cloner, comparer, policy ?? OutputPolicy.Exact, readOnly);

    /// <summary>Registers a by-value struct argument that holds one or more <c>TField[]</c> fields using a <see cref="StructFieldCheckContext"/> callback.</summary>
    public DiffBuffers InDeepScalar<T>(
        string name,
        T value,
        Func<T, DiffArrayCloneScope, T> clone,
        Action<StructFieldCheckContext, T, T>? compare = null,
        bool readOnly = false,
        OutputPolicy? policy = null
    )
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(clone);
        var comparer = compare is not null
            ? new StructElementComparer<T>(compare)
            : new StructElementComparer<T>(static (_, _, _) => true);
        var effectivePolicy = compare is not null ? (policy ?? OutputPolicy.Exact) : OutputPolicy.Ignore;
        return InDeep(name, [value], clone, comparer, effectivePolicy, readOnly);
    }

    /// <summary>Registers a <c>ref</c> struct argument that holds one or more <c>TField[]</c> fields.</summary>
    public DiffBuffers RefDeepScalar<T>(
        string name,
        T initialValue,
        Func<T, DiffArrayCloneScope, T> cloner,
        IElementComparer<T> comparer,
        OutputPolicy? policy = null
    )
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(cloner);
        ArgumentNullException.ThrowIfNull(comparer);
        var spec = new BufferSpec<T>(name, null, policy ?? OutputPolicy.Exact, IsOutput: true, CustomComparer: comparer, DeepCloner: cloner);
        return Add(new DiffBuffer<T>(spec, [initialValue]));
    }

    /// <summary>Registers a <c>ref</c> struct argument that holds one or more <c>TField[]</c> fields using a <see cref="StructFieldCheckContext"/> callback.</summary>
    public DiffBuffers RefDeepScalar<T>(
        string name,
        T initialValue,
        Func<T, DiffArrayCloneScope, T> clone,
        Action<StructFieldCheckContext, T, T> compare,
        OutputPolicy? policy = null
    )
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(compare);
        return RefDeepScalar(name, initialValue, clone, new StructElementComparer<T>(compare), policy);
    }

    /// <summary>Registers a top-level nullable array input (<c>T[]?</c>) that may be <see langword="null"/>.</summary>
    public DiffBuffers InNullable<T>(
        string name,
        T[]? values,
        OutputPolicy? policy = null,
        IElementComparer<T>? comparer = null,
        bool readOnly = false
    ) =>
        values is null
            ? Add(new DiffBuffer<T>(new BufferSpec<T>(name, null, OutputPolicy.Ignore, IsOutput: false, CustomComparer: comparer, IsReadOnly: readOnly, IsNullArray: true), []))
            : In(name, values, policy, comparer, readOnly);

    /// <summary>Registers a top-level nullable array input (<c>T[]?</c>) with a read-only flag.</summary>
    public DiffBuffers InNullable<T>(string name, T[]? values, bool readOnly) =>
        InNullable(name, values, policy: null, comparer: null, readOnly: readOnly);

    /// <summary>Returns the backing array of a nullable buffer, or <see langword="null"/> when registered as <see langword="null"/>.</summary>
    public T[]? GetNullable<T>(string name)
    {
        var buffer = Find(name);
        return buffer is DiffBuffer<T> typed
            ? typed.NullableData
            : throw new ArgumentException($"buffer {name} is not a {typeof(T).Name} buffer", nameof(name));
    }

    /// <summary>Registers a scalar input argument as a 1-element buffer.</summary>
    public DiffBuffers InScalar<T>(string name, T value, OutputPolicy? policy = null, IElementComparer<T>? comparer = null) =>
        In(name, [value], policy ?? OutputPolicy.Exact, comparer);

    /// <summary>Registers a scalar input argument with a custom element comparer.</summary>
    public DiffBuffers InScalar<T>(string name, T value, IElementComparer<T> comparer) =>
        In(name, [value], OutputPolicy.Exact, comparer);

    /// <summary>Registers an <c>out</c> scalar parameter as a 1-element output buffer.</summary>
    public DiffBuffers OutScalar<T>(string name, OutputPolicy? policy = null, IElementComparer<T>? comparer = null) =>
        Out(name, 1, policy ?? OutputPolicy.Exact, comparer);

    /// <summary>Registers a <c>ref</c> scalar parameter as a 1-element output buffer initialized with <paramref name="initialValue"/>.</summary>
    public DiffBuffers RefScalar<T>(string name, T initialValue, OutputPolicy? policy = null, IElementComparer<T>? comparer = null) =>
        Add(new DiffBuffer<T>(new BufferSpec<T>(name, null, policy ?? OutputPolicy.Exact, IsOutput: true, CustomComparer: comparer), [initialValue]));

    /// <summary>Registers the automatic return array (<c>__result</c>) for a value-returning kernel.</summary>
    public DiffBuffers OutAutoResult<T>(int length, OutputPolicy? policy = null, IElementComparer<T>? comparer = null) =>
        Out(AutoResultBufferName, length, policy ?? OutputPolicy.Exact, comparer);

    /// <summary>Gets the scalar value of the 1-element buffer named <paramref name="name"/>.</summary>
    public T GetScalar<T>(string name) => Get<T>(name)[0];

    /// <summary>Gets a <c>ref</c> to the scalar value of the 1-element buffer named <paramref name="name"/>.</summary>
    public ref T GetScalarRef<T>(string name) => ref Get<T>(name)[0];

    /// <summary>Sets the scalar value of the 1-element buffer named <paramref name="name"/>.</summary>
    public void SetScalar<T>(string name, T value) => Get<T>(name)[0] = value;

    /// <summary>Gets the backing array for the automatic return buffer (<c>__result</c>).</summary>
    public T[] GetAutoResult<T>() => Get<T>(AutoResultBufferName);

    /// <summary>Gets a mutable reference to the automatic return array (<c>__result</c>).</summary>
    public ref T[] GetAutoResultRef<T>()
    {
        var buffer = Find(AutoResultBufferName);
        if (buffer is DiffBuffer<T> typed)
        {
            return ref typed.DataRef;
        }

        throw new ArgumentException($"buffer {AutoResultBufferName} is not a {typeof(T).Name} buffer");
    }

    /// <summary>Copies or assigns the returned array of a value-returning kernel into the <c>__result</c> buffer.</summary>
    public void SetAutoResult<T>(ReadOnlySpan<T> values)
    {
        var dst = Get<T>(AutoResultBufferName);
        int count = Math.Min(values.Length, dst.Length);
        values[..count].CopyTo(dst);
    }

    /// <summary>Copies or assigns the returned array of a value-returning kernel into the <c>__result</c> buffer.</summary>
    public void SetAutoResult<T>(T[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        SetAutoResult<T>(values.AsSpan());
    }

    /// <summary>Returns the array of the named buffer.</summary>
    /// <typeparam name="T">The element type it was added with.</typeparam>
    /// <param name="name">The name.</param>
    public T[] Get<T>(string name)
    {
        var buffer = Find(name);
        if (buffer.IsNullArray)
        {
            throw new InvalidOperationException($"Buffer '{name}' is null; use GetNullable<{typeof(T).Name}>().");
        }

        return buffer is DiffBuffer<T> typed
            ? typed.Data
            : throw new ArgumentException($"buffer {name} is not a {typeof(T).Name} buffer", nameof(name));
    }

    /// <summary>Returns the named buffer.</summary>
    /// <param name="name">The name.</param>
    public DiffBuffer Find(string name)
    {
        foreach (var buffer in _buffers)
        {
            if (buffer.Name == name)
            {
                return buffer;
            }
        }

        throw new ArgumentException($"no buffer named {name}", nameof(name));
    }

    /// <summary>Returns a copy whose arrays are new ones with the same contents, preserving aliasing between buffers and deep structs.</summary>
    public DiffBuffers Clone()
    {
        var copy = new DiffBuffers { Shape = Shape };
        var objectMap = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        foreach (var buffer in _buffers)
        {
            copy._buffers.Add(buffer.CloneWithTracking(objectMap));
        }

        return copy;
    }

    /// <summary>
    /// Allocates and uploads device buffers (on <c>GSHARP_CUDA</c>) or pins host buffers (on CPU) for <c>Launch{Kernel}Ptr</c>
    /// execution, copying modified buffers back to host arrays on disposal.
    /// </summary>
    public DiffPointerScope PinOrUpload() => new(this);

    private DiffBuffers Add(DiffBuffer buffer)
    {
        ArgumentException.ThrowIfNullOrEmpty(buffer.Name);
        if (_buffers.Any(b => b.Name == buffer.Name))
        {
            throw new ArgumentException($"a buffer named {buffer.Name} already exists", nameof(buffer));
        }

        _buffers.Add(buffer);
        return this;
    }
}
