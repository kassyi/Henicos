using Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Classifies C# types for emission to target C++, determining whether a type has a direct C++ type
/// representation or requires C++ type deduction (<c>auto</c>).
/// </summary>
internal static class TypeMapping
{
    /// <summary>
    /// Whether <paramref name="type"/> has a direct C++ type representation that can be emitted by
    /// <see cref="Dialects.ITranspilerDialect.AppendType"/> for declarations, rather than using C++ type deduction (<c>auto</c>).
    /// </summary>
    public static bool HasDirectCppType(ITypeSymbol? type)
    {
        if (type is null or { TypeKind: TypeKind.Error })
        {
            return false;
        }

        if (StackLinqHelper.IsStackLinqType(type))
        {
            return false;
        }

        return !IsOrContainsTypeParameter(type);
    }

    /// <summary>
    /// Whether <paramref name="type"/> is or contains an unresolved type parameter.
    /// </summary>
    public static bool IsOrContainsTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => IsOrContainsTypeParameter(array.ElementType),
        IPointerTypeSymbol pointer => IsOrContainsTypeParameter(pointer.PointedAtType),
        INamedTypeSymbol named => named.TypeArguments
            .AsStackEnumerable()
            .Any(static argument => IsOrContainsTypeParameter(argument)),
        _ => false,
    };
}
