namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Transpiles a <c>Gpu.SyncGrid(body)</c> statement (CUDA): the statements of the body with a cooperative grid sync
/// (<c>cg::this_grid().sync();</c>) between its top-level statements.
/// </summary>
/// <remarks>
/// <para>
/// The statements and the gaps that get a sync come from <see cref="SyncGridBody"/>, which the CPU lowering
/// (<see cref="PreLoweringExpander"/>) reads too, so both dialects synchronize at the same places. A nested block
/// (<c>for</c>, <c>if</c>, a loop macro body, a local function) is one statement and is emitted as usual, without syncs
/// inside it: a grid sync that only some threads reach deadlocks (#626). A <c>Gpu.SyncGrid(body)</c> written inside
/// such a block is a statement of its own and gets its own syncs.
/// </para>
/// <para>
/// A <c>Gpu.SyncGrid(body)</c> that is itself a statement of the body is flattened into the gaps of the enclosing body,
/// and its statements are emitted in a scope of their own, as the lambda scopes its locals in C#.
/// </para>
/// </remarks>
internal sealed class SyncGridMacroRule : IMacroTranspilerRule
{
    private const string GridSync = "cg::this_grid().sync();";

    public bool TryTranspile(
        InvocationExpressionSyntax invocation,
        ExpressionStatementSyntax statement,
        GpuMacroCall call,
        ref TranspileContext context)
    {
        if (call is not { Kind: GpuMacroKind.SyncGrid, HasBody: true })
        {
            return false;
        }

        if (!GpuCodeConstraints.TryGetSyncGridBody(invocation, out var lambdaExpr))
        {
            var args = invocation.ArgumentList.Arguments;
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, args.Count > 0 ? args[0] : invocation,
                GpuCodeConstraints.SyncGridRequiresLambda);
            return false;
        }

        var statements = SyncGridBody.Flatten(lambdaExpr, context.Query.SemanticModel);

        // [WHY] Completely eliminate empty Gpu.SyncGrid(() => {}) as a No-op (zero CUDA C++ output).
        if (statements.HasExecutableStatements)
        {
            var index = 0;
            EmitBody(statements, bodyIndex: 0, ref index, ref context);
        }

        return true;
    }

    // Emits, in a scope, the statements of body bodyIndex from elements[index] on: its own statements, and the bodies
    // nested in it through EmitBody again. Returns with index at the first statement outside the body.
    private static void EmitBody(
        SyncGridStatements statements,
        int bodyIndex,
        ref int index,
        ref TranspileContext context)
    {
        var elements = statements.Elements;
        var model = context.Query.SemanticModel;
        using (context.Emitter.Writer.Scope())
        {
            while (index < elements.Length && IsWithin(statements, elements[index].BodyIndex, bodyIndex))
            {
                var element = elements[index];
                if (element.BodyIndex == bodyIndex)
                {
                    context.Visitor.Visit(element.Node);
                    // The expression of an expression-bodied lambda is a statement of the body
                    context.Emitter.Writer.AppendIf(element.Node is ExpressionSyntax, ";").AppendLine();
                    index++;
                }
                else
                {
                    EmitBody(statements, ChildOnPath(statements, element.BodyIndex, bodyIndex), ref index, ref context);
                }

                // The sync after a statement goes where the next statement is: after the scopes that end here, before
                // the ones that start with it
                if (index < elements.Length &&
                    IsWithin(statements, elements[index].BodyIndex, bodyIndex) &&
                    SyncGridBody.IsSyncPoint(elements, index - 1, model))
                {
                    context.Emitter.Writer.AppendLine(GridSync);
                }
            }
        }
    }

    // Whether body is ancestor or the same body as descendant
    private static bool IsWithin(SyncGridStatements statements, int descendant, int ancestor)
    {
        for (var body = descendant; body >= 0; body = statements.BodyParents[body])
        {
            if (body == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    // The body directly nested in ancestor on the way to descendant
    private static int ChildOnPath(SyncGridStatements statements, int descendant, int ancestor)
    {
        var body = descendant;
        while (statements.BodyParents[body] != ancestor)
        {
            body = statements.BodyParents[body];
        }

        return body;
    }
}
