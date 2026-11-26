namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>The CPU execution path that keeps a method on the state machine under a stack switch.</summary>
internal enum CpuPath
{
    Native,
    Managed
}
