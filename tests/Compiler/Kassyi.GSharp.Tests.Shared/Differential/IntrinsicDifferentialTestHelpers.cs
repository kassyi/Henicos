#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// The launch, the boundary tables and the input builders shared by <see cref="IntrinsicDifferentialTests"/> and
/// <see cref="IntrinsicBitDifferentialTests"/>.
/// </summary>
internal static class IntrinsicDifferentialTestHelpers
{
    // Two blocks of 32, the second one partial
    internal const int Rows = 70;

    // Zero, one, the sign bit alone and around it, all ones, and a value of each half, so that a pair of them reaches
    // a signed difference above int.MaxValue (int.MinValue - int.MaxValue), a product with a negative operand and a
    // leading zero count of 0, 1, 31 and 32
    internal static readonly uint[] s_uint32Boundaries =
    [
        0u, 1u, 2u, 0x7FFF_FFFFu, 0x8000_0000u, 0xFFFF_FFFFu, 0xFFFF_FFFEu, 0x0001_0000u, 0x8000_0001u, 0x4000_0000u,
        3u,
    ];

    internal static readonly ulong[] s_uint64Boundaries =
    [
        0ul, 1ul, ulong.MaxValue, 0x8000_0000_0000_0000ul, 0x7FFF_FFFF_FFFF_FFFFul, 0xFFFF_FFFFul, 0x1_0000_0000ul,
        0x8000_0000_0000_0001ul, 2ul, 0xFFFF_FFFF_FFFF_FFFEul,
    ];

    internal static string KernelName(string kernel) =>
        DiffKernelNames.Of(typeof(IntrinsicDifferentialKernels), kernel);

    // The boundary values from offset on, cycled, for seed 0 (each table has its own length, so the operands of a row
    // meet in many combinations); random values for any other seed
    internal static T[] Values<T>(DiffRandom rng, int rows, T[] boundaries, int offset) where T : unmanaged
    {
        var values = new T[rows];
        for (int k = 0; k < rows; k++)
        {
            values[k] = rng.IsBoundarySeed ? boundaries[(k + offset) % boundaries.Length] : Random<T>(rng);
        }

        return values;
    }

    private static T Random<T>(DiffRandom rng) where T : unmanaged
    {
        ulong bits = rng.NextUInt64();
        return typeof(T) == typeof(uint) ? (T)(object)(uint)bits : (T)(object)bits;
    }

    internal static T[] Cycled<T>(int rows, T[] table)
    {
        var values = new T[rows];
        for (int k = 0; k < rows; k++)
        {
            values[k] = table[k % table.Length];
        }

        return values;
    }
}
