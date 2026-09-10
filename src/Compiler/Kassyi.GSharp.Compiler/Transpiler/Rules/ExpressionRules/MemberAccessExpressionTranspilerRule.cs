namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

public sealed class MemberAccessExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<MemberAccessExpressionSyntax>
{
    public static readonly MemberAccessExpressionTranspilerRule Instance = new();

    private MemberAccessExpressionTranspilerRule() { }

    public override void Transpile(MemberAccessExpressionSyntax node, ref TranspileContext context)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(node).Symbol;

        if (symbol == null && node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node)
        {
            symbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol;
        }

        if (symbol != null)
        {
            if (context.Dialect.TryMapIntrinsic(symbol, out var mappedName))
            {
                context.Writer.Append(mappedName);
                return;
            }
        }

        if (symbol is
            {
                ContainingType.Name: TypeConstants.MathType or TypeConstants.MathFType
                or TypeConstants.InterlockedType
            } &&
            (symbol.ContainingNamespace.IsSystemNamespace() || symbol.ContainingNamespace.IsSystemThreading()))
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(
                GSharpDiagnostics.UnsupportedSyntax,
                node.GetLocation(),
                $"{symbol.ContainingType.Name}.{symbol.Name}"));
            context.Writer.Append($"__gsharp_unsupported_{symbol.Name}");
            return;
        }

        if (symbol is IFieldSymbol { IsConst: true, HasConstantValue: true } field)
        {
            context.Writer.AppendConstantField(field);
            return;
        }

        var memberExprSymbol = context.SemanticModel.GetSymbolInfo(node.Expression).Symbol;
        if (memberExprSymbol is ITypeSymbol typeSym)
        {
            if (typeSym.TypeKind == TypeKind.Enum)
            {
                context.Visitor.Visit(node.Expression);
                context.Writer.Append($"::{node.Name.Identifier.Text}");
                return;
            }

            // Skip class name for global static methods
            context.Writer.Append(node.Name.Identifier.Text);
            return;
        }

        if (TryTranspileStackArrayLength(node, ref context))
        {
            return;
        }

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
            var decl = localSym.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as VariableDeclaratorSyntax;
            if (decl?.Initializer?.Value is CollectionExpressionSyntax collExpr)
            {
                context.Writer.Append(collExpr.Elements.Count);
                return true;
            }

            if (decl?.Initializer?.Value is StackAllocArrayCreationExpressionSyntax stackAlloc)
            {
                if (stackAlloc.Initializer != null)
                {
                    context.Writer.Append(stackAlloc.Initializer.Expressions.Count);
                    return true;
                }

                if (stackAlloc.Type is ArrayTypeSyntax { RankSpecifiers.Count: > 0 } ats)
                {
                    var sizeSyntax = ats.RankSpecifiers[0].Sizes.FirstOrDefault();
                    if (sizeSyntax != null)
                    {
                        context.Visitor.Visit(sizeSyntax);
                        return true;
                    }
                }
            }

            if (decl?.Initializer?.Value is ImplicitStackAllocArrayCreationExpressionSyntax implStackAlloc)
            {
                context.Writer.Append(implStackAlloc.Initializer.Expressions.Count);
                return true;
            }

            if (decl?.Initializer?.Value is ArrayCreationExpressionSyntax arrCreation)
            {
                if (arrCreation.Initializer != null)
                {
                    context.Writer.Append(arrCreation.Initializer.Expressions.Count);
                    return true;
                }

                if (arrCreation.Type.RankSpecifiers.Count > 0)
                {
                    var sizeSyntax = arrCreation.Type.RankSpecifiers[0].Sizes.FirstOrDefault();
                    if (sizeSyntax != null)
                    {
                        context.Visitor.Visit(sizeSyntax);
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
