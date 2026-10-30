namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>How far a value, or the choice of a branch, may differ between the threads that run it.</summary>
[Flags]
internal enum ThreadVariance : byte
{
    /// <summary>The same in every thread of the grid.</summary>
    Uniform = 0,

    /// <summary>The same in every thread of a block, but may differ between the blocks (<c>BlockIndex</c>).</summary>
    Grid = 1,

    /// <summary>May differ between the threads of a block, and so between the threads of the grid.</summary>
    Block = Grid | 2
}
