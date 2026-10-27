namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

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

    private static readonly ImmutableArray<ISyntaxValidationRule<IdentifierNameSyntax>> s_identifierNameRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<IdentifierNameSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<ParameterSyntax>> s_parameterRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<ParameterSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<MethodDeclarationSyntax>> s_methodRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<MethodDeclarationSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<LocalFunctionStatementSyntax>> s_localFunctionRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<LocalFunctionStatementSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<SingleVariableDesignationSyntax>> s_designationRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<SingleVariableDesignationSyntax>>()
            .ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<BinaryExpressionSyntax>> s_binaryRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<BinaryExpressionSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<ConditionalExpressionSyntax>> s_conditionalRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<ConditionalExpressionSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<AssignmentExpressionSyntax>> s_assignmentRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<AssignmentExpressionSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<PrefixUnaryExpressionSyntax>> s_prefixUnaryRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<PrefixUnaryExpressionSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<PostfixUnaryExpressionSyntax>> s_postfixUnaryRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<PostfixUnaryExpressionSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<VariableDeclarationSyntax>> s_variableDeclarationRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<VariableDeclarationSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<PredefinedTypeSyntax>> s_predefinedTypeRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<PredefinedTypeSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<LiteralExpressionSyntax>> s_literalRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<LiteralExpressionSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<ObjectCreationExpressionSyntax>> s_objectCreationRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<ObjectCreationExpressionSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<ImplicitObjectCreationExpressionSyntax>> s_implicitObjectCreationRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<ImplicitObjectCreationExpressionSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<SwitchExpressionArmSyntax>> s_switchArmRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<SwitchExpressionArmSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<LocalDeclarationStatementSyntax>> s_localDeclarationRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<LocalDeclarationStatementSyntax>>().ToImmutableArray();

    private static readonly ImmutableArray<ISyntaxValidationRule<DestructorDeclarationSyntax>> s_destructorRules =
        DiagnosticRuleRegistry.Rules.OfType<ISyntaxValidationRule<DestructorDeclarationSyntax>>().ToImmutableArray();

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        foreach (var rule in s_methodRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitMethodDeclaration(node);
    }

    // The generator emits no finalizer, so its body is not checked
    public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node)
    {
        foreach (var rule in s_destructorRules)
        {
            rule.Analyze(node, ref _context);
        }
    }

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        foreach (var rule in s_localFunctionRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitLocalFunctionStatement(node);
    }

    public override void VisitSingleVariableDesignation(SingleVariableDesignationSyntax node)
    {
        foreach (var rule in s_designationRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitSingleVariableDesignation(node);
    }

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

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        foreach (var rule in s_identifierNameRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitIdentifierName(node);
    }

    public override void VisitParameter(ParameterSyntax node)
    {
        foreach (var rule in s_parameterRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitParameter(node);
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        foreach (var rule in s_binaryRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitBinaryExpression(node);
    }

    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        foreach (var rule in s_conditionalRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitConditionalExpression(node);
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        foreach (var rule in s_assignmentRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitAssignmentExpression(node);
    }

    public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        foreach (var rule in s_prefixUnaryRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitPrefixUnaryExpression(node);
    }

    public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
    {
        foreach (var rule in s_postfixUnaryRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitPostfixUnaryExpression(node);
    }

    public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
    {
        foreach (var rule in s_variableDeclarationRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitVariableDeclaration(node);
    }

    public override void VisitPredefinedType(PredefinedTypeSyntax node)
    {
        foreach (var rule in s_predefinedTypeRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitPredefinedType(node);
    }

    public override void VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        foreach (var rule in s_literalRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitLiteralExpression(node);
    }

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        foreach (var rule in s_objectCreationRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitObjectCreationExpression(node);
    }

    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
    {
        foreach (var rule in s_implicitObjectCreationRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitImplicitObjectCreationExpression(node);
    }

    public override void VisitSwitchExpressionArm(SwitchExpressionArmSyntax node)
    {
        foreach (var rule in s_switchArmRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitSwitchExpressionArm(node);
    }

    public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        foreach (var rule in s_localDeclarationRules)
        {
            rule.Analyze(node, ref _context);
        }

        base.VisitLocalDeclarationStatement(node);
    }
}
