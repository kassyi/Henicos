using Kassyi.GSharp.Compiler.Backends.Cpp;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Supplies syntax-transpiler type resolution, struct registration, and lazy diagnostic reporting to
/// <see cref="CppTypeWriter"/>.
/// </summary>
internal readonly struct TranspileTypeServices(
    Reporter reporter,
    SyntaxNode? currentNode,
    INamedTypeSymbol? currentStructType,
    Compilation compilation,
    HashSet<INamedTypeSymbol> usedStructs) : ICppTypeServices
{
    /// <summary>
    /// Returns the emitted C++ name when <paramref name="type"/> is an emitted user struct, or <see langword="null"/>.
    /// </summary>
    internal static string? ResolveUserTypeName(INamedTypeSymbol type) =>
        EmittedUserTypes.IsEmittedUserType(type) ? GlobalCppNames.GetTypeName(type) : null;

    /// <inheritdoc />
    public ITypeSymbol? SubstituteTypeParameters(ITypeSymbol? typeSymbol) =>
        StructTypeSubstitution.SubstituteTypeParameters(typeSymbol, currentStructType, compilation);

    /// <inheritdoc />
    public void RegisterUsedStruct(ITypeSymbol typeSymbol) =>
        StructOrdering.RegisterUsedStructIfApplicable(typeSymbol, currentStructType, compilation, usedStructs);

    /// <inheritdoc />
    public string? TryGetUserTypeName(INamedTypeSymbol type) => ResolveUserTypeName(type);

    /// <inheritdoc />
    public void ReportUnsubstitutedTypeParameter(ITypeParameterSymbol typeParameter)
    {
        if (currentNode is not null)
        {
            reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, currentNode, "unsubstituted type parameter");
            return;
        }

        var location = typeParameter.Locations
            .AsStackEnumerable()
            .FirstOrDefault(static loc => loc.IsInSource) ?? Location.None;
        reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, location, "unsubstituted type parameter");
    }
}
