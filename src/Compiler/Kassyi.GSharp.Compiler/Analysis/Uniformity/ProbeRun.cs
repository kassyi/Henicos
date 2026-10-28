namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>One probe run: which coordinate it fixes, and to which value (0 or 1).</summary>
internal readonly record struct ProbeRun(Probe Probe, int Coordinate);
