namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The parameters that receive a value that varies within a block, one that varies only between blocks, and the
/// thread index itself, one bit per parameter by ordinal (<see cref="Bit"/>).
/// </summary>
internal readonly record struct ArgumentMasks(ulong Block, ulong Grid, ulong Identity)
{
    /// <summary>The bit of a parameter; parameters past the 64th share the last bit.</summary>
    public static ulong Bit(int ordinal) => 1UL << Math.Min(ordinal, 63);
}
