using Kassyi.GSharp.Compiler.Backends.Cpp;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Locals;

/// <summary>
/// Writes the C++ storage type of a local or <c>out</c> slot, emitting <c>T*</c> for
/// <c>SharedMemory&lt;T&gt;</c> instead of the CPU frame wrapper type.
/// </summary>
internal static class SharedMemoryStorageType
{
    /// <summary>
    /// Appends the C++ storage type for <paramref name="typeSymbol"/> to the emitter writer: <c>T*</c> when
    /// <paramref name="typeSymbol"/> is <c>SharedMemory&lt;T&gt;</c>, or <see cref="CppTypeWriter.AppendType"/>
    /// otherwise.
    /// </summary>
    public static void Append(ref TranspileContext context, ITypeSymbol? typeSymbol)
    {
        var typeContext = DialectTranspilerExtensions.CreateTypeContext(ref context);
        Append(in typeContext, typeSymbol);
    }

    /// <inheritdoc cref="Append(ref TranspileContext, ITypeSymbol?)"/>
    public static void Append(in CppTypeContext context, ITypeSymbol? typeSymbol) =>
        CppTypeWriter.AppendStorageType(in context, typeSymbol);
}
