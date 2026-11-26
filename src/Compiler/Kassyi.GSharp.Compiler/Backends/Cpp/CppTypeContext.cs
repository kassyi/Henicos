namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>
/// Context-free inputs for <see cref="CppTypeWriter"/> type, storage-type, and parameter emission.
/// </summary>
internal readonly struct CppTypeContext<TServices>(
    SourceWriter writer,
    ITranspilerDialect dialect,
    TServices services)
    where TServices : struct, ICppTypeServices
{
    public SourceWriter Writer { get; } = writer;
    public ITranspilerDialect Dialect { get; } = dialect;
    public TServices Services { get; } = services;
}
