using System.Collections.Concurrent;
using Kassyi.GSharp.Compiler.Diagnostics.Rules;

namespace Kassyi.GSharp.Compiler.Diagnostics;

internal sealed class GpuKernelValidationWalker : CSharpSyntaxWalker, IDisposable
{
    private static readonly ConcurrentBag<GpuKernelValidationWalker> s_pool = [];

    private ValidationContext _context;
    private int _isRented;

    private GpuKernelValidationWalker()
    {
    }

    public static GpuKernelValidationWalker Rent(ValidationContext context)
    {
        if (!s_pool.TryTake(out var walker))
        {
            walker = new GpuKernelValidationWalker();
        }

        Volatile.Write(ref walker._isRented, 1);

        walker._context = context;
        return walker;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isRented, 0) != 1)
        {
            return;
        }

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

