namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Whether an invocation is claimed by a rule in <c>TranspilerRuleRegistry.InvocationRules</c> before standard call
/// emission.
/// </summary>
internal enum InvocationRuleClaim
{
    /// <summary>No invocation rule claims the call; standard call classification applies.</summary>
    None,

    /// <summary>Claimed by <c>BclIntrinsicTranspilerRule</c> via the compilation's BCL intrinsic map.</summary>
    BclIntrinsicMap,

    /// <summary>
    /// Claimed by one of the remaining invocation rules (GPU macros, span/MemoryExtensions, StackLinq,
    /// <c>Nullable&lt;T&gt;.GetValueOrDefault</c>, or integer <c>Math.Abs</c>).
    /// </summary>
    Other
}
