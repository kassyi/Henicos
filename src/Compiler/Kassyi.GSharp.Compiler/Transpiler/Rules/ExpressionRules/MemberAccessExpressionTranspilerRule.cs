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

        if (symbol is null && node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node)
        {
            symbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol;
        }

        if (symbol is not null)
        {
            if (TryTranspileIntrinsic(symbol, ref context) ||
                TryEnsureNotProhibitedApi(symbol, node, ref context))
            {
                return;
            }

            if (symbol is IFieldSymbol { IsConst: true, HasConstantValue: true } field)
            {
                context.Writer.AppendConstantField(field);
                return;
            }
        }

        switch (context.SemanticModel.GetSymbolInfo(node.Expression).Symbol)
        {
            case ITypeSymbol { TypeKind: TypeKind.Enum }:
                context.Visitor.Visit(node.Expression);
                context.Writer.Append($"::{node.Name.Identifier.Text}");
                return;

            case ITypeSymbol:
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
        if (!context.Dialect.TryMapIntrinsic(symbol, out var mappedName))
        {
            return false;
        }

        context.Writer.Append(mappedName);
        return true;
    }

    private static bool TryEnsureNotProhibitedApi(ISymbol symbol, MemberAccessExpressionSyntax node,
        ref TranspileContext context)
    {
        if (symbol is
                not
                {
                    ContainingType.Name: TypeConstants.MathType or TypeConstants.MathFType
                    or TypeConstants.InterlockedType
                } ||
            (!symbol.ContainingNamespace.IsSystemNamespace() && !symbol.ContainingNamespace.IsSystemThreading()))
        {
            return false;
        }

        context.ReportDiagnostic?.Invoke(Diagnostic.Create(
            GSharpDiagnostics.UnsupportedSyntax,
            node.GetLocation(),
            $"{symbol.ContainingType.Name}.{symbol.Name}"));
        context.Writer.Append($"__gsharp_unsupported_{symbol.Name}");
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
        if (node is not { Name.Identifier.Text: "Length" })
        {
            return false;
        }

        if (context.SemanticModel.GetSymbolInfo(node.Expression).Symbol is not ILocalSymbol localSym ||
            localSym.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not VariableDeclaratorSyntax
            {
                Initializer.Value: { } initValue
            } ||
            !ArraySyntaxExtractor.TryGetArrayInitialization(initValue, out var count, out var sizeExpr, out _))
        {
            return false;
        }

        if (count is { } fixedCount)
        {
            context.Writer.Append(fixedCount);
            return true;
        }

        if (sizeExpr is null)
        {
            return false;
        }

        context.Visitor.Visit(sizeExpr);
        return true;
    }
}
