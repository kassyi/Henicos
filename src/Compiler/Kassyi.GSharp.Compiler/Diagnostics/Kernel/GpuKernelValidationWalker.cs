namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>Pooled syntax walker that performs zero-allocation validation of GPU kernel AST nodes.</summary>
/// <remarks>
/// Each visit runs the rules registered for the node type (<see cref="SyntaxValidationRules{TNode}"/>) and then visits
/// the children; the checks themselves live in the rules.
/// </remarks>
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

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        Analyze(node);
        base.VisitMethodDeclaration(node);
    }

    // The generator emits no finalizer, so its body is not checked
    public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node) => Analyze(node);

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        Analyze(node);
        base.VisitLocalFunctionStatement(node);
    }

    public override void VisitSingleVariableDesignation(SingleVariableDesignationSyntax node)
    {
        Analyze(node);
        base.VisitSingleVariableDesignation(node);
    }

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        Analyze(node);
        base.VisitForEachStatement(node);
    }

    public override void VisitQueryExpression(QueryExpressionSyntax node)
    {
        Analyze(node);
        base.VisitQueryExpression(node);
    }

    public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        Analyze(node);
        base.VisitVariableDeclarator(node);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        Analyze(node);
        base.VisitInvocationExpression(node);
    }

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        Analyze(node);
        base.VisitMemberAccessExpression(node);
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        Analyze(node);
        base.VisitIdentifierName(node);
    }

    public override void VisitParameter(ParameterSyntax node)
    {
        Analyze(node);
        base.VisitParameter(node);
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        Analyze(node);
        base.VisitBinaryExpression(node);
    }

    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        Analyze(node);
        base.VisitConditionalExpression(node);
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        Analyze(node);
        base.VisitAssignmentExpression(node);
    }

    public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        Analyze(node);
        base.VisitPrefixUnaryExpression(node);
    }

    public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
    {
        Analyze(node);
        base.VisitPostfixUnaryExpression(node);
    }

    public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
    {
        Analyze(node);
        base.VisitVariableDeclaration(node);
    }

    public override void VisitPredefinedType(PredefinedTypeSyntax node)
    {
        Analyze(node);
        base.VisitPredefinedType(node);
    }

    public override void VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        Analyze(node);
        base.VisitLiteralExpression(node);
    }

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        Analyze(node);
        base.VisitObjectCreationExpression(node);
    }

    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
    {
        Analyze(node);
        base.VisitImplicitObjectCreationExpression(node);
    }

    public override void VisitCheckedExpression(CheckedExpressionSyntax node)
    {
        Analyze(node);
        base.VisitCheckedExpression(node);
    }

    public override void VisitCheckedStatement(CheckedStatementSyntax node)
    {
        Analyze(node);
        base.VisitCheckedStatement(node);
    }

    public override void VisitSwitchExpressionArm(SwitchExpressionArmSyntax node)
    {
        Analyze(node);
        base.VisitSwitchExpressionArm(node);
    }

    public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        Analyze(node);
        base.VisitLocalDeclarationStatement(node);
    }

    private void Analyze<TNode>(TNode node) where TNode : SyntaxNode
    {
        foreach (var rule in SyntaxValidationRules<TNode>.All)
        {
            rule.Analyze(node, ref _context);
        }
    }
}
