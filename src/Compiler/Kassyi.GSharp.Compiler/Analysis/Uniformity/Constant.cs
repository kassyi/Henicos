namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>A value of the constant propagation: not reached yet (Bottom), one constant, or unknown.</summary>
internal readonly struct Constant : IEquatable<Constant>
{
    private readonly byte _kind;

    private Constant(byte kind, long value)
    {
        _kind = kind;
        Value = value;
    }

    public static Constant Unknown => new(2, 0);

    public long Value { get; }

    public bool IsValue => _kind == 1;

    public static Constant Of(long value) => new(1, value);

    public static Constant FromObject(object? value) => value switch
    {
        bool flag => Of(flag ? 1 : 0),
        int number => Of(number),
        uint number => Of(number),
        long number => Of(number),
        short number => Of(number),
        ushort number => Of(number),
        byte number => Of(number),
        sbyte number => Of(number),
        char character => Of(character),
        _ => Unknown
    };

    // A read of a value that no reached path assigned is not a constant
    public Constant OrUnknown() => _kind == 0 ? Unknown : this;

    public Constant Join(Constant other) =>
        _kind == 0 ? other :
        other._kind == 0 ? this :
        _kind == 1 && other._kind == 1 && Value == other.Value ? this : Unknown;

    public bool Equals(Constant other) => _kind == other._kind && Value == other.Value;

    public override bool Equals(object? obj) => obj is Constant other && Equals(other);

    public override int GetHashCode() => (_kind * 397) ^ Value.GetHashCode();
}
