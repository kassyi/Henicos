namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The capacity of a vector expression and its number of elements when that is known on the host, read once when the
/// node over it is built: both are fixed by then, and a node that asked its input every time would recurse through
/// every level of an expression built in a loop.
/// </summary>
internal readonly struct VectorShape
{
    private readonly int _length;
    private readonly bool _lengthKnown;

    private VectorShape(int capacity, bool lengthKnown, int length)
    {
        Capacity = capacity;
        _lengthKnown = lengthKnown;
        _length = lengthKnown ? length : 0;
    }

    /// <summary>Gets the upper bound of the number of elements.</summary>
    public int Capacity { get; }

    /// <summary>
    /// Gets the number of elements, or throws when it is known only on the device (as
    /// <see cref="DeviceCounted.KnownLength(IGpuBigIntVectorExpression)"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">The number is known only on the device.</exception>
    public int Length => _lengthKnown ? _length : throw new InvalidOperationException(DeviceCounted.LengthUnknown);

    /// <summary>The shape of an integer vector.</summary>
    public static VectorShape Of(IGpuBigIntVectorExpression vector) =>
        new(vector.Capacity, vector.TryGetLength(out var length), length);

    /// <summary>The shape of a floating-point vector.</summary>
    public static VectorShape Of(IGpuBigFloatVectorExpression vector) =>
        new(vector.Capacity, vector.TryGetLength(out var length), length);

    /// <summary>The shape of a flag vector.</summary>
    public static VectorShape Of(IGpuBoolVectorExpression vector) =>
        new(vector.Capacity, vector.TryGetLength(out var length), length);

    /// <summary>The shape after a <c>Where</c>: the same capacity; the number is known only on the device.</summary>
    public VectorShape Filtered() => new(Capacity, false, 0);

    /// <summary>Gets the number of elements when it is known on the host.</summary>
    /// <param name="length">The number of elements, or 0.</param>
    /// <returns><see langword="true"/> when the number is known on the host.</returns>
    public bool TryGetLength(out int length)
    {
        length = _length;
        return _lengthKnown;
    }
}
