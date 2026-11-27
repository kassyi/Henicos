namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>
/// Resolves the C++ identifiers of user-declared symbols for the IR body naming plan, implemented outside the C++
/// backend folder until global C++ naming moves out of the syntax transpiler.
/// </summary>
internal interface IIrCppNameSource
{
    /// <summary>
    /// The C++ name the legacy route gives to the local or parameter <paramref name="symbol"/> declared in
    /// <paramref name="region"/>.
    /// </summary>
    string LocalName(ISymbol symbol, string name, ITranspilerDialect dialect, int region);
}
