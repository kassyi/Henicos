namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>The residues a squaring chain keeps, each in its own register of an <see cref="ISquaringModulus"/>.</summary>
internal enum SquaringRegister
{
    /// <summary>The chain value <c>u_k</c>.</summary>
    State,

    /// <summary>The start of the chain <c>u_0</c> (the base), which the Gerbicz-Li check multiplies in.</summary>
    Start,

    /// <summary>The Gerbicz-Li product <c>d</c> of the chain values at the block boundaries.</summary>
    CheckD,

    /// <summary>The Gerbicz-Li check value <c>d^(2^B) * u_0</c>.</summary>
    CheckAux,

    /// <summary>The chain value saved at the snapshot point (<c>u_E</c> for <see cref="FermatAlgorithm"/>).</summary>
    Snapshot,

    /// <summary>The chain value at the end of the chain (<c>u_K</c>), saved before the chain runs on to the last check.</summary>
    Final,

    /// <summary>A constant of the verdict (<c>a^10 mod N</c> for <see cref="FermatAlgorithm"/>).</summary>
    Constant,

    /// <summary>The value the verdict compares (<c>u_K * a^10</c>), so that <see cref="Final"/> keeps <c>u_K</c>.</summary>
    Verdict
}
