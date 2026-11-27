namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>The kind of per-iteration key emitted for a keyed loop on CUDA.</summary>
internal enum IterationKeyKind
{
    /// <summary>A synthesized 64-bit <c>unsigned long long</c> iteration counter.</summary>
    Counter,

    /// <summary>The loop's own <c>int</c> or <c>uint</c> induction variable cast to <c>unsigned int</c>.</summary>
    LoopVariable
}
