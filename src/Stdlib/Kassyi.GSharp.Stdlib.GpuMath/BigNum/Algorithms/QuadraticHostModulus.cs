namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// The host stage of <see cref="FermatAlgorithm"/>, <c>E &lt;= 31</c>: <c>N = 2^(2E+1) - 2^E + 11 &lt; 2^63</c>, residues
/// are canonical <see cref="ulong"/>s and a product is reduced with <see cref="UInt128"/>. No GPU memory is used.
/// </summary>
/// <remarks>The GPU fold needs <c>c = 2^E - 11 &gt; 0</c> (<c>E &gt;= 4</c>); the host stage covers <c>E &lt;= 3</c> as well.</remarks>
internal sealed class QuadraticHostModulus : ISquaringModulus
{
    /// <summary>The largest exponent of the host stage: <c>N &lt; 2^64</c>.</summary>
    internal const int MaxExponent = 31;

    private readonly ulong _modulus;
    private readonly ulong[] _registers = new ulong[Enum.GetValues<SquaringRegister>().Length];

    /// <summary>Initializes the modulus for <paramref name="exponent"/> (1 to <see cref="MaxExponent"/>).</summary>
    public QuadraticHostModulus(int exponent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(exponent, QuadraticFoldModulus.MinExponent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(exponent, MaxExponent);
        _modulus = (1UL << (2 * exponent + 1)) - (1UL << exponent) + QuadraticFoldModulus.ConstantTerm;
        Words = QuadraticFoldModulus.WordsOf(exponent);
    }

    /// <summary>Gets <c>N</c>.</summary>
    public ulong Modulus => _modulus;

    public int Words { get; }

    public BigInteger GetModulus() => _modulus;

    public bool IsBelowModulus(ReadOnlySpan<uint> words) => ToUInt64(words) < _modulus;

    public void Write(SquaringRegister register, ReadOnlySpan<uint> words) =>
        _registers[(int)register] = ToUInt64(words);

    public uint[] ReadCanonical(SquaringRegister register)
    {
        var value = _registers[(int)register];
        var words = new uint[Words];
        words[0] = (uint)value;
        if (Words > 1)
        {
            words[1] = (uint)(value >> 32);
        }

        return words;
    }

    public ValueTask<uint[]> ReadCanonicalAsync(SquaringRegister register, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? ValueTask.FromCanceled<uint[]>(cancellationToken)
            : ValueTask.FromResult(ReadCanonical(register));

    public void Copy(SquaringRegister destination, SquaringRegister source) =>
        _registers[(int)destination] = _registers[(int)source];

    public void SquareState(int count) => Square(SquaringRegister.State, count);

    public void Square(SquaringRegister register, int count)
    {
        ref var value = ref _registers[(int)register];
        for (var i = 0; i < count; i++)
        {
            value = MultiplyMod(value, value);
        }
    }

    public void Multiply(SquaringRegister target, SquaringRegister left, SquaringRegister right) =>
        _registers[(int)target] = MultiplyMod(_registers[(int)left], _registers[(int)right]);

    public void Synchronize()
    {
        // Everything runs synchronously on the host.
    }

    public ValueTask SynchronizeAsync(CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled(cancellationToken) : ValueTask.CompletedTask;

    public void Dispose()
    {
        // No unmanaged resources.
    }

    // Both factors are below N < 2^63, so the product fits UInt128.
    private ulong MultiplyMod(ulong left, ulong right) => (ulong)((UInt128)left * right % _modulus);

    private static ulong ToUInt64(ReadOnlySpan<uint> words) =>
        words.Length > 1 ? words[0] | ((ulong)words[1] << 32) : words[0];
}
