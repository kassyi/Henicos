namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>
/// The host environment contract through which the C++ IR writer emits types, tracks used structs, writes C++, and
/// advances method-scoped counters without accessing the legacy compiler layers directly.
/// </summary>
internal interface IIrCppEnvironment
{
    /// <summary>The route profile currently being emitted.</summary>
    CppBodyProfile Profile { get; }

    /// <summary>The active output writer for the current body.</summary>
    SourceWriter Writer { get; }

    /// <summary>Writes the C++ spelling of <paramref name="type"/> and registers any used struct.</summary>
    void AppendType(ITypeSymbol? type);

    /// <summary>Registers <paramref name="type"/> as a used struct without writing it (for deduced locals).</summary>
    void RegisterUsedType(ITypeSymbol? type);

    /// <summary>
    /// Advances the method counter <paramref name="counter"/> by <paramref name="count"/> and returns the updated
    /// value.
    /// </summary>
    int Advance(IrCppCounter counter, int count = 1);
}
