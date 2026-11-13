namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Decides whether G# can add the generated body of a <c>[CudaImport]</c> method, the implementing part of a partial
/// method in a generated part of its containing types. <c>CudaImportAnalyzer</c> reports a declaration it cannot
/// (GSH0113), and <see cref="CudaImportGenerator"/> generates nothing for it, so both read the same decision.
/// </summary>
internal static class CudaImportDeclaration
{
    public const string NotPartialReason =
        "it is not a partial method declaration; declare it 'partial' without a body, and G# generates the body.";

    public const string ImplementedReason = "its implementing part is already written, and G# would add a second one.";

    public const string GenericMethodReason =
        "it is generic; a module has one kernel per entry point name, so declare one method per type argument.";

    public const string ReturnsValueReason =
        "it returns a value; a kernel returns nothing, so write the result through an array or a 'ref' or 'out' " +
        "parameter.";

    /// <summary>
    /// Gets why G# cannot generate the body of <paramref name="method"/>, as the end of the GSH0113 message, or
    /// <see langword="null"/> when it can.
    /// </summary>
    public static string? GetProblem(IMethodSymbol method)
    {
        if (!IsPartialDefinition(method))
        {
            return NotPartialReason;
        }

        // [WHY] The analyzer also sees the part this generator added; only a part the user wrote is a second body
        if (method.PartialImplementationPart is { } implementation &&
            !implementation.DeclaringSyntaxReferences
                .AsStackEnumerable()
                .All(static reference => GSharpGeneratedTrees.IsOwnOutput(reference.SyntaxTree)))
        {
            return ImplementedReason;
        }

        if (method.IsGenericMethod)
        {
            return GenericMethodReason;
        }

        if (!method.ReturnsVoid)
        {
            return ReturnsValueReason;
        }

        for (var type = method.ContainingType; type is not null; type = type.ContainingType)
        {
            if (type.IsFileLocal)
            {
                return $"its containing type '{type.Name}' is file-local, so the generated part, in another file, " +
                       "cannot add the body to it.";
            }

            if (!IsPartial(type))
            {
                return $"its containing type '{type.Name}' is not partial, so the generated part cannot add the body " +
                       "to it.";
            }
        }

        return null;
    }

    // A declaration with the 'partial' modifier and no body: the part that the generated body implements
    private static bool IsPartialDefinition(IMethodSymbol method) =>
        method.PartialDefinitionPart is null &&
        method.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(static reference => reference.GetSyntax() is MethodDeclarationSyntax
            {
                Body: null,
                ExpressionBody: null,
            } declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(static reference => reference.GetSyntax() is TypeDeclarationSyntax declaration &&
                declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
}
