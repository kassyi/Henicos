namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Residues modulo a fixed <c>N</c> in registers, and the operations a squaring chain needs on them. A register holds a
/// value congruent to its residue, not necessarily below <c>N</c> (redundant form); reads return the canonical residue.
/// </summary>
/// <remarks>
/// Operations are queued in order on the implementation's stream; reads wait for the queued work.
/// <see cref="FermatAlgorithm"/> uses <see cref="QuadraticHostModulus"/> and <see cref="QuadraticDeviceModulus"/>; a
/// Pepin test of Fermat numbers would add its own modulus and reuse <see cref="SquaringChainDriver"/>.
/// </remarks>
internal interface ISquaringModulus : IDisposable
{
    /// <summary>Gets the word count of a canonical residue.</summary>
    int Words { get; }

    /// <summary>Returns the modulus <c>N</c>.</summary>
    /// <returns><c>N</c>.</returns>
    BigInteger GetModulus();

    /// <summary>Returns whether a value is below <c>N</c>.</summary>
    /// <param name="words">The value, <see cref="Words"/> words.</param>
    /// <returns><see langword="true"/> when the value is a canonical residue.</returns>
    bool IsBelowModulus(ReadOnlySpan<uint> words);

    /// <summary>Writes a residue to a register.</summary>
    /// <param name="register">The register.</param>
    /// <param name="words">A residue below <c>N</c>, <see cref="Words"/> words.</param>
    void Write(SquaringRegister register, ReadOnlySpan<uint> words);

    /// <summary>Reads the canonical residue of a register, after the queued work.</summary>
    /// <param name="register">The register.</param>
    /// <returns>The residue below <c>N</c>, <see cref="Words"/> words.</returns>
    uint[] ReadCanonical(SquaringRegister register);

    /// <summary>Reads the canonical residue of a register without blocking while the queued work completes.</summary>
    /// <param name="register">The register.</param>
    /// <param name="cancellationToken">A token that stops waiting.</param>
    /// <returns>The residue below <c>N</c>, <see cref="Words"/> words.</returns>
    ValueTask<uint[]> ReadCanonicalAsync(SquaringRegister register, CancellationToken cancellationToken);

    /// <summary>Queues a copy of one register to another.</summary>
    /// <param name="destination">The register to write.</param>
    /// <param name="source">The register to read.</param>
    void Copy(SquaringRegister destination, SquaringRegister source);

    /// <summary>Queues squarings of <see cref="SquaringRegister.State"/>, the hot loop.</summary>
    /// <param name="count">The number of squarings; none when it is zero or less.</param>
    void SquareState(int count);

    /// <summary>Queues squarings of another register.</summary>
    /// <param name="register">The register.</param>
    /// <param name="count">The number of squarings.</param>
    void Square(SquaringRegister register, int count);

    /// <summary>Queues <c>target = left * right mod N</c>.</summary>
    /// <param name="target">The register to write; it may be one of the factors.</param>
    /// <param name="left">The first factor.</param>
    /// <param name="right">The second factor (the same register for a square).</param>
    void Multiply(SquaringRegister target, SquaringRegister left, SquaringRegister right);

    /// <summary>Waits for the queued work.</summary>
    void Synchronize();

    /// <summary>Waits for the queued work without blocking the calling thread.</summary>
    /// <param name="cancellationToken">A token that stops waiting.</param>
    /// <returns>A task that completes with the queued work.</returns>
    ValueTask SynchronizeAsync(CancellationToken cancellationToken);
}
