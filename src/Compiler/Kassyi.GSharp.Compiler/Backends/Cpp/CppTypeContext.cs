namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>
/// Context-free inputs for <see cref="CppTypeWriter"/> type, storage-type, and parameter emission.
/// </summary>
internal readonly struct CppTypeContext(SourceWriter writer, ITranspilerDialect dialect)
{
    public SourceWriter Writer { get; } = writer;
    public ITranspilerDialect Dialect { get; } = dialect;
    public Compilation? Compilation { get; init; }
    public INamedTypeSymbol? CurrentStructType { get; init; }
    public HashSet<INamedTypeSymbol>? UsedStructs { get; init; }
    public Location? CurrentLocation { get; init; }
    public object? FailureSink { get; init; }
    public Func<ITypeSymbol?, INamedTypeSymbol?, Compilation?, ITypeSymbol?>? SubstituteTypeParameters { get; init; }
    public Action<ITypeSymbol?, INamedTypeSymbol?, Compilation, HashSet<INamedTypeSymbol>>? RegisterUsedStruct { get; init; }
    public Func<INamedTypeSymbol, string?>? TryGetUserTypeName { get; init; }
    public Action<Location, object?>? ReportUnsubstitutedTypeParameter { get; init; }
}
