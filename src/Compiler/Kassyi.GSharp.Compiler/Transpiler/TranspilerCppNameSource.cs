using Kassyi.GSharp.Compiler.Backends.Cpp;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Resolves C++ local and parameter names for the IR naming plan by delegating to <see cref="CppNameResolver"/>.
/// </summary>
internal sealed class TranspilerCppNameSource : IIrCppNameSource
{
    /// <inheritdoc/>
    public string LocalName(ISymbol symbol, string name, ITranspilerDialect dialect, int region) =>
        CppNameResolver.Resolve(symbol, name, dialect, region).ToString();
}
