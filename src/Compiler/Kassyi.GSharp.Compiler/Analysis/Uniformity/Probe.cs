namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>Which coordinate a probe run fixes.</summary>
internal enum Probe
{
    /// <summary><c>ThreadIndex.X</c> and <c>Gpu.LaneId()</c>.</summary>
    Block,

    /// <summary><c>BlockIndex.X</c>.</summary>
    Grid
}
