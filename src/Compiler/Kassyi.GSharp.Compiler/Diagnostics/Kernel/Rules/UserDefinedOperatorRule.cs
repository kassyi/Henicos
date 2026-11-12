namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Rejects a user-defined operator in GPU code that has no device definition
/// (<see cref="GpuCodeConstraints.GetOperatorProblem"/>), whatever the namespace of its type, and a
/// <c>[GpuOperatorMacro]</c> operator where it is not expanded.
/// </summary>
/// <remarks>
/// Direct support for user-defined operators in GPU code depends on #402 (emitting operators as named device functions
/// called with the C#-resolved overload). The transpiler fails the same operators from the same decision, and
/// <see cref="TranspilerFailureCheck"/> drops its GSH0060 in the method this rule walks.
/// </remarks>
internal sealed class UserDefinedOperatorRule :
    ISyntaxValidationRule<BinaryExpressionSyntax>,
    ISyntaxValidationRule<AssignmentExpressionSyntax>,
    ISyntaxValidationRule<PrefixUnaryExpressionSyntax>,
    ISyntaxValidationRule<PostfixUnaryExpressionSyntax>
{
    public static readonly UserDefinedOperatorRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.UserDefinedOperatorNotSupported, GSharpDiagnostics.UnsupportedSyntax);

    private UserDefinedOperatorRule()
    {
    }

    public void Analyze(BinaryExpressionSyntax node, ref ValidationContext context) =>
        CheckOperator(node, node.OperatorToken, ref context);

    public void Analyze(AssignmentExpressionSyntax node, ref ValidationContext context) =>
        CheckOperator(node, node.OperatorToken, ref context);

    public void Analyze(PrefixUnaryExpressionSyntax node, ref ValidationContext context) =>
        CheckOperator(node, node.OperatorToken, ref context);

    public void Analyze(PostfixUnaryExpressionSyntax node, ref ValidationContext context) =>
        CheckOperator(node, node.OperatorToken, ref context);

    private static void CheckOperator(ExpressionSyntax node, SyntaxToken operatorToken, ref ValidationContext context)
    {
        var model = context.SemanticModel;
        if (GpuCodeConstraints.GetUserDefinedOperator(node, model) is not var (method, _) ||
            RemovedCalls.IsInRemovedCall(node, model))
        {
            return;
        }

        if (method.HasAttribute(KnownAttributes.GpuOperatorMacro))
        {
            // Expanded only where GpuOperatorMacroTranspiler reaches it; elsewhere it would be a C++ struct operator
            if (OperatorMacroPosition.GetPositionFailure(node, model) is { } failure)
            {
                context.ReportDiagnostic(GSharpDiagnostics.UnsupportedSyntax, node, failure);
            }

            return;
        }

        if (GpuCodeConstraints.GetOperatorProblem(method, context.Dialect) is var (name, type))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UserDefinedOperatorNotSupported,
                operatorToken.GetLocation(),
                name,
                type);
        }
    }
}
