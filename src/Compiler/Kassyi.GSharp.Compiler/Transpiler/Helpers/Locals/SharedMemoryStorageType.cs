namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Locals;

/// <summary>
/// Writes the C++ storage type of a local or <c>out</c> slot, emitting <c>T*</c> for
/// <c>SharedMemory&lt;T&gt;</c> instead of the CPU frame wrapper type.
/// </summary>
internal static class SharedMemoryStorageType
{
    /// <summary>
    /// Appends the C++ storage type for <paramref name="typeSymbol"/> to the emitter writer: <c>T*</c> when
    /// <paramref name="typeSymbol"/> is <c>SharedMemory&lt;T&gt;</c>, or <see cref="ITranspilerDialect.AppendType"/>
    /// otherwise.
    /// </summary>
    public static void Append(ref TranspileContext context, ITypeSymbol? typeSymbol)
    {
        if (typeSymbol.IsSharedMemoryType() &&
            typeSymbol is INamedTypeSymbol { TypeArguments.Length: 1 } sharedMemory)
        {
            context.Emitter.Dialect.AppendType(ref context, sharedMemory.TypeArguments[0]);
            context.Emitter.Writer.Append('*');
            return;
        }

        context.Emitter.Dialect.AppendType(ref context, typeSymbol);
    }
}
