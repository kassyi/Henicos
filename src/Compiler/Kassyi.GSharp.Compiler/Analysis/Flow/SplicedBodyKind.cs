namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>Why a body is spliced into a <see cref="SplicedFlowGraph"/> where it is.</summary>
internal enum SplicedBodyKind
{
    /// <summary>The body of <c>Gpu.ForUnroll</c> (a loop) or <c>Gpu.SyncGrid(body)</c>, where the call expands it.</summary>
    Macro,

    /// <summary>A lambda, where it is created (also a lambda that a <c>Gpu</c> macro or StackLinq runs in place).</summary>
    Lambda,

    /// <summary>A local function, where it is called.</summary>
    LocalFunctionCall,

    /// <summary>A local function, where it is converted to a delegate.</summary>
    LocalFunctionDelegate
}
