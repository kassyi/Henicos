namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>
/// Services that <see cref="CppTypeWriter"/> asks of the body writer when emitting C++ types.
/// </summary>
internal interface ICppTypeServices
{
    /// <summary>
    /// Returns <paramref name="typeSymbol"/> with enclosing type parameters substituted, called at the start of <see cref="CppTypeWriter.AppendType{TServices}"/>.
    /// </summary>
    ITypeSymbol? SubstituteTypeParameters(ITypeSymbol? typeSymbol);

    /// <summary>
    /// Records a non-special <paramref name="typeSymbol"/> so its definition is emitted, called before spelling its C++ name.
    /// </summary>
    void RegisterUsedStruct(ITypeSymbol typeSymbol);

    /// <summary>
    /// Returns the encoded C++ name when <paramref name="type"/> is an emitted user struct, or <see langword="null"/> to fall back to the legacy spelling.
    /// </summary>
    string? TryGetUserTypeName(INamedTypeSymbol type);

    /// <summary>
    /// Reports a diagnostic for <paramref name="typeParameter"/>, called only when an unsubstituted type parameter remains.
    /// </summary>
    void ReportUnsubstitutedTypeParameter(ITypeParameterSymbol typeParameter);
}
