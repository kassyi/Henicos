namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>Represents <c>curandRngType_t</c>, the algorithm used by a cuRAND generator.</summary>
public enum CuRandRngType
{
    PseudoDefault = 100,
    PseudoXorwow = 101,
    PseudoMrg32k3a = 121,
    PseudoMtgp32 = 141,
    PseudoMt19937 = 142,
    PseudoPhilox4x32x10 = 161,
    QuasiDefault = 200,
    QuasiSobol32 = 201,
    QuasiScrambledSobol32 = 202,
    QuasiSobol64 = 203,
    QuasiScrambledSobol64 = 204
}
