using Kassyi.GSharp.Compiler.Transpiler.Helpers;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

public sealed class MemberAccessExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<MemberAccessExpressionSyntax>
{
    public static readonly MemberAccessExpressionTranspilerRule Instance = new();

    private MemberAccessExpressionTranspilerRule()
    {
    }

    public override void Transpile(MemberAccessExpressionSyntax node, ref TranspileContext context)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(node).Symbol;

        if (symbol == null && node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node)
        {
            symbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol;
        }

        if (symbol != null && TryTranspileIntrinsic(symbol, ref context))
        {
            return;
        }

        if (symbol != null && TryEnsureNotProhibitedApi(symbol, node, ref context))
        {
            return;
        }

        if (symbol is IFieldSymbol { IsConst: true, HasConstantValue: true } field)
        {
            context.Writer.AppendConstantField(field);
            return;
        }

        var memberExprSymbol = context.SemanticModel.GetSymbolInfo(node.Expression).Symbol;
        if (TryTranspileEnumAccess(memberExprSymbol, node, ref context))
        {
            return;
        }

        if (memberExprSymbol is ITypeSymbol)
        {
            // Skip class name for global static methods
            context.Writer.Append(node.Name.Identifier.Text);
            return;
        }

        if (TryTranspileStackArrayLength(node, ref context))
        {
            return;
        }

        TranspileMemberAccess(node, ref context);
    }

    private static bool TryTranspileIntrinsic(ISymbol symbol, ref TranspileContext context)
    {
        if (context.Dialect.TryMapIntrinsic(symbol, out var mappedName))
        {
            context.Writer.Append(mappedName);
            return true;
        }

        return false;
    }

    private static bool TryEnsureNotProhibitedApi(ISymbol symbol, MemberAccessExpressionSyntax node,
        ref TranspileContext context)
    {
        if (symbol is
            {
                ContainingType.Name: TypeConstants.MathType or TypeConstants.MathFType
                or TypeConstants.InterlockedType or TypeConstants.BitOperationsType
            } &&
            (symbol.ContainingNamespace.IsSystemNamespace() || symbol.ContainingNamespace.IsSystemThreading() || symbol.ContainingNamespace.IsSystemOrNumerics()))
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(
                GSharpDiagnostics.UnsupportedSyntax,
                node.GetLocation(),
                $"{symbol.ContainingType.Name}.{symbol.Name}"));
            context.Writer.Append($"__gsharp_unsupported_{symbol.Name}");
            return true;
        }

        return false;
    }

    private static bool TryTranspileEnumAccess(ISymbol? memberExprSymbol, MemberAccessExpressionSyntax node,
        ref TranspileContext context)
    {
        if (memberExprSymbol is ITypeSymbol { TypeKind: TypeKind.Enum })
        {
            context.Visitor.Visit(node.Expression);
            context.Writer.Append($"::{node.Name.Identifier.Text}");
            return true;
        }

        return false;
    }

    private static void TranspileMemberAccess(MemberAccessExpressionSyntax node, ref TranspileContext context)
    {
        context.Visitor.Visit(node.Expression);
        context.Writer.Append(".");

        var memberName = node.Name.Identifier.Text;
        var typeInfo = context.SemanticModel.GetTypeInfo(node.Expression);
        if (typeInfo.Type != null)
        {
            if (typeInfo.Type.IsSystemNumericsVector())
            {
                memberName = memberName.ToLowerInvariant();
            }
            else if (memberName == "Length")
            {
                if (typeInfo.Type.HasAttribute(KnownAttributes.InlineArray))
                {
                    context.Writer.Append("size()");
                    return;
                }

                memberName = "length";
            }
            else if (memberName == "Count")
            {
                memberName = "length";
            }
        }

        context.Writer.Append(memberName);
    }

    private static bool TryTranspileStackArrayLength(MemberAccessExpressionSyntax node, ref TranspileContext context)
    {
        if (node.Name.Identifier.Text != "Length")
        {
            return false;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(node.Expression).Symbol;
        if (symbol is ILocalSymbol localSym)
        {
            if (localSym.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not VariableDeclaratorSyntax decl)
            {
                return false;
            }

            if (decl.Initializer == null)
            {
                return false;
            }

            if (ArraySyntaxExtractor.TryGetArrayInitialization(decl.Initializer.Value, out var count, out var sizeExpr,
                    out _))
            {
                if (count.HasValue)
                {
                    context.Writer.Append(count.Value);
                    return true;
                }

                if (sizeExpr != null)
                {
                    context.Visitor.Visit(sizeExpr);
                    return true;
                }
            }
        }

        return false;
    }
}
