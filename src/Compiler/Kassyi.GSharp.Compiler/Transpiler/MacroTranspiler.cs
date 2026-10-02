using Kassyi.GSharp.Compiler.Transpiler.Rules;
using Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

namespace Kassyi.GSharp.Compiler.Transpiler;

// ReSharper disable once UnusedType.Global -- Used implicitly by Roslyn compiler infra
// ReSharper disable once UnusedMember.Global -- Used implicitly by Roslyn compiler infra
/// <summary>
/// Intercepts and expands high-level GSharp GPU macros (such as unrolled loops, grid-stride iterations, and operator macros) into target dialect constructs.
/// </summary>
internal static class MacroTranspiler
{
    private static readonly ImmutableArray<IMacroTranspilerRule> s_macroRules =
        ImmutableArray.Create<IMacroTranspilerRule>(
            new HintUnrollMacroRule(),
            new ForUnrollMacroRule(),
            new GridStrideLoopMacroRule(),
            new SingleThreadMacroRule(),
            new SyncGridMacroRule()
        );

    /// <summary>
    /// Attempts to intercept and transpile a high-level GPU macro invocation or assignment expression into target dialect constructs.
    /// </summary>
    public static bool TryTranspileMacro(ExpressionStatementSyntax node, ref TranspileContext context)
    {
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
            !symbol.ContainingNamespace.IsCoreNamespace())
        {
            return false;
        }

        foreach (var rule in s_macroRules)
        {
            if (rule.TryTranspile(invocation, statement, symbol, ref context))
            {
                return true;
            }
        }

        return false;
    }
}
