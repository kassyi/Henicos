using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles explicit C# type cast expressions into C-style parenthesized casts in target dialect code.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class CastExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<CastExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="CastExpressionTranspilerRule"/>.
    /// </summary>
    public CastExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(CastExpressionSyntax node, ref TranspileContext context)
    {
        // An integer constant is written as its value, as the binary operators write theirs: (int)(3L << 65) is 6 (#722)
        var model = context.Query.SemanticModel;
        var operation = model.GetSafeOperation(node) is { } bound &&
                        bound.Syntax.RawKind == node.RawKind
            ? bound
            : null;
        if (IntegerSemanticsEmitter.TryAppendConstant(operation, node, ref context))
        {
            return;
        }

        var targetType = model.GetTypeSymbol(node.Type);
        var conversionMethod = operation is IConversionOperation
        {
            Conversion: { IsUserDefined: true, MethodSymbol: { } opMethod }
        }
            ? opMethod
            : model.GetSymbol(node) as IMethodSymbol;
        if (StructTranspiler.IsEmittedStructConversion(conversionMethod, model.Compilation) &&
            conversionMethod != null)
        {
            UserDefinedConversionHelper.AppendConversionCall(conversionMethod, node.Expression, targetType, ref context);
            return;
        }

        // A float or double converted to an integer saturates and NaN gives 0, as .NET converts it; C++ leaves an
        // out-of-range conversion undefined (#724). A type narrower than int is the int result truncated:
        // (unsigned char)__gsharp_to_i32(x). A cast synthesized by foreach lowering (#995) has no bound conversion
        // operation and resolves its source and target types from the expressions.
        var sourceType = operation is IConversionOperation convOp
            ? convOp.Operand.Type
            : GetSynthesizedSourceType(node, model);
        var sourceUnderlying = sourceType is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [var s]
        }
            ? s
            : sourceType;
        var targetUnderlying = targetType is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [var t]
        }
            ? t
            : targetType;
        var isLiftedNumeric = (sourceType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } ||
                               targetType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }) &&
                              sourceUnderlying != null && targetUnderlying != null;

        var helper = isLiftedNumeric
            ? IntegerSemantics.GetConversionHelper(sourceUnderlying, targetUnderlying, out var needsCast)
            : (operation is IConversionOperation conversion
                ? IntegerSemantics.GetConversionHelper(conversion, out needsCast)
                : IntegerSemantics.GetConversionHelper(sourceType, targetType, out needsCast));
        if (helper != null)
        {
            if (isLiftedNumeric)
            {
                if (sourceType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } &&
                    targetType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
                {
                    const string temp = GSharpInternalNames.Lowering.CoalesceTemp;
                    using (context.Emitter.Writer.ImmediatelyInvokedLambda())
                    {
                        context.Emitter.Writer.Append($"auto {temp} = ");
                        context.Visitor.Visit(node.Expression);
                        context.Emitter.Writer.AppendLine(";");

                        context.Emitter.Writer.Append($"return {temp}.HasValue ? ");
                        context.Emitter.Dialect.AppendType(ref context, targetType);
                        using (context.Emitter.Writer.Parentheses())
                        {
                            if (needsCast && targetUnderlying != null)
                            {
                                using (context.Emitter.Writer.Parentheses())
                                {
                                    context.Emitter.Dialect.AppendType(ref context, targetUnderlying);
                                }
                            }

                            context.Emitter.Writer.Append($"{helper}({temp}.Value)");
                        }

                        context.Emitter.Writer.Append(" : ");
                        context.Emitter.Dialect.AppendType(ref context, targetType);
                        context.Emitter.Writer.AppendLine("();");
                    }

                    return;
                }

                if (targetType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
                {
                    context.Emitter.Dialect.AppendType(ref context, targetType);
                    using (context.Emitter.Writer.Parentheses())
                    {
                        if (needsCast && targetUnderlying != null)
                        {
                            using (context.Emitter.Writer.Parentheses())
                            {
                                context.Emitter.Dialect.AppendType(ref context, targetUnderlying);
                            }
                        }

                        context.Emitter.Writer.Append($"{helper}(");
                        context.Visitor.Visit(node.Expression);
                        context.Emitter.Writer.Append(")");
                    }

                    return;
                }

                if (needsCast && targetType != null)
                {
                    using (context.Emitter.Writer.Parentheses())
                    {
                        context.Emitter.Dialect.AppendType(ref context, targetType);
                    }
                }

                context.Emitter.Writer.Append($"{helper}(");
                context.Visitor.Visit(node.Expression);
                context.Emitter.Writer.Append(".Value)");
                return;
            }

            if (needsCast)
            {
                using (context.Emitter.Writer.Parentheses())
                {
                    context.Emitter.Dialect.AppendType(ref context, targetType);
                }
            }

            IntegerSemanticsEmitter.AppendUnaryCall(helper, node.Expression, ref context);
            return;
        }

        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Dialect.AppendType(ref context, targetType);
        }

        context.Visitor.Visit(node.Expression);
    }

    private static ITypeSymbol? GetSynthesizedSourceType(CastExpressionSyntax node, SemanticModel model)
    {
        var expr = node.Expression;
        while (expr is ParenthesizedExpressionSyntax parenthesized)
        {
            expr = parenthesized.Expression;
        }

        return model.GetTypeSymbol(expr) ??
               (model.GetSafeOperation(node) is IForEachLoopOperation { Collection.Type: { } collectionType } &&
                collectionType.TryGetArrayLikeElementType(out var elementType)
                   ? elementType
                   : null);
    }
}
