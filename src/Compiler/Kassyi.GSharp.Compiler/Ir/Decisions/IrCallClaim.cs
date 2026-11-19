namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>
/// Whether the invocation rule chain claims an <see cref="Expressions.IrCall"/> before standard emission.
/// </summary>
internal enum IrCallClaim
{
    /// <summary>No rule in the chain claims the call.</summary>
    None,

    /// <summary>Claimed by <c>BclIntrinsicTranspilerRule</c>.</summary>
    BclIntrinsicMap,

    /// <summary>Claimed by one of the other rules in <c>TranspilerRuleRegistry.InvocationRules</c>.</summary>
    OtherRule
}
