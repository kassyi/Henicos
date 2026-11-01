namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Emits calls to user-defined struct conversion operators (<c>implicit operator</c> and <c>explicit operator</c>),
/// which <see cref="StructTranspiler"/> emits as free device functions (<c>__gsharp_fn_&lt;Type&gt;__op_Implicit</c> /
/// <c>__op_Explicit</c>).
/// </summary>
internal static class UserDefinedConversionHelper
{
    /// <summary>
    /// When <paramref name="expression"/> has an implicit user-defined struct conversion in its context, emits the
    /// conversion function call around <paramref name="expression"/> and returns <see langword="true"/>.
    /// </summary>
    public static bool TryAppendImplicitConversion(ExpressionSyntax expression, ref TranspileContext context)
    {
        if (expression is ParenthesizedExpressionSyntax ||
            context.Emitter.SuppressImplicitConversionNode == expression)
        {
            return false;
        }

        var model = context.Query.SemanticModel;
        if (model.GetSafeConversion(expression) is not
            {
                IsImplicit: true,
                IsUserDefined: true,
                MethodSymbol: { } conversionMethod
            })
        {
            return false;
        }

        // For a CastExpressionSyntax, GetConversion returns the cast's own conversion unless an outer implicit
        // conversion sits above the cast; CastExpressionTranspilerRule handles the cast's own conversion.
        if (expression is CastExpressionSyntax cast &&
            model.GetSymbol(cast) is IMethodSymbol castMethod &&
            conversionMethod.IsEqualTo(castMethod))
        {
            return false;
        }

        conversionMethod = SubstituteStructConversion(conversionMethod, ref context);
        if (!StructTranspiler.IsEmittedStructConversion(conversionMethod, model.Compilation))
        {
            return false;
        }

        var previousSuppressed = context.Emitter.SuppressImplicitConversionNode;
        context.Emitter.SuppressImplicitConversionNode = expression;
        try
        {
            AppendConversionCall(
                conversionMethod,
                expression,
                model.GetConvertedTypeSymbol(expression),
                ref context);
            return true;
        }
        finally
        {
            context.Emitter.SuppressImplicitConversionNode = previousSuppressed;
        }
    }

    /// <summary>
    /// Emits a call to <paramref name="conversion"/> on <paramref name="operand"/>, wrapping with a post-conversion to
    /// <paramref name="targetType"/> when the surrounding conversion includes a standard conversion after the
    /// user-defined operator.
    /// </summary>
    public static void AppendConversionCall(
        IMethodSymbol conversion,
        ExpressionSyntax operand,
        ITypeSymbol? targetType,
        ref TranspileContext context)
    {
        var compilation = context.Query.SemanticModel.Compilation;
        conversion = SubstituteStructConversion(conversion, ref context);

        StructTranspiler.RegisterUsedStructIfApplicable(conversion.ContainingType, ref context);
        var returnType = StructTranspiler.SubstituteTypeParameters(
            conversion.ReturnType,
            context.Emitter.CurrentStructType,
            compilation);
        StructTranspiler.RegisterUsedStructIfApplicable(returnType, ref context);

        ITypeSymbol? paramType = null;
        var isByRefParam = false;
        if (conversion.Parameters.Length > 0)
        {
            paramType = StructTranspiler.SubstituteTypeParameters(
                conversion.Parameters[0].Type,
                context.Emitter.CurrentStructType,
                compilation);
            StructTranspiler.RegisterUsedStructIfApplicable(paramType, ref context);
            isByRefParam = conversion.Parameters[0].RefKind is RefKind.In or RefKind.RefReadOnlyParameter;
        }

        targetType = StructTranspiler.SubstituteTypeParameters(
            targetType,
            context.Emitter.CurrentStructType,
            compilation);

        var needsOuterCast = targetType is not null and not IErrorTypeSymbol &&
                             returnType != null &&
                             !targetType.IsEqualTo(returnType);
        if (needsOuterCast && targetType != null)
        {
            if (targetType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
            {
                context.Emitter.Dialect.AppendType(ref context, targetType);
            }
            else
            {
                context.Emitter.Writer.Append("static_cast");
                using (context.Emitter.Writer.InlineScope("<", ">"))
                {
                    context.Emitter.Dialect.AppendType(ref context, targetType);
                }
            }

            using (context.Emitter.Writer.Parentheses())
            {
                AppendDirectCall(conversion, operand, paramType, isByRefParam, ref context);
            }

            return;
        }

        AppendDirectCall(conversion, operand, paramType, isByRefParam, ref context);
    }

    private static void AppendDirectCall(
        IMethodSymbol conversion,
        ExpressionSyntax operand,
        ITypeSymbol? paramType,
        bool isByRefParam,
        ref TranspileContext context)
    {
        context.Emitter.Writer.Append(GlobalCppNames.GetFunctionName(conversion));
        using (context.Emitter.Writer.Parentheses())
        {
            if (isByRefParam && paramType != null)
            {
                context.Emitter.Writer.Append(GSharpInternalNames.Prelude.TempAddress);
                using (context.Emitter.Writer.InlineScope("<", ">"))
                {
                    context.Emitter.Dialect.AppendType(ref context, paramType);
                }

                using (context.Emitter.Writer.Parentheses())
                {
                    context.Visitor.Visit(operand);
                }
            }
            else
            {
                context.Visitor.Visit(operand);
            }
        }
    }

    private static IMethodSymbol SubstituteStructConversion(IMethodSymbol conversion, ref TranspileContext context)
    {
        if (context.Emitter.CurrentStructType is { IsGenericType: true } currentStruct &&
            conversion.ContainingType is { IsGenericType: true } containingType &&
            !containingType.IsEqualTo(currentStruct) &&
            containingType.OriginalDefinition.IsEqualTo(currentStruct.OriginalDefinition))
        {
            foreach (var member in currentStruct.GetMembers(conversion.Name))
            {
                if (member is IMethodSymbol { MethodKind: MethodKind.Conversion } candidate &&
                    candidate.OriginalDefinition.IsEqualTo(conversion.OriginalDefinition))
                {
                    return candidate;
                }
            }
        }

        return conversion;
    }
}
