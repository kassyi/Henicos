namespace Kassyi.GSharp.Compiler;

/// <summary>Why an aggregate runs in each thread (the reason of Warning A, GSH0099).</summary>
internal enum ParallelAggregateFallback
{
    None,
    OutsideKernelBody,
    InsideLambda,
    DivergentCall,
    EarlyExit,
    ThreadPrivateSource,
    OrderDependentStage,
    SideEffect,
    ThreadVariantInput,
    UnsupportedElementType,
    UnfusedChain,
    Unresolved
}
