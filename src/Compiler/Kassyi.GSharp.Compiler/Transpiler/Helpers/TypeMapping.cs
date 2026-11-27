namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Classifies C# types for emission to target C++, determining whether a type has a direct C++ type
/// representation or requires C++ type deduction (<c>auto</c>).
/// </summary>
internal static class TypeMapping
{
    /// <summary>
    /// Whether <paramref name="type"/> has a direct C++ type representation that can be emitted by
    /// <see cref="Backends.Cpp.CppTypeWriter.AppendType{TServices}"/> for declarations, rather than using C++ type deduction (<c>auto</c>).
    /// </summary>
    public static bool HasDirectCppType(ITypeSymbol? type) => CppTypeMapping.HasDirectCppType(type);

    /// <summary>
    /// Whether <paramref name="type"/> is or contains an unresolved type parameter.
    /// </summary>
    public static bool IsOrContainsTypeParameter(ITypeSymbol type) => CppTypeMapping.IsOrContainsTypeParameter(type);
}
