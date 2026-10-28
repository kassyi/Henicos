namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>Whether only some threads reach a barrier, and whether a branch behind it certainly diverges.</summary>
/// <param name="Divergence">How the threads that skip the barrier go on.</param>
/// <param name="Certain">
/// Whether a branch that gives <paramref name="Divergence"/> certainly diverges: thread 0 and thread 1 of a block
/// (block 0 and block 1 of the grid for a grid barrier) take different sides of it. Otherwise whether it diverges depends on
/// the launch configuration or on run-time values.
/// </param>
internal readonly record struct BarrierReport(BarrierDivergence Divergence, bool Certain);
