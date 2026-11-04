namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Backing storage for the <c>2 * M_p</c> recorded chain leaves (slots <c>0 .. 2*M_p - 1</c>) and <c>M_p</c> tree-folding
/// workspace nodes (slots <c>2*M_p .. 3*M_p - 1</c>). Keeping the workspace separate leaves recorded leaves untouched across
/// repeated runs or checkpoint restores.
/// </summary>
internal interface IPietrzakLeafStore : IDisposable
{
    /// <summary>Gets the number of recorded leaf slots (<c>2 * M_p</c>).</summary>
    int TotalLeafSlots { get; }

    /// <summary>Gets the canonical word count per residue.</summary>
    int ResidueWords { get; }

    /// <summary>Returns whether <paramref name="leafSlot"/> has been recorded.</summary>
    /// <param name="leafSlot">The zero-based leaf slot index in <c>[0, TotalLeafSlots)</c>.</param>
    /// <returns><see langword="true"/> when the leaf slot holds a recorded residue; otherwise <see langword="false"/>.</returns>
    bool HasLeafSlot(int leafSlot);

    /// <summary>Clears the recorded-leaf bitmap before starting a fresh run from iteration 0.</summary>
    void ResetLeaves();

    /// <summary>Writes a canonical residue into <paramref name="slot"/>.</summary>
    /// <param name="slot">The zero-based storage slot index.</param>
    /// <param name="words">The canonical residue words to store.</param>
    void WriteSlot(int slot, ReadOnlySpan<uint> words);

    /// <summary>Reads a canonical residue from <paramref name="slot"/> into <paramref name="destination"/>.</summary>
    /// <param name="slot">The zero-based storage slot index.</param>
    /// <param name="destination">The buffer receiving the canonical residue words.</param>
    void ReadSlot(int slot, Span<uint> destination);

    /// <summary>Flushes buffered leaf writes to backing storage.</summary>
    void Flush();
}
