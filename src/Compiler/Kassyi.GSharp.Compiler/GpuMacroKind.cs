namespace Kassyi.GSharp.Compiler;

/// <summary>The <c>Gpu</c> macros that the transpiler expands and the analyzers validate.</summary>
internal enum GpuMacroKind
{
    None,
    ForUnroll,
    GridStrideLoop,
    SingleThread,
    SyncGrid,
    InlinePtx,
    Constant,
    HintUnroll,
    NotNull
}
