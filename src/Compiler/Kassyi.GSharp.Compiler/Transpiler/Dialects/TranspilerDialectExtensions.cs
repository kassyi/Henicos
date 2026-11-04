namespace Kassyi.GSharp.Compiler.Transpiler.Dialects;

internal static class TranspilerDialectExtensions
{
    /// <summary>
    /// Whether the dialect runs the threads of a block one after another, so that kernels and device functions that reach a
    /// barrier are lowered into state machines.
    /// </summary>
    public static bool EmulatesThreadsWithStateMachine(this ITranspilerDialect dialect) =>
        (dialect.Capabilities & DialectCapabilities.HardwareThreadScheduling) == 0;
}
