namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

// ReSharper disable once UnusedType.Global -- Used implicitly by Roslyn compiler infra
// ReSharper disable once UnusedMember.Global -- Used implicitly by Roslyn compiler infra
/// <summary>
/// Intercepts and expands high-level GSharp GPU macros (such as unrolled loops, grid-stride iterations, and operator macros) into target dialect constructs.
/// </summary>
internal static class MacroTranspiler
{
    /// <summary>
    /// Attempts to intercept and transpile a high-level GPU macro invocation or assignment expression into target dialect constructs.
    /// </summary>
    public static bool TryTranspileMacro(ExpressionStatementSyntax node, ref TranspileContext context)
    {
        // [WHY] Two entry points: invocation statements go through the IMacroTranspilerRule chain, while
        // assignment statements (x = Op(a, b) with [GpuOperatorMacro]) go to GpuOperatorMacroTranspiler.
        return node.Expression switch
        {
            InvocationExpressionSyntax invocation => TryTranspileInvocation(invocation, node, ref context),
            AssignmentExpressionSyntax assignment => GpuOperatorMacroTranspiler.TryTranspile(assignment, ref context),
            _ => false
        };
    }

    private static bool TryTranspileInvocation(
        InvocationExpressionSyntax invocation,
        ExpressionStatementSyntax statement,
        ref TranspileContext context)
    {
        if (context.Query.SemanticModel.GetSymbol(invocation) is not IMethodSymbol symbol ||
            GpuMacroClassifier.Classify(symbol) is not { Kind: not GpuMacroKind.None } call)
        {
            return false;
        }

        foreach (var rule in context.RuleRegistry.MacroRules)
        {
            if (rule.TryTranspile(invocation, statement, call, ref context))
            {
                return true;
            }
        }

        return false;
    }
}
