namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>The C++ spelling of a user declaration, produced only by <see cref="CppNameResolver"/>.</summary>
/// <remarks>
/// The parts are written separately so that emission does not allocate; <see cref="ToString"/> exists for the few
/// callers that need the name as a string.
/// </remarks>
internal readonly struct CppName
{
    private CppName(CppNameKind kind, int ordinal, string name, int region = 0)
    {
        Kind = kind;
        Ordinal = ordinal;
        Name = name;
        Region = region;
    }

    public CppNameKind Kind { get; }

    /// <summary>The declaration ordinal within the declaring method, or -1 when the declaration was not found there.</summary>
    public int Ordinal { get; }

    /// <summary>The 1-based copy (region) ordinal stamped onto the node by an expansion, or 0 outside expansions.</summary>
    public int Region { get; }

    /// <summary>The C# name without the verbatim <c>@</c>.</summary>
    public string Name { get; }

    internal static CppName Verbatim(string name) => new(CppNameKind.Verbatim, -1, name);

    internal static CppName FunctionScope(int ordinal, string name, int region = 0) =>
        new(CppNameKind.FunctionScope, ordinal, name, region > 0 ? region : 0);

    internal static CppName Member(string name) => new(CppNameKind.Member, -1, name);

    internal static CppName BackingField(string propertyName) => new(CppNameKind.BackingField, -1, propertyName);

    internal static CppName Global(string cppName) => new(CppNameKind.Global, -1, cppName);

    public override string ToString()
    {
        using var writer = new SourceWriter();
        writer.AppendCppName(this);
        return writer.ToString();
    }
}
