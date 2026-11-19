namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>
/// Decisions for an <see cref="Expressions.IrCall"/> node (#1561).
/// <see cref="TargetPlan"/>, <see cref="Target"/>, and <see cref="Arguments"/> are native-dialect decisions
/// (<see cref="ITranspilerDialect"/>) and are not read by managed writers; managed writers read <see cref="Managed"/>.
/// </summary>
/// <param name="targetPlan">
/// The native-dialect call target plan, or <see langword="null"/> when left for syntax rules.
/// </param>
/// <param name="claim">Whether the invocation rule chain claims the call.</param>
/// <param name="arguments">Per-argument native-dialect decisions in parameter order.</param>
/// <param name="managed">Managed routing and caller-info decisions.</param>
internal sealed class IrCallDecision(
    CallTargetPlan? targetPlan,
    IrCallClaim claim,
    ImmutableArray<IrArgumentDecision> arguments,
    IrManagedCall managed) : IrDecision
{
    /// <summary>The native-dialect call target plan, or <see langword="null"/> when undecided.</summary>
    public CallTargetPlan? TargetPlan { get; } = targetPlan;

    /// <summary>The native-dialect call target kind (<see cref="TargetPlan"/>.<c>Kind</c>).</summary>
    public CallTargetKind? Target => TargetPlan?.Kind;

    /// <summary>Whether the invocation rule chain claims the call.</summary>
    public IrCallClaim Claim { get; } = claim;

    /// <summary>
    /// Per-argument native-dialect decisions in the same order as <see cref="Expressions.IrCall.Arguments"/>.
    /// </summary>
    public ImmutableArray<IrArgumentDecision> Arguments { get; } = arguments;

    /// <summary>Managed routing and caller-info decisions.</summary>
    public IrManagedCall Managed { get; } = managed;
}
