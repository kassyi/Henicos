namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>Runs the IR decision pass on a method body (#1561).</summary>
internal static class IrDecisions
{
    /// <summary>
    /// The method body with the decisions of every node a writer reads; the shape of the tree does not change.
    /// </summary>
    public static IrMethod Decide(IrMethod method, IrDecisionContext context) =>
        new IrDecider(context).RewriteMethod(method);
}
