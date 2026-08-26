using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.Diagnostics;
using Kassyi.GSharp.Compiler.Diagnostics.Rules;

namespace Kassyi.GSharp.Compiler.Diagnostics;

internal sealed class GpuKernelValidationWalker : CSharpSyntaxWalker
{
    private static readonly ConcurrentBag<GpuKernelValidationWalker> s_pool = [];

    private SyntaxNodeAnalysisContext _analyzerContext;
    private IMethodSymbol _methodSymbol;

    private GpuKernelValidationWalker()
    {
    }

    public static GpuKernelValidationWalker Rent(ValidationContext context)
    {
        if (!s_pool.TryTake(out var walker))
        {
            walker = new GpuKernelValidationWalker();
        }

        walker._analyzerContext = context.AnalyzerContext;
        walker._methodSymbol = context.MethodSymbol;
        return walker;
    }

    public void Free()
    {
        _analyzerContext = default;
        _methodSymbol = default;
        s_pool.Add(this);
    }

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        var context = new ValidationContext(_analyzerContext, _methodSymbol);
        ForEachNotSupportedRule.Instance.Analyze(node, ref context);
        base.VisitForEachStatement(node);
    }

    public override void VisitQueryExpression(QueryExpressionSyntax node)
    {
        var context = new ValidationContext(_analyzerContext, _methodSymbol);
        LinqNotSupportedRule.Instance.Analyze(node, ref context);
        base.VisitQueryExpression(node);
    }

    public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        var context = new ValidationContext(_analyzerContext, _methodSymbol);
        ImplicitVarNeedsSuffixRule.Instance.Analyze(node, ref context);
        base.VisitVariableDeclarator(node);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var context = new ValidationContext(_analyzerContext, _methodSymbol);
        GpuMacroArgumentRule.Instance.Analyze(node, ref context);
        base.VisitInvocationExpression(node);
    }
}
