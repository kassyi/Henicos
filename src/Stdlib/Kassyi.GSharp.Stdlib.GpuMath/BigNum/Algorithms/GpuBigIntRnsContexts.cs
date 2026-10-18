namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Precomputed constants for RNS (Residue Number System) operations.
/// </summary>
public struct RnsConstantsContext
{
    /// <summary>Moduli values for the first RNS base.</summary>
    [GpuReadOnly] public uint[] ModuliB1;
    /// <summary>Moduli values for the second RNS base.</summary>
    [GpuReadOnly] public uint[] ModuliB2;
    /// <summary>Special sk modulus.</summary>
    public uint ModSk;
    /// <summary>Gamma values for base 1.</summary>
    [GpuReadOnly] public uint[] GammaB1;
    /// <summary>Gamma values for base 2.</summary>
    [GpuReadOnly] public uint[] GammaB2;
    /// <summary>Precomputed values of M1 / mi mod Msk.</summary>
    [GpuReadOnly] public uint[] M1OverMiModMsk;
    /// <summary>Precomputed inverse of M1 mod Msk.</summary>
    public uint M1InvModMsk;
    /// <summary>Precomputed values of M2 / mj mod Msk.</summary>
    [GpuReadOnly] public uint[] M2OverMjModMsk;
    /// <summary>Precomputed inverse of M2 mod Msk.</summary>
    public uint M2InvModMsk;
    /// <summary>Precomputed values of M1 / mi mod mj.</summary>
    [GpuReadOnly] public uint[] M1OverMiModMj;
    /// <summary>Precomputed values of M2 / mj mod mi.</summary>
    [GpuReadOnly] public uint[] M2OverMjModMi;
    /// <summary>Precomputed values of M1 mod mj.</summary>
    [GpuReadOnly] public uint[] M1ModMj;
    /// <summary>Precomputed values of M2 mod mi.</summary>
    [GpuReadOnly] public uint[] M2ModMi;
    /// <summary>Size of the RNS bases (k).</summary>
    public int KSize;
}

/// <summary>
/// Represents a multi-precision integer decomposed into Residue Number System spaces.
/// </summary>
public struct RnsBigInt
{
    /// <summary>Values in the first RNS base.</summary>
    public uint[] B1;
    /// <summary>Values in the second RNS base.</summary>
    public uint[] B2;
    /// <summary>Value modulo sk.</summary>
    public uint Sk;
}

/// <summary>
/// Encapsulates dynamic constants dependent on the modulus N for Montgomery reduction.
/// </summary>
public struct RnsMontgomeryContext
{
    /// <summary>Modulus N represented in the second RNS base.</summary>
    [GpuReadOnly] public uint[] NRnsB2;
    /// <summary>Modulus N modulo sk.</summary>
    public uint NSk;
    /// <summary>Precomputed -N^(-1) in the first RNS base.</summary>
    [GpuReadOnly] public uint[] NegNInvB1;
    /// <summary>Precomputed R^(-1) in the second RNS base.</summary>
    [GpuReadOnly] public uint[] RInvB2;
}
