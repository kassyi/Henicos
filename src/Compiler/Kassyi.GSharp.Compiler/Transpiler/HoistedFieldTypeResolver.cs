namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Resolves the static type of a hoisted state-machine context field for both the managed C# and the C++ CPU dialects.
/// </summary>
internal static class HoistedFieldTypeResolver
{
    /// <summary>
    /// Returns the type of the hoisted field that <paramref name="field"/> declares, trying the declared symbol, the
    /// initializer type (the type it converts to for an argument temporary,
    /// <see cref="LoweringAnnotations.ArgumentTempAnnotationKind"/>), a predefined declared type and the <c>foreach</c>
    /// index annotation in this order.
    /// </summary>
    /// <exception cref="InvalidOperationException">No lookup produced a usable type (an internal compiler error).</exception>
    /// <remarks>
    /// The first lookup takes the declared symbol: a local, or a parameter when an expansion turned the parameter into a
    /// local whose declarator carries the parameter's span.
    /// </remarks>
    public static ITypeSymbol Resolve(VariableDeclaratorSyntax field, SemanticModel semanticModel, string kernelName)
    {
        var declaredSymbol = semanticModel.GetDeclaredSymbol<ISymbol>(field);
        var type1 = declaredSymbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            _ => null
        };
        var type2 = field.Initializer?.Value is { } initializer
            ? GetInitializerType(field, initializer, semanticModel)
            : null;

        if (IsAcceptable(type1))
        {
            return type1;
        }

        if (IsAcceptable(type2))
        {
            return type2;
        }

        if (field.Parent is VariableDeclarationSyntax { Type: PredefinedTypeSyntax predefinedType } &&
            TryGetSpecialType(predefinedType.Keyword.Kind()) is { } specialType)
        {
            return semanticModel.Compilation.GetSpecialType(specialType);
        }

        if (field.HasAnnotations(LoweringAnnotations.ForEachIndexAnnotationKind))
        {
            return semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
        }

        throw new InvalidOperationException(BuildErrorMessage(field, semanticModel, kernelName, declaredSymbol, type1, type2));
    }

    // [WHY] An argument temporary holds the value the callee receives: 'Half / 2' passed to a uint parameter is a uint
    // there, though its own type is int, and an int field would not convert implicitly the way the constant did (#788)
    private static ITypeSymbol? GetInitializerType(VariableDeclaratorSyntax field, ExpressionSyntax initializer,
        SemanticModel semanticModel) =>
        field.HasAnnotations(LoweringAnnotations.ArgumentTempAnnotationKind) &&
        semanticModel.GetConvertedTypeSymbol(initializer) is { } convertedType
            ? convertedType
            : semanticModel.GetTypeSymbol(initializer);

    private static SpecialType? TryGetSpecialType(SyntaxKind kind) => kind switch
    {
        SyntaxKind.BoolKeyword => SpecialType.System_Boolean,
        SyntaxKind.ByteKeyword => SpecialType.System_Byte,
        SyntaxKind.SByteKeyword => SpecialType.System_SByte,
        SyntaxKind.ShortKeyword => SpecialType.System_Int16,
        SyntaxKind.UShortKeyword => SpecialType.System_UInt16,
        SyntaxKind.IntKeyword => SpecialType.System_Int32,
        SyntaxKind.UIntKeyword => SpecialType.System_UInt32,
        SyntaxKind.LongKeyword => SpecialType.System_Int64,
        SyntaxKind.ULongKeyword => SpecialType.System_UInt64,
        SyntaxKind.FloatKeyword => SpecialType.System_Single,
        SyntaxKind.DoubleKeyword => SpecialType.System_Double,
        SyntaxKind.DecimalKeyword => SpecialType.System_Decimal,
        SyntaxKind.CharKeyword => SpecialType.System_Char,
        _ => null
    };

#pragma warning disable RS0030 // Source and symbol text for a human-readable internal-error report only
    private static string BuildErrorMessage(
        VariableDeclaratorSyntax field,
        SemanticModel semanticModel,
        string kernelName,
        ISymbol? declaredSymbol,
        ITypeSymbol? type1,
        ITypeSymbol? type2)
    {
        var fieldName = field.Identifier.ValueText;
        var origLocation = semanticModel.GetOriginalLocation(field);

        string locationText;
        if (origLocation != null && origLocation.IsInSource)
        {
            var lineSpan = origLocation.GetLineSpan();
            locationText = $"{lineSpan.Path}({lineSpan.StartLinePosition.Line + 1},{lineSpan.StartLinePosition.Character + 1})";
        }
        else
        {
            locationText = "not mapped (the node was synthesized by lowering)";
        }

        var declaredTypeText = (field.Parent as VariableDeclarationSyntax)?.Type.ToFullString().Trim() ?? "";
        var initializerText = field.Initializer?.Value.ToFullString().Trim() ?? "<none>";
        var lookup1Text = declaredSymbol?.ToDisplayString() ?? "not found";

        string lookup2Text;
        if (type2 == null)
        {
            lookup2Text = "<none>";
        }
        else if (type2.TypeKind == TypeKind.Error)
        {
            lookup2Text = $"error type '{type2.ToDisplayString()}'";
        }
        else
        {
            lookup2Text = type2.ToDisplayString();
        }

        var reason = DescribeRejection(type1) ?? DescribeRejection(type2) ?? "no lookup produced a type";

        return
            $"G# internal compiler error: cannot determine the type of the hoisted state-machine field '{fieldName}' in kernel '{kernelName}'.\n" +
            $"Original location: {locationText}\n" +
            $"Declared type: {declaredTypeText}\n" +
            $"Initializer: {initializerText}\n" +
            $"Lookup 1, declared local symbol: {lookup1Text}\n" +
            $"Lookup 2, initializer type: {lookup2Text}\n" +
            $"Reason: {reason}\n" +
            "Please report this at https://github.com/kassyi/Henicos/issues and include the kernel source and this message.";
    }
#pragma warning restore RS0030

    private static bool IsAcceptable([NotNullWhen(true)] ITypeSymbol? type) =>
        type is { TypeKind: not TypeKind.Error } && FindTypeParameter(type) == null;

    private static string? DescribeRejection(ITypeSymbol? type)
    {
        if (type == null)
        {
            return null;
        }

        if (type.TypeKind == TypeKind.Error)
        {
            return "the type is an error type";
        }

        return FindTypeParameter(type) is { } typeParameter
            ? $"the type contains the type parameter '{typeParameter.Name}'; generic kernels are not supported"
            : null;
    }

    private static ITypeParameterSymbol? FindTypeParameter(ITypeSymbol type)
    {
        switch (type)
        {
            case ITypeParameterSymbol typeParameter:
                return typeParameter;
            case IArrayTypeSymbol arrayType:
                return FindTypeParameter(arrayType.ElementType);
            case IPointerTypeSymbol pointerType:
                return FindTypeParameter(pointerType.PointedAtType);
            case INamedTypeSymbol namedType:
                var fromTypeArguments = namedType.TypeArguments
                    .AsStackEnumerable()
                    .Select(FindTypeParameter)
                    .FirstOrDefault(static found => found is not null);
                if (fromTypeArguments is not null)
                {
                    return fromTypeArguments;
                }

                return namedType.ContainingType != null ? FindTypeParameter(namedType.ContainingType) : null;
            default:
                return null;
        }
    }
}
