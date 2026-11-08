using Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.Wmma;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Validates Tensor Core warp matrix (<c>Wmma</c>) fragment types, marker types, and operations (GSH0076).
/// </summary>
/// <remarks>
/// Where a type may be used is checked by <see cref="WmmaTypeUsage"/>, the supported fragment shapes by
/// <see cref="WmmaFragmentTypes"/>, and the arguments of the <c>Wmma</c> calls by <see cref="WmmaCallArguments"/>.
/// </remarks>
internal sealed class WmmaRule :
    ISyntaxValidationRule<MethodDeclarationSyntax>,
    ISyntaxValidationRule<LocalFunctionStatementSyntax>,
    ISyntaxValidationRule<ParameterSyntax>,
    ISyntaxValidationRule<VariableDeclarationSyntax>,
    ISyntaxValidationRule<BaseObjectCreationExpressionSyntax>,
    ISyntaxValidationRule<InvocationExpressionSyntax>
{
    public static readonly WmmaRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.UnsupportedWarpMatrixOperation);

    private WmmaRule()
    {
    }

    public void Analyze(MethodDeclarationSyntax node, ref ValidationContext context) =>
        ReportIfNotAllowed(node.ReturnType, allowDirectFragment: false, isKernelParameter: false, ref context);

    public void Analyze(LocalFunctionStatementSyntax node, ref ValidationContext context) =>
        ReportIfNotAllowed(node.ReturnType, allowDirectFragment: false, isKernelParameter: false, ref context);

    public void Analyze(ParameterSyntax node, ref ValidationContext context)
    {
        if (node.Type is null)
        {
            return;
        }

        var isKernelParam = node.Parent?.Parent is MethodDeclarationSyntax &&
                            context.MethodSymbol.HasAttribute(KnownAttributes.GpuKernel);
        ReportIfNotAllowed(node.Type, allowDirectFragment: !isKernelParam, isKernelParam, ref context);
    }

    public void Analyze(VariableDeclarationSyntax node, ref ValidationContext context)
    {
        var type = context.SemanticModel.GetTypeSymbol(node.Type);
        if (WmmaTypeUsage.IsAllowed(
                type,
                allowDirectFragment: true,
                isKernelParameter: false,
                out var targetName,
                out var reason
            ))
        {
            return;
        }

        var reportNode = node.Type.IsVar &&
                         node.Variables is [{ Initializer.Value: { } initVal }] &&
                         TryGetInitializerTypeSyntax(initVal) is { } initTypeSyntax
            ? initTypeSyntax
            : node.Type;

        context.ReportDiagnostic(GSharpDiagnostics.UnsupportedWarpMatrixOperation, reportNode, targetName, reason);
    }

    public void Analyze(BaseObjectCreationExpressionSyntax node, ref ValidationContext context)
    {
        if (node.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax } })
        {
            return;
        }

        ReportIfNotAllowed(node, allowDirectFragment: true, isKernelParameter: false, ref context);
    }

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetSymbol(node) is not IMethodSymbol method)
        {
            return;
        }

        if (method.ContainingType is { Name: TypeConstants.WmmaType } &&
            method.ContainingNamespace.IsCoreNamespace())
        {
            WmmaCallArguments.Analyze(node, method, ref context);
            return;
        }

        if (!method.IsGenericMethod)
        {
            return;
        }

        foreach (var typeArg in method.TypeArguments)
        {
            if (!WmmaTypeUsage.IsAllowed(
                    typeArg,
                    allowDirectFragment: false,
                    isKernelParameter: false,
                    out var targetName,
                    out var reason
                ))
            {
                context.ReportDiagnostic(GSharpDiagnostics.UnsupportedWarpMatrixOperation, node, targetName, reason);
                break;
            }
        }
    }

    // Reports node when its type cannot be used at this position.
    private static void ReportIfNotAllowed(
        SyntaxNode node,
        bool allowDirectFragment,
        bool isKernelParameter,
        ref ValidationContext context)
    {
        var type = context.SemanticModel.GetTypeSymbol(node);
        if (!WmmaTypeUsage.IsAllowed(type, allowDirectFragment, isKernelParameter, out var targetName, out var reason))
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnsupportedWarpMatrixOperation, node, targetName, reason);
        }
    }

    private static TypeSyntax? TryGetInitializerTypeSyntax(ExpressionSyntax initializer) =>
        initializer switch
        {
            ObjectCreationExpressionSyntax objectCreation => objectCreation.Type,
            DefaultExpressionSyntax defaultExpr => defaultExpr.Type,
            _ => null
        };
}
