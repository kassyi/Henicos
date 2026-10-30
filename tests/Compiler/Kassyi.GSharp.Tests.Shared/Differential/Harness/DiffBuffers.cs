#nullable enable
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// One named array of a differential case. An output lays its rows out as <c>r[row * Columns.Length + column]</c>, so a
/// cell names its row (the thread that wrote it) and its column.
/// </summary>
public abstract class DiffBuffer
{
    private protected DiffBuffer(string name, string[]? columns, OutputPolicy policy, bool isOutput)
    {
        Name = name;
        Columns = columns;
        Policy = policy;
        IsOutput = isOutput;
    }

    /// <summary>Gets the name of the buffer, as the failure report prints it.</summary>
    public string Name { get; }

    /// <summary>Gets the column names of an output with several cells per row, or <see langword="null"/>.</summary>
    public string[]? Columns { get; }

    /// <summary>Gets how the cells are compared with the oracle.</summary>
    public OutputPolicy Policy { get; }

    /// <summary>Gets a value indicating whether the kernel writes the buffer (inputs are compared too, in case it does).</summary>
    public bool IsOutput { get; }

    /// <summary>Gets the number of cells.</summary>
    public abstract int Length { get; }

    /// <summary>Gets the number of cells per row: the number of columns, or 1.</summary>
    public int ColumnCount => Columns?.Length ?? 1;

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

    /// <summary>Returns a copy whose array is a new one with the same contents.</summary>
    public abstract DiffBuffer Clone();

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
}

/// <summary>A <see cref="DiffBuffer"/> of <typeparamref name="T"/> elements.</summary>
/// <typeparam name="T">The element type.</typeparam>
public sealed class DiffBuffer<T> : DiffBuffer
    where T : unmanaged
{
    internal DiffBuffer(string name, T[] data, string[]? columns, OutputPolicy policy, bool isOutput)
        : base(name, columns, policy, isOutput) => Data = data;

    /// <summary>Gets the array that the launch and the oracle read and write.</summary>
    public T[] Data { get; }

    /// <inheritdoc/>
    public override int Length => Data.Length;

    /// <inheritdoc/>
    public override DiffBuffer Clone() => new DiffBuffer<T>(Name, (T[])Data.Clone(), Columns, Policy, IsOutput);

    /// <inheritdoc/>
    public override bool CellsMatch(DiffBuffer other, int index)
    {
        var otherData = Cast(other).Data;
        return Policy.Kind switch
        {
            OutputPolicyKind.Ignore => true,
            OutputPolicyKind.Bitwise => BitsEqual(Data[index], otherData[index]) || (IsNaN(Data[index]) && IsNaN(otherData[index])),
            _ => BitsEqual(Data[index], otherData[index]),
        };
    }

    /// <inheritdoc/>
    public override bool SameBits(DiffBuffer other, int index) => BitsEqual(Data[index], Cast(other).Data[index]);

    /// <inheritdoc/>
    public override string FormatCell(int index)
    {
        var value = Data[index];
        return $"{string.Format(CultureInfo.InvariantCulture, "{0}", value)} ({Hex(value)})";
    }

    private DiffBuffer<T> Cast(DiffBuffer other) =>
        other as DiffBuffer<T> ?? throw new ArgumentException($"buffer {other.Name} is not a {typeof(T).Name} buffer", nameof(other));

    private static bool BitsEqual(T x, T y) =>
        MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in x)).SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in y)));

    private static bool IsNaN(T value) => value switch
    {
        float f => float.IsNaN(f),
        double d => double.IsNaN(d),
        Half h => Half.IsNaN(h),
        _ => false,
    };

    // A primitive prints as the hex of its value (the bytes from the most significant); a struct prints its bytes in
    // memory order
    private static string Hex(T value)
    {
        var bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value));
        var text = new StringBuilder("0x", 2 + bytes.Length * 2);
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
    private readonly List<DiffBuffer> _buffers = [];

    /// <summary>Gets the buffers in the order they were added.</summary>
    public IReadOnlyList<DiffBuffer> All => _buffers;

    /// <summary>
    /// Adds an input. It is compared like an output (<see cref="OutputPolicy.Bitwise"/> unless given), so a kernel that
    /// writes its input is checked too. An input with one value per row is printed with each mismatching row.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="name">The name.</param>
    /// <param name="values">The values; the buffer keeps this array.</param>
    /// <param name="policy">The comparison policy.</param>
    public DiffBuffers In<T>(string name, T[] values, OutputPolicy? policy = null)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(values);
        return Add(new DiffBuffer<T>(name, values, null, policy ?? OutputPolicy.Bitwise, isOutput: false));
    }

    /// <summary>Adds a zeroed output of <paramref name="rows"/> rows of the named columns.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="name">The name.</param>
    /// <param name="rows">The number of rows.</param>
    /// <param name="columns">The column names; the kernel writes cell <c>row * columns.Length + column</c>.</param>
    /// <param name="policy">The comparison policy.</param>
    public DiffBuffers Out<T>(string name, int rows, string[] columns, OutputPolicy policy)
        where T : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfZero(columns.Length);
        if (columns.Distinct(StringComparer.Ordinal).Count() != columns.Length)
        {
            throw new ArgumentException($"output {name} repeats a column name", nameof(columns));
        }

        return Add(new DiffBuffer<T>(name, new T[rows * columns.Length], columns, policy, isOutput: true));
    }

    /// <summary>Adds a zeroed output of <paramref name="length"/> cells without column names.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="name">The name; it is also the column name of every cell.</param>
    /// <param name="length">The number of cells.</param>
    /// <param name="policy">The comparison policy.</param>
    public DiffBuffers Out<T>(string name, int length, OutputPolicy policy)
        where T : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        return Add(new DiffBuffer<T>(name, new T[length], null, policy, isOutput: true));
    }

    /// <summary>Returns the array of the named buffer.</summary>
    /// <typeparam name="T">The element type it was added with.</typeparam>
    /// <param name="name">The name.</param>
    public T[] Get<T>(string name)
        where T : unmanaged
    {
        var buffer = Find(name);
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

    /// <summary>Returns a copy whose arrays are new ones with the same contents.</summary>
    public DiffBuffers Clone()
    {
        var copy = new DiffBuffers();
        foreach (var buffer in _buffers)
        {
            copy._buffers.Add(buffer.Clone());
        }

        return copy;
    }

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
