namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.Macros;

/// <summary>
/// Checks <c>Gpu.SyncGrid</c>: the body is a lambda, the mode is a constant member of <c>GridSyncMode</c>, the kernel
/// is cooperative, and the statements of the body can be split at the grid syncs.
/// </summary>
internal static class SyncGridChecks
{
    /// <summary>
    /// Reports a body that is not a lambda, a non-cooperative kernel, and statements the split cannot keep.
    /// </summary>
    public static void CheckSyncGrid(
        InvocationExpressionSyntax invocation,
        GpuMacroCall call,
        ref ValidationContext context)
    {
        if (call.HasBody && !GpuCodeConstraints.TryGetSyncGridBody(invocation, out _))
        {
            var args = invocation.ArgumentList.Arguments;
            if (args.Count >= 1)
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.UnsupportedSyntax,
                    args[0],
                    GpuCodeConstraints.SyncGridRequiresLambda);
            }
        }

        if (!call.HasBody)
        {
            CheckMode(invocation, call, ref context);
        }

        if (context.MethodSymbol.HasAttribute(KnownAttributes.GpuKernel) &&
            !context.MethodSymbol.IsCooperativeKernel())
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.SyncGridRequiresCooperativeKernel,
                invocation,
                context.MethodSymbol.Name);
        }

        // A body flattened into an enclosing body is checked with the statements of that body
        if (call.HasBody &&
            GpuCodeConstraints.TryGetSyncGridBody(invocation, out var body) &&
            !SyncGridBody.IsFlattenedIntoEnclosingBody(invocation, context.SemanticModel))
        {
            var statements = SyncGridBody.Flatten(body, context.SemanticModel);
            CheckGuardedStatements(statements, ref context);
            CheckReturnsWithValue(statements, ref context);
        }
    }

    // [WHY] The CUDA build picks the barrier from the mode at compile time (GridSyncBarriers): a mode that is not a
    // constant, or names no member, has no barrier. Reported on every dialect alike, so that a kernel builds for all
    private static void CheckMode(
        InvocationExpressionSyntax invocation,
        GpuMacroCall call,
        ref ValidationContext context)
    {
        switch (GridSyncBarriers.Get(invocation, call, context.SemanticModel, out var modeArgument))
        {
            case GridSyncBarrier.NotConstant when modeArgument is not null:
                context.ReportDiagnostic(
                    GSharpDiagnostics.MacroArgumentMustBeConstant,
                    modeArgument,
                    GridSyncBarriers.ModeParameter,
                    GridSyncBarriers.MacroName);
                break;
            case GridSyncBarrier.Undefined when modeArgument is not null:
                context.ReportDiagnostic(
                    GSharpDiagnostics.UnsupportedSyntax,
                    modeArgument,
                    GridSyncBarriers.UndefinedMode);
                break;
        }
    }

    // [WHY] A return of the body sets a done flag and jumps past its statement; the later statements run in guards. A
    // goto across the gaps would skip the grid syncs and the guards, and a local that cannot be declared at the top of
    // the body would disappear inside its guard (#626)
    private static void CheckGuardedStatements(SyncGridStatements statements, ref ValidationContext context)
    {
        var model = context.SemanticModel;
        var gotos = ImmutableArray.CreateBuilder<GotoStatementSyntax>();
        for (var i = 0; i < statements.Elements.Length; i++)
        {
            var node = statements.Elements[i].Node;
            if (SyncGridBody.IsUnsplittable(node, model) && !statements.GetGuardBodies(i, model).IsEmpty)
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.UnsupportedSyntax,
                    node,
                    GpuCodeConstraints.SyncGridUnsplittableDeclaration);
            }

            SyncGridBody.AddGotosLeaving(node, gotos);
        }

        foreach (var jump in gotos)
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                jump,
                GpuCodeConstraints.SyncGridGotoAcrossGaps);
        }
    }

    // Defensive: the body is an Action, so C# rejects a return with a value as well
    private static void CheckReturnsWithValue(SyncGridStatements statements, ref ValidationContext context)
    {
        foreach (var element in statements.Elements)
        {
            if (!element.HasBodyReturn)
            {
                continue;
            }

            var nodes = element.Node.DescendantNodes(
                static n => n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax));
            foreach (var node in nodes)
            {
                if (node is ReturnStatementSyntax { Expression: not null } returnWithValue)
                {
                    context.ReportDiagnostic(
                        GSharpDiagnostics.UnsupportedSyntax,
                        returnWithValue,
                        GpuCodeConstraints.SyncGridReturnWithValue);
                }
            }
        }
    }
}
