using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Rejects user-defined operators in GPU code unless mapped via <c>[GpuOperatorMacro]</c>.
/// </summary>
/// <remarks>
/// Direct support for user-defined operators in GPU code depends on #402 (emitting operators as named device functions called with the C#-resolved overload).
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
        var methodSymbol = GetOperatorMethodSymbol(node, context.SemanticModel);
        if (methodSymbol is not { MethodKind: MethodKind.UserDefinedOperator })
        {
            return;
        }

        if (methodSymbol.HasAttribute(KnownAttributes.GpuOperatorMacro))
        {
            // Expanded only where GpuOperatorMacroTranspiler reaches it; elsewhere it would be a C++ struct operator
            if (OperatorMacroResolver.GetPositionFailure(node, context.SemanticModel) is { } failure)
            {
                context.ReportDiagnostic(GSharpDiagnostics.UnsupportedSyntax, node, failure);
            }

            return;
        }

        var ns = methodSymbol.ContainingType?.ContainingNamespace;
        if (ns.IsSystemOrNumerics() || ns.IsCoreNamespace())
        {
            return;
        }

        var containingTypeName = methodSymbol.ContainingType?.Name ?? string.Empty;
        context.ReportDiagnostic(
            GSharpDiagnostics.UserDefinedOperatorNotSupported,
            operatorToken.GetLocation(),
            methodSymbol.Name,
            containingTypeName);
    }

    private static IMethodSymbol? GetOperatorMethodSymbol(ExpressionSyntax node, SemanticModel semanticModel)
    {
        if (semanticModel.GetSymbol(node) is IMethodSymbol symbol)
        {
            return symbol;
        }

        return semanticModel.GetSafeOperation(node) switch
        {
            IIncrementOrDecrementOperation incDec => incDec.OperatorMethod,
            ICompoundAssignmentOperation compound => compound.OperatorMethod,
            IUnaryOperation unary => unary.OperatorMethod,
            IBinaryOperation binary => binary.OperatorMethod,
            _ => null
        };
    }
}
