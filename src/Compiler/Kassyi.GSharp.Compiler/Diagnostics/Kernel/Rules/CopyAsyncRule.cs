namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Verifies <c>Gpu.CopyAsync</c> destination and source arguments (GSH0075) and that the argument to
/// <c>Gpu.CopyAsyncWaitGroup</c> is a compile-time constant in <c>0..7</c> (GSH0074).
/// </summary>
internal sealed class CopyAsyncRule : ISyntaxValidationRule<InvocationExpressionSyntax>
{
    public static readonly CopyAsyncRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        GSharpDiagnostics.CopyAsyncWaitGroupCountOutOfRange,
        GSharpDiagnostics.InvalidCopyAsyncArgument);

    private CopyAsyncRule()
    {
    }

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetSymbol(node) is not IMethodSymbol
            {
                ContainingType.Name: TypeConstants.GpuType
            } method ||
            !method.ContainingNamespace.IsCoreNamespace())
        {
            return;
        }

        switch (method.Name)
        {
            case TypeConstants.GpuMacros.CopyAsyncWaitGroup when node.ArgumentList.Arguments.Count >= 1:
            {
                var countExpr = node.ArgumentList.Arguments[0].Expression;
                if (!GpuCodeConstraints.TryGetMacroConstantInt(countExpr, context.SemanticModel, out var count) ||
                    count is < 0 or > 7)
                {
                    context.ReportDiagnostic(GSharpDiagnostics.CopyAsyncWaitGroupCountOutOfRange, countExpr);
                }

                break;
            }

            case TypeConstants.GpuMacros.CopyAsync when node.ArgumentList.Arguments.Count >= 2:
            {
                CheckCopyAsync(node.ArgumentList.Arguments, ref context);
                break;
            }
        }
    }

    private static void CheckCopyAsync(SeparatedSyntaxList<ArgumentSyntax> arguments, ref ValidationContext context)
    {
        var dstArg = arguments[0];
        var srcArg = arguments[1];
        if (dstArg.NameColon is { Name.Identifier.ValueText: "globalSrc" } ||
            srcArg.NameColon is { Name.Identifier.ValueText: "sharedDst" })
        {
            (dstArg, srcArg) = (srcArg, dstArg);
        }

        var dstExpr = dstArg.Expression;
        if (!IsSharedMemoryLocation(dstExpr, context.SemanticModel))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.InvalidCopyAsyncArgument,
                dstExpr,
                "sharedDst",
                "the destination must be an element of SharedMemory<T>");
        }

        var srcExpr = srcArg.Expression;
        if (!RefSyntaxHelper.IsCppLValue(srcExpr, context.SemanticModel) ||
            !IsGlobalMemorySourceLocation(srcExpr, context.SemanticModel))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.InvalidCopyAsyncArgument,
                srcExpr,
                "globalSrc",
                "the source must refer to global memory (an array/Span parameter element or a ref/in parameter) without implicit conversion");
        }
    }

    internal static bool IsSharedMemoryLocation(ExpressionSyntax expression, SemanticModel semanticModel, int depth = 0)
    {
        if (depth > 8)
        {
            return false;
        }

        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case RefExpressionSyntax refExpr:
                    expression = refExpr.Expression;
                    continue;
                case MemberAccessExpressionSyntax memberAccess:
                    expression = memberAccess.Expression;
                    continue;
                case ElementAccessExpressionSyntax elementAccess:
                    if (semanticModel.GetTypeSymbol(elementAccess.Expression).IsSharedMemoryType())
                    {
                        return true;
                    }

                    expression = elementAccess.Expression;
                    continue;
                case IdentifierNameSyntax identifier when semanticModel.GetSymbol(identifier) is ILocalSymbol { IsRef: true } local &&
                                                          local.TryGetDeclaringSyntax(out VariableDeclaratorSyntax? declarator) &&
                                                          declarator.Initializer is { Value: { } initValue }:
                    return IsSharedMemoryLocation(initValue, semanticModel, depth + 1);
                default:
                    return false;
            }
        }
    }

    internal static bool IsGlobalMemorySourceLocation(ExpressionSyntax expression, SemanticModel semanticModel, int depth = 0)
    {
        if (depth > 8)
        {
            return false;
        }

        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case RefExpressionSyntax refExpr:
                    expression = refExpr.Expression;
                    continue;
                case MemberAccessExpressionSyntax memberAccess
                    when semanticModel.GetSymbol(memberAccess) is IFieldSymbol { IsConst: false, IsStatic: false }:
                    expression = memberAccess.Expression;
                    continue;
                case ElementAccessExpressionSyntax elementAccess:
                    return IsGlobalMemoryReceiver(elementAccess.Expression, semanticModel, depth);
                case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PointerIndirectionExpression):
                    return true;
                case IdentifierNameSyntax identifier:
                {
                    var symbol = semanticModel.GetSymbol(identifier);
                    return symbol switch
                    {
                        IParameterSymbol { RefKind: RefKind.Ref or RefKind.In or RefKind.RefReadOnlyParameter } => true,
                        ILocalSymbol { IsRef: true } refLocal when refLocal.TryGetDeclaringSyntax(out VariableDeclaratorSyntax? declarator) &&
                                                                   declarator.Initializer is { Value: { } initValue } =>
                            IsGlobalMemorySourceLocation(initValue, semanticModel, depth + 1),
                        _ => false
                    };
                }
                default:
                    return false;
            }
        }
    }

    private static bool IsGlobalMemoryReceiver(ExpressionSyntax receiver, SemanticModel semanticModel, int depth)
    {
        if (depth > 8)
        {
            return false;
        }

        while (true)
        {
            if (semanticModel.GetTypeSymbol(receiver).IsSharedMemoryType())
            {
                return false;
            }

            switch (receiver)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    receiver = parenthesized.Expression;
                    continue;
                case ElementAccessExpressionSyntax nestedElement:
                    receiver = nestedElement.Expression;
                    continue;
                case MemberAccessExpressionSyntax nestedMember
                    when semanticModel.GetSymbol(nestedMember) is IFieldSymbol { IsConst: false, IsStatic: false }:
                    receiver = nestedMember.Expression;
                    continue;
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax invMember } invocation
                    when semanticModel.GetTypeSymbol(invocation).IsArrayLikeType():
                    receiver = invMember.Expression;
                    continue;
                case IdentifierNameSyntax identifier:
                {
                    var symbol = semanticModel.GetSymbol(identifier);
                    return symbol switch
                    {
                        IParameterSymbol param =>
                            param.Type.UnwrapNullableArrayLike().IsArrayLikeType() || param.Type is IPointerTypeSymbol,
                        ILocalSymbol local when (local.Type.UnwrapNullableArrayLike().IsArrayLikeType() || local.Type is IPointerTypeSymbol) &&
                                                local.TryGetDeclaringSyntax(out VariableDeclaratorSyntax? declarator) &&
                                                declarator.Initializer is
                                                {
                                                    Value: not StackAllocArrayCreationExpressionSyntax
                                                        and not ImplicitStackAllocArrayCreationExpressionSyntax
                                                        and { } initValue
                                                } =>
                            IsGlobalMemoryReceiver(initValue, semanticModel, depth + 1),
                        _ => false
                    };
                }
                default:
                    return false;
            }
        }
    }
}
