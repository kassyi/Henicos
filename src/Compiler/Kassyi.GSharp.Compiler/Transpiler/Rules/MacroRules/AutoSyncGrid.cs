namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Emits the statements of a <c>Gpu.SyncGrid(() =&gt; { ... })</c> body with a cooperative grid sync inserted between consecutive statements.
/// </summary>
/// <remarks>
/// Which statements make up the body and which gaps get a sync is decided by <see cref="SyncGridBody"/>, shared with the
/// CPU lowering. Active while <see cref="Emitter.AutoSyncGridDepth"/> is positive.
/// Only this class writes the depth: rules enter, suppress and restore it through <see cref="Enter"/>,
/// <see cref="Suppress"/> and <see cref="Restore"/>, in a <c>try</c> / <c>finally</c> because the depth lives in a struct
/// that a scope object cannot reach.
/// </remarks>
internal static class AutoSyncGrid
{
    /// <summary>Whether statements are emitted in auto-sync mode.</summary>
    public static bool IsActive(in TranspileContext context) => context.Emitter.AutoSyncGridDepth > 0;

    /// <summary>Enters the body of a <c>Gpu.SyncGrid</c> lambda.</summary>
    /// <returns>The depth to pass to <see cref="Restore"/>; positive when the body is nested in another one.</returns>
    public static int Enter(ref TranspileContext context) => context.Emitter.AutoSyncGridDepth++;

    /// <summary>
    /// Turns auto-sync off, for a region that only some threads run: a grid sync there would deadlock, because it waits
    /// for every thread of the grid.
    /// </summary>
    /// <returns>The depth to pass to <see cref="Restore"/>.</returns>
    public static int Suppress(ref TranspileContext context)
    {
        var previous = context.Emitter.AutoSyncGridDepth;
        context.Emitter.AutoSyncGridDepth = 0;
        return previous;
    }

    /// <summary>Restores the depth returned by <see cref="Enter"/> or <see cref="Suppress"/>.</summary>
    public static void Restore(ref TranspileContext context, int depth) => context.Emitter.AutoSyncGridDepth = depth;

    /// <summary>
    /// Emits the statements of <paramref name="node"/> in auto-sync mode.
    /// </summary>
    public static void VisitBlock(BlockSyntax node, ref TranspileContext context)
    {
        var statements = SyncGridBody.Flatten(node, context.Query.SemanticModel);
        VisitStatements(statements.Elements, ref context);
    }

    /// <summary>
    /// Emits already flattened statements, inserting <c>cg::this_grid().sync();</c> where
    /// <see cref="SyncGridBody.IsSyncPoint"/> places one.
    /// </summary>
    public static void VisitStatements(ImmutableArray<SyncGridElement> statements, ref TranspileContext context)
    {
        for (var i = 0; i < statements.Length; i++)
        {
            var node = statements[i].Node;
            context.Visitor.Visit(node);
            // The expression of an expression-bodied lambda is a statement of the body
            context.Emitter.Writer.AppendIf(node is ExpressionSyntax, ";").AppendLine();
            if (IsActive(context) && SyncGridBody.IsSyncPoint(statements, i, context.Query.SemanticModel))
            {
                context.Emitter.Writer.AppendLine("cg::this_grid().sync();");
            }
        }
    }
}
