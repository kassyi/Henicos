using Kassyi.GSharp.Compiler.Ir.Linear;

namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>Runs the IR decision pass on a method body or a lowered state machine (#1561).</summary>
internal static class IrDecisions
{
    /// <summary>
    /// The method body with the decisions of every node a writer reads; the shape of the tree does not change.
    /// </summary>
    public static IrMethod Decide(IrMethod method, IrDecisionContext context) =>
        new IrDecider(context).RewriteMethod(method);

    /// <summary>
    /// The same for the statements of every state of <paramref name="machine"/> (after the split plans were applied
    /// and linearized).
    /// </summary>
    public static IrStateMachine Decide(IrStateMachine machine, IrDecisionContext context) =>
        new IrDecider(context).VisitStateMachine(machine);
}
