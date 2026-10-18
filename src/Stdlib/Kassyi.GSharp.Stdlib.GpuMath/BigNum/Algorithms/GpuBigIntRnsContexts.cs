namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Precomputed constants for a single RNS base and its base-extension coefficients.
/// </summary>
public struct RnsBaseConstantsContext
{
    /// <summary>Moduli values for this RNS base.</summary>
    [GpuReadOnly] public uint[] Moduli;
    /// <summary>Gamma values for this RNS base.</summary>
    [GpuReadOnly] public uint[] Gamma;
    /// <summary>Precomputed values of M / m_i mod Msk.</summary>
    [GpuReadOnly] public uint[] MOverMiModMsk;
    /// <summary>Precomputed inverse of M mod Msk.</summary>
    public uint MInvModMsk;
    /// <summary>Precomputed values of M / m_i mod m_other.</summary>
    [GpuReadOnly] public uint[] MOverMiModOther;
    /// <summary>Precomputed values of M mod m_other.</summary>
    [GpuReadOnly] public uint[] MModOther;
}

/// <summary>
/// Precomputed constants for RNS (Residue Number System) operations across dual bases and the Shenoy-Kumaresan modulus.
/// </summary>
public struct RnsConstantsContext
{
    /// <summary>Precomputed constants for the first RNS base (B1) and B1 -> B2 extension.</summary>
    public RnsBaseConstantsContext Base1;
    /// <summary>Precomputed constants for the second RNS base (B2) and B2 -> B1 extension.</summary>
    public RnsBaseConstantsContext Base2;
    /// <summary>Special Shenoy-Kumaresan modulus (m_sk).</summary>
    public uint ModSk;
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
