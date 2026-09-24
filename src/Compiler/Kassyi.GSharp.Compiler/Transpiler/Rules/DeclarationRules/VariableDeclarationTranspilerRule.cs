using Kassyi.GSharp.Compiler.Transpiler.Helpers;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.DeclarationRules;

/// <summary>
/// Transpiles C# variable declaration syntax nodes into target dialect variable definitions,
/// supporting type inference (<c>auto</c>), pointer declarations, stack allocations, and multiple declarators.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and safe for concurrent execution across threads.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public class VariableDeclarationTranspilerRule : SyntaxNodeTranspilerRuleBase<VariableDeclarationSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="VariableDeclarationTranspilerRule"/>.
    /// </summary>
    public static readonly VariableDeclarationTranspilerRule Instance = new();

    private VariableDeclarationTranspilerRule()
    {
    }

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

    /// <inheritdoc />
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
            TranspileCombinedVariableDeclarations(node.Variables, mapping.TypeSymbol, mapping.IsVar, mapping.IsPointer,
                ref context);
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

            DeclarationTranspilerHelper.WriteDeclaratorInitializer(v, ref context);

            if (i >= variables.Count - 1)
            {
                continue;
            }

            context.Writer.AppendSemiColonLine();
        }
    }

    private static bool TryTranspileStackArrayDeclaration(VariableDeclaratorSyntax variable, ITypeSymbol? typeSymbol,
        ref TranspileContext context)
    {
        if (variable.Initializer == null ||
            !InitializationSyntaxExtractor.TryGetArrayElementType(typeSymbol, out var elemType, out var isConst))
        {
            return false;
        }

        if (!InitializationSyntaxExtractor.TryGetArrayInitialization(variable.Initializer.Value, out var count,
                out var sizeExpr, out var initializerNode))
        {
            return false;
        }

        if (initializerNode != null && count.HasValue)
        {
            WriteArrayWithInitializer(variable, elemType, isConst, count.Value, initializerNode, ref context);
            return true;
        }

        return TryWriteFixedSizeArray(variable, elemType, isConst, sizeExpr, "dynamic array size", ref context);
    }

    private static void WriteArrayDeclarationHeader(ITypeSymbol elemType, string identifier, bool isConst,
        ref TranspileContext context)
    {
        if (isConst)
        {
            context.Writer.Append("const ");
        }

        context.AppendType(elemType);
        context.Writer.Append($" {identifier}");
    }

    private static void WriteArrayWithInitializer(VariableDeclaratorSyntax variable, ITypeSymbol elemType, bool isConst,
        int count, SyntaxNode initializerNode, ref TranspileContext context)
    {
        WriteArrayDeclarationHeader(elemType, variable.Identifier.Text, isConst, ref context);
        using (context.Writer.Brackets())
        {
            context.Writer.Append(count);
        }

        context.Writer.AppendOperator("=");
        context.Visitor.Visit(initializerNode);
    }

    private static bool TryWriteFixedSizeArray(VariableDeclaratorSyntax variable, ITypeSymbol elemType, bool isConst,
        ExpressionSyntax? sizeExpr, string errorLabel, ref TranspileContext context)
    {
        if (sizeExpr == null)
        {
            return false;
        }

        var constVal = context.SemanticModel.GetConstantValue(sizeExpr);
        if (!constVal.HasValue)
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(
                GSharpDiagnostics.UnsupportedSyntax,
                sizeExpr.GetLocation(),
                errorLabel));
            return true;
        }

        WriteArrayDeclarationHeader(elemType, variable.Identifier.Text, isConst, ref context);
        using (context.Writer.Brackets())
        {
            context.Visitor.Visit(sizeExpr);
        }

        return true;
    }

    private static void TranspileCombinedVariableDeclarations(SeparatedSyntaxList<VariableDeclaratorSyntax> variables,
        ITypeSymbol? typeSymbol, bool isVar, bool isPointer, ref TranspileContext context) =>
        DeclarationTranspilerHelper.TranspileCombinedDeclarations(variables, typeSymbol, isVar, isPointer, ref context);
}
