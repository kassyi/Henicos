namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Non-owning view used by <see cref="SquaringChainDriver"/> to stop at scheduled Pietrzak leaf iterations and record
/// canonical chain states without taking ownership of the underlying leaf store.
/// </summary>
internal interface IChainLeafRecorder
{
    /// <summary>Returns the next leaf iteration strictly after <paramref name="iteration"/>, or <see cref="long.MaxValue"/>.</summary>
    /// <param name="iteration">The current chain iteration.</param>
    /// <returns>The next scheduled leaf iteration, or <see cref="long.MaxValue"/> when none remain.</returns>
    long NextStopAfter(long iteration);

    /// <summary>Records the canonical <see cref="SquaringRegister.State"/> when <paramref name="iteration"/> is a leaf.</summary>
    /// <param name="iteration">The current chain iteration.</param>
    /// <param name="modulus">The modulus holding the chain state.</param>
    void RecordIfLeaf(long iteration, ISquaringModulus modulus);
}

