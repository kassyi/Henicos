#nullable enable
using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Tests.Generators;

/// <summary>
/// Symbol queries shared by the test convention analyzers (HNT0002, HNT0004 ...).
/// </summary>
internal static class TestSymbols
{
    public const string FactAttributeMetadataName = "Xunit.FactAttribute";

    /// <summary>
    /// Whether the compilation is one of this repository's test assemblies. The compiler also loads the analyzers
    /// (for HNT0003), so the analyzers that check tests return early elsewhere.
    /// </summary>
    public static bool IsTestAssembly(Compilation compilation) =>
        compilation.AssemblyName?.Contains("Test") == true;

    /// <summary>
    /// Whether the method is an xUnit test: an attribute derives from <c>Xunit.FactAttribute</c>, which covers
    /// <c>[Fact]</c>, <c>[Theory]</c>, <c>[SkippableFact]</c> and <c>[SkippableTheory]</c>.
    /// </summary>
    public static bool IsTestMethod(IMethodSymbol method, INamedTypeSymbol factAttribute)
    {
        foreach (var attribute in method.GetAttributes())
        {
            for (var type = attribute.AttributeClass; type is not null; type = type.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(type, factAttribute))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
