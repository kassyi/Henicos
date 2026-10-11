
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

    private static readonly ImmutableArray<ISyntaxValidationRule<ForEachStatementSyntax>> s_forEachRules
        = DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<ForEachStatementSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<QueryExpressionSyntax>> s_queryRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<QueryExpressionSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<VariableDeclaratorSyntax>> s_variableRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<VariableDeclaratorSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<InvocationExpressionSyntax>> s_invocationRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<InvocationExpressionSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<MemberAccessExpressionSyntax>> s_memberAccessRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<MemberAccessExpressionSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<ParameterSyntax>> s_parameterRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<ParameterSyntax>>().ToImmutableArray();

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        foreach (var rule in s_forEachRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitForEachStatement(node);
    }

    public override void VisitQueryExpression(QueryExpressionSyntax node)
    {
        foreach (var rule in s_queryRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitQueryExpression(node);
    }

    public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        foreach (var rule in s_variableRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitVariableDeclarator(node);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        foreach (var rule in s_invocationRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitInvocationExpression(node);
    }

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        foreach (var rule in s_memberAccessRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitMemberAccessExpression(node);
    }

    public override void VisitParameter(ParameterSyntax node)
    {
        foreach (var rule in s_parameterRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitParameter(node);
    }
}

