using Kassyi.GSharp.Compiler.Diagnostics.Rules;

namespace Kassyi.GSharp.Compiler.Diagnostics;

/// <summary>Pooled syntax walker that performs zero-allocation validation of GPU kernel AST nodes.</summary>
internal sealed class GpuKernelValidationWalker : PooledWalker<GpuKernelValidationWalker>
{
    private ValidationContext _context;
    internal ValidationContext Context => _context;

    public static GpuKernelValidationWalker Rent(ValidationContext context)
    {
        var walker = RentCore();
        walker._context = context;
        return walker;
    }

    protected override void OnReturn() => _context = default;

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
        ManualEnumeratorRule.Instance.Analyze(node, ref _context);
        GpuMacroArgumentRule.Instance.Analyze(node, ref _context);
        LinqNotSupportedRule.Instance.Analyze(node, ref _context);
        base.VisitInvocationExpression(node);
    }

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        ProhibitedApiRule.Instance.Analyze(node, ref _context);
        base.VisitMemberAccessExpression(node);
    }

    public override void VisitParameter(ParameterSyntax node)
    {
        ParamsModifierNotSupportedRule.Instance.Analyze(node, ref _context);
        base.VisitParameter(node);
    }
}

