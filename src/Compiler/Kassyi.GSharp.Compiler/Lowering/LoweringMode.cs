namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Selects what splits the state machine during AST lowering.</summary>
public enum LoweringMode
{
    /// <summary>Splits on thread barriers, warp shuffles, and calls to suspending device functions.</summary>
    Suspension,

    /// <summary>
    /// Splits on <c>yield return</c> and <c>yield break</c> statements in an iterator device function.
    /// </summary>
    Iterator
}
