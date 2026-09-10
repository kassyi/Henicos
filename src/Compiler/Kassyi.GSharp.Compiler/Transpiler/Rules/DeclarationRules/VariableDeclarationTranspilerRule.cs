namespace Kassyi.GSharp.Compiler.Transpiler.Rules.DeclarationRules;

/// <summary>
/// Strategy rule for transpiling variable declarations, including stack allocations.
/// </summary>
public class VariableDeclarationTranspilerRule : SyntaxNodeTranspilerRuleBase<VariableDeclarationSyntax>
{
    public static readonly VariableDeclarationTranspilerRule Instance = new();

    private VariableDeclarationTranspilerRule() { }

    private readonly struct VariableMapping(
        ITypeSymbol? typeSymbol,
        bool isVar,
        bool isPointer,
        bool shouldSplit)
    {
        public ITypeSymbol? TypeSymbol { get; } = typeSymbol;
        public bool IsVar { get; } = isVar;
        public bool IsPointer { get; } = isPointer;
        public bool ShouldSplit { get; } = shouldSplit;
    }

    public override void Transpile(VariableDeclarationSyntax node, ref TranspileContext context)
    {
        var typeSymbol = context.SemanticModel.GetTypeInfo(node.Type).Type;
        ValidateAndTrackVariableType(typeSymbol, node.Type, ref context);

        var mapping = AnalyzeVariableMapping(node, typeSymbol);

        if (mapping.ShouldSplit)
        {
            TranspileSplitVariableDeclarations(node.Variables, mapping.TypeSymbol, mapping.IsVar, ref context);
        }
        else
        {
            TranspileCombinedVariableDeclarations(node.Variables, mapping.TypeSymbol, mapping.IsVar, mapping.IsPointer, ref context);
        }
    }

    private static VariableMapping AnalyzeVariableMapping(VariableDeclarationSyntax node, ITypeSymbol? typeSymbol)
    {
        var isVar = node.Type.IsVar;
        var isPointer = typeSymbol?.TypeKind == TypeKind.Pointer;
        var shouldSplit = node.Parent is LocalDeclarationStatementSyntax or FixedStatementSyntax;

        return new VariableMapping(typeSymbol, isVar, isPointer, shouldSplit);
    }

    private static void ValidateAndTrackVariableType(ITypeSymbol? typeSymbol, TypeSyntax typeSyntax,
        ref TranspileContext context)
    {
        if (typeSymbol is { SpecialType: SpecialType.System_String })
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.StringNotSupported,
                typeSyntax.GetLocation()));
        }

        StructTranspiler.RegisterUsedStructIfApplicable(typeSymbol, ref context);
    }

    private static void TranspileSplitVariableDeclarations(SeparatedSyntaxList<VariableDeclaratorSyntax> variables,
        ITypeSymbol? typeSymbol, bool isVar, ref TranspileContext context)
    {
        for (var i = 0; i < variables.Count; i++)
        {
            var v = variables[i];

            if (TryTranspileStackArrayDeclaration(v, typeSymbol, ref context))
            {
                if (i < variables.Count - 1)
                {
                    context.Writer.AppendSemiColonLine();
                }
                continue;
            }

            if (isVar)
            {
                context.Writer.Append($"auto {v.Identifier.Text}");
            }
            else
            {
                context.AppendType(typeSymbol);
                context.Writer.Append($" {v.Identifier.Text}");
            }
            WriteDeclaratorInitializer(v, ref context);

            if (i >= variables.Count - 1)
            {
                continue;
            }

            context.Writer.AppendSemiColonLine();
        }
    }

    private static bool TryTranspileStackArrayDeclaration(VariableDeclaratorSyntax variable, ITypeSymbol? typeSymbol, ref TranspileContext context)
    {
        if (variable.Initializer == null)
        {
            return false;
        }

        var initVal = variable.Initializer.Value;

        ITypeSymbol? elemType = null;
        var isConst = false;

        if (typeSymbol is INamedTypeSymbol { TypeArguments.Length: 1, Name: "Span" or "ReadOnlySpan" } named)
        {
            elemType = named.TypeArguments[0];
            isConst = named.Name == "ReadOnlySpan";
        }
        else if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            elemType = arrayType.ElementType;
        }

        if (elemType == null)
        {
            return false;
        }

        // Case 1: CollectionExpression [10, 20, 30]
        if (initVal is CollectionExpressionSyntax collectionExpr)
        {
            if (isConst)
            {
                context.Writer.Append("const ");
            }

            context.AppendType(elemType);
            context.Writer.Append($" {variable.Identifier.Text}");
            using (context.Writer.Brackets())
            {
                context.Writer.Append(collectionExpr.Elements.Count);
            }
            context.Writer.AppendOperator("=");
            context.Visitor.Visit(collectionExpr);
            return true;
        }

        // Case 2: StackAllocArrayCreationExpression stackalloc int[4] or stackalloc int[] { 1, 2, 3 }
        if (initVal is StackAllocArrayCreationExpressionSyntax stackAlloc)
        {
            if (stackAlloc.Initializer != null)
            {
                if (isConst)
                {
                    context.Writer.Append("const ");
                }

                context.AppendType(elemType);
                context.Writer.Append($" {variable.Identifier.Text}");
                using (context.Writer.Brackets())
                {
                    context.Writer.Append(stackAlloc.Initializer.Expressions.Count);
                }
                context.Writer.AppendOperator("=");
                context.Visitor.Visit(stackAlloc.Initializer);
                return true;
            }

            if (stackAlloc.Type is ArrayTypeSyntax { RankSpecifiers.Count: > 0 } arrayTypeSyntax)
            {
                var sizeExpr = arrayTypeSyntax.RankSpecifiers[0].Sizes.FirstOrDefault();
                if (sizeExpr != null)
                {
                    var constVal = context.SemanticModel.GetConstantValue(sizeExpr);
                    if (!constVal.HasValue)
                    {
                        context.ReportDiagnostic?.Invoke(Diagnostic.Create(
                            GSharpDiagnostics.UnsupportedSyntax,
                            sizeExpr.GetLocation(),
                            "dynamic stackalloc size"));
                        return true;
                    }

                    context.AppendType(elemType);
                    context.Writer.Append($" {variable.Identifier.Text}");
                    using (context.Writer.Brackets())
                    {
                        context.Visitor.Visit(sizeExpr);
                    }
                    return true;
                }
            }
        }

        // Case 3: ImplicitStackAllocArrayCreationExpression stackalloc[] { 1, 2, 3 }
        if (initVal is ImplicitStackAllocArrayCreationExpressionSyntax implStackAlloc)
        {
            if (isConst)
            {
                context.Writer.Append("const ");
            }

            context.AppendType(elemType);
            context.Writer.Append($" {variable.Identifier.Text}");
            using (context.Writer.Brackets())
            {
                context.Writer.Append(implStackAlloc.Initializer.Expressions.Count);
            }
            context.Writer.Append(" = ");
            context.Visitor.Visit(implStackAlloc.Initializer);
            return true;
        }

        // Case 4: ArrayCreationExpression new int[] { 1, 2, 3 } or new int[4]
        if (initVal is ArrayCreationExpressionSyntax arrayCreation)
        {
            if (arrayCreation.Initializer != null)
            {
                if (isConst)
                {
                    context.Writer.Append("const ");
                }

                context.AppendType(elemType);
                context.Writer.Append($" {variable.Identifier.Text}");
                using (context.Writer.Brackets())
                {
                    context.Writer.Append(arrayCreation.Initializer.Expressions.Count);
                }
                context.Writer.AppendOperator("=");
                context.Visitor.Visit(arrayCreation.Initializer);
                return true;
            }

            if (arrayCreation.Type.RankSpecifiers.Count > 0)
            {
                var sizeExpr = arrayCreation.Type.RankSpecifiers[0].Sizes.FirstOrDefault();
                if (sizeExpr != null)
                {
                    var constVal = context.SemanticModel.GetConstantValue(sizeExpr);
                    if (!constVal.HasValue)
                    {
                        context.ReportDiagnostic?.Invoke(Diagnostic.Create(
                            GSharpDiagnostics.UnsupportedSyntax,
                            sizeExpr.GetLocation(),
                            "dynamic array size"));
                        return true;
                    }

                    context.AppendType(elemType);
                    context.Writer.Append($" {variable.Identifier.Text}");
                    using (context.Writer.Brackets())
                    {
                        context.Visitor.Visit(sizeExpr);
                    }
                    return true;
                }
            }
        }

        return false;
    }

    private static void TranspileCombinedVariableDeclarations(SeparatedSyntaxList<VariableDeclaratorSyntax> variables,
        ITypeSymbol? typeSymbol, bool isVar, bool isPointer, ref TranspileContext context)
    {
        if (isVar)
        {
            context.Writer.Append("auto");
        }
        else
        {
            if (isPointer && typeSymbol is IPointerTypeSymbol pointerType)
            {
                context.AppendType(pointerType.PointedAtType);
            }
            else
            {
                context.AppendType(typeSymbol);
            }
        }
        context.Writer.Append(" ");

        for (var i = 0; i < variables.Count; i++)
        {
            var v = variables[i];
            if (isPointer)
            {
                context.Writer.Append($"*{v.Identifier.Text}");
            }
            else
            {
                context.Writer.Append(v.Identifier.Text);
            }

            WriteDeclaratorInitializer(v, ref context);

            if (i < variables.Count - 1)
            {
                context.Writer.AppendComma();
            }
        }
    }

    private static void WriteDeclaratorInitializer(VariableDeclaratorSyntax variable, ref TranspileContext context)
    {
        if (variable.Initializer != null)
        {
            if (context.SkipLocalsInit)
            {
                var val = variable.Initializer.Value;
                if (val is ImplicitObjectCreationExpressionSyntax or ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0 } or DefaultExpressionSyntax ||
                    val is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.DefaultLiteralExpression))
                {
                    return; // Skip zero-initialization
                }
            }

            context.Writer.Append(" = ");
            context.Visitor.Visit(variable.Initializer.Value);
        }
    }
}
