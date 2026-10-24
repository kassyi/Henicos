namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Resolves the static type of a hoisted state-machine context field for both the managed C# and the C++ CPU dialects.
/// </summary>
internal static class HoistedFieldTypeResolver
{
    /// <summary>
    /// Resolves a hoisted state-machine field that holds an iterator or enumerator value to the iterator
    /// <see cref="IMethodSymbol"/> that produces its struct.
    /// </summary>
    public static bool TryResolveIteratorField(
        VariableDeclaratorSyntax field,
        SemanticModel semanticModel,
        [NotNullWhen(true)] out IMethodSymbol? iteratorMethod)
    {
        if (field.Initializer?.Value is { } initValue &&
            semanticModel.GetOriginalNode(initValue) is ExpressionSyntax origInit &&
            IteratorTypeResolver.TryResolveIteratorSource(origInit, semanticModel, out _, out iteratorMethod))
        {
            return true;
        }

        if (semanticModel.GetOriginalNode(field) is ExpressionSyntax origField &&
            IteratorTypeResolver.TryResolveIteratorSource(origField, semanticModel, out _, out iteratorMethod))
        {
            return true;
        }

        if (semanticModel.GetDeclaredSymbol<ILocalSymbol>(field) is { } localSymbol &&
            localSymbol.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var origDeclarator) &&
            origDeclarator.Initializer?.Value is { } origDeclaratorInit &&
            IteratorTypeResolver.TryResolveIteratorSource(origDeclaratorInit, semanticModel, out _, out iteratorMethod))
        {
            return true;
        }

        iteratorMethod = null;
        return false;
    }

    public static ITypeSymbol Resolve(VariableDeclaratorSyntax field, SemanticModel semanticModel, string kernelName)
    {
        var declaredSymbol = semanticModel.GetDeclaredSymbol<ILocalSymbol>(field);
        var type1 = declaredSymbol?.Type;
        var type2 = field.Initializer != null ? semanticModel.GetTypeSymbol(field.Initializer.Value) : null;

        if (IsAcceptable(type1))
        {
            return type1;
        }

        if (IsAcceptable(type2))
        {
            return type2;
        }

        if (field.HasAnnotations(LoweringAnnotations.ForEachIndexAnnotationKind))
        {
            return semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
        }

        throw new InvalidOperationException(BuildErrorMessage(field, semanticModel, kernelName, declaredSymbol, type1, type2));
    }

#pragma warning disable RS0030 // Source and symbol text for a human-readable internal-error report only
    private static string BuildErrorMessage(
        VariableDeclaratorSyntax field,
        SemanticModel semanticModel,
        string kernelName,
        ILocalSymbol? declaredSymbol,
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
