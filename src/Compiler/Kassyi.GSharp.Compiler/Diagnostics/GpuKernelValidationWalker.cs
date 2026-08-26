using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.Diagnostics;
using Kassyi.GSharp.Compiler.Diagnostics.Rules;

namespace Kassyi.GSharp.Compiler.Diagnostics;

internal sealed class GpuKernelValidationWalker : CSharpSyntaxWalker
{
    private static readonly ConcurrentBag<GpuKernelValidationWalker> s_pool = [];

    private ValidationContext _context;

    private GpuKernelValidationWalker()
    {
    }

    public static GpuKernelValidationWalker Rent(ValidationContext context)
    {
        if (!s_pool.TryTake(out var walker))
        {
            walker = new GpuKernelValidationWalker();
        }

        walker._context = context;
        return walker;
    }

    public void Free()
    {
        _context = default;
        s_pool.Add(this);
    }

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        ForEachNotSupportedRule.Instance.Analyze(node, ref _context);
        base.VisitForEachStatement(node);
    }

    public override void VisitQueryExpression(QueryExpressionSyntax node)
    {
        LinqNotSupportedRule.Instance.Analyze(node, ref _context);
        base.VisitQueryExpression(node);
    }

    public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        ImplicitVarNeedsSuffixRule.Instance.Analyze(node, ref _context);
        base.VisitVariableDeclarator(node);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        GpuMacroArgumentRule.Instance.Analyze(node, ref _context);
        base.VisitInvocationExpression(node);
    }
}

