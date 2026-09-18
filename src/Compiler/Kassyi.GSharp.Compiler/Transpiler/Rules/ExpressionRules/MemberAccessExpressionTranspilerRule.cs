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
        var symbol = context.SemanticModel.GetSymbolInfo(node).Symbol
                     ?? (node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node
                         ? context.SemanticModel.GetSymbolInfo(invocation).Symbol
                         : null);

        var memberExprSymbol = context.SemanticModel.GetSymbolInfo(node.Expression).Symbol;

        var handled = (symbol, memberExprSymbol) switch
        {
            ({ } s, _) when TryTranspileIntrinsic(s, ref context) => true,
            ({ } s, _) when TryEnsureNotProhibitedApi(s, node, ref context) => true,
            (IFieldSymbol { IsConst: true, HasConstantValue: true } field, _) => appendConstant(field, ref context),
            (_, { } exprSym) when TryTranspileEnumAccess(exprSym, node, ref context) => true,
            (_, ITypeSymbol) => appendGlobalStatic(node, ref context),
            _ when TryTranspileStackArrayLength(node, ref context) => true,
            _ => false
        };

        if (!handled)
        {
            TranspileMemberAccess(node, ref context);
        }

        static bool appendConstant(IFieldSymbol field, ref TranspileContext ctx)
        {
            ctx.Writer.AppendConstantField(field);
            return true;
        }

        static bool appendGlobalStatic(MemberAccessExpressionSyntax n, ref TranspileContext ctx)
        {
            // Skip class name for global static methods
            ctx.Writer.Append(n.Name.Identifier.Text);
            return true;
        }
    }

    private static bool TryTranspileIntrinsic(ISymbol symbol, ref TranspileContext context)
    {
        if (!context.Dialect.TryMapIntrinsic(symbol, out var mappedName))
        {
            return false;
        }

        context.Writer.Append(mappedName);
        return true;
    }

    internal static bool TryEnsureNotProhibitedApi(ISymbol symbol, SyntaxNode node,
        ref TranspileContext context)
    {
        if (symbol.ContainingType == null)
        {
            return false;
        }

        if (context.Dialect.TryMapIntrinsic(symbol, out _) || (symbol is IMethodSymbol ms &&
                                                               context.BclIntrinsicMap.ContainsKey(
                                                                   ms.OriginalDefinition)))
        {
            return false;
        }

        var typeName = symbol.ContainingType.Name;
        var ns = symbol.ContainingType.ContainingNamespace;

        var isProhibited = typeName switch
        {
            TypeConstants.MathType or TypeConstants.MathFType => ns.IsSystemNamespace(),
            TypeConstants.InterlockedType => ns.IsSystemThreading(),
            TypeConstants.BitOperationsType => ns.IsSystemNumerics(),
            _ => false
        };

        if (isProhibited)
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(
                GSharpDiagnostics.UnsupportedSyntax,
                node.GetLocation(),
                $"{typeName}.{symbol.Name}"));
            context.Writer.Append($"__gsharp_unsupported_{symbol.Name}");
            return true;
        }

        return false;
    }

    private static bool TryTranspileEnumAccess(ISymbol? memberExprSymbol, MemberAccessExpressionSyntax node,
        ref TranspileContext context)
    {
        if (memberExprSymbol is not ITypeSymbol { TypeKind: TypeKind.Enum })
        {
            return false;
        }

        context.Visitor.Visit(node.Expression);
        context.Writer.Append($"::{node.Name.Identifier.Text}");
        return true;
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
            else
            {
                switch (memberName)
                {
                    case "Length" when typeInfo.Type.HasAttribute(KnownAttributes.InlineArray):
                        context.Writer.Append("size()");
                        return;
                    case "Length":
                    case "Count":
                        memberName = "length";
                        break;
                }
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

        return symbol switch
        {
            ILocalSymbol { DeclaringSyntaxReferences: { Length: > 0 } refs }
                when refs[0].GetSyntax() is VariableDeclaratorSyntax { Initializer: { } initializer } &&
                     ArraySyntaxExtractor.TryGetArrayInitialization(initializer.Value, out var count, out var sizeExpr,
                         out _) =>
                (count, sizeExpr) switch
                {
                    ({ } c, _) => appendCount(c, ref context),
                    (_, { } expr) => visitSizeExpr(expr, ref context),
                    _ => false
                },
            _ => false
        };

        static bool appendCount(int c, ref TranspileContext ctx)
        {
            ctx.Writer.Append(c);
            return true;
        }

        static bool visitSizeExpr(SyntaxNode expr, ref TranspileContext ctx)
        {
            ctx.Visitor.Visit(expr);
            return true;
        }
    }
}
