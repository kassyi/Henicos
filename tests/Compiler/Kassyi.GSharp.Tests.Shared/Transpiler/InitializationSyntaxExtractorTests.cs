using Kassyi.GSharp.Compiler.Transpiler.Helpers;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Kassyi.ZeroAlloc.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>Verifies which local types become a fixed-size C++ array in a stack array declaration.</summary>
[Category(TestCategory.Compiler)]
public class InitializationSyntaxExtractorTests
{
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("span", "int", false)]
    [InlineData("readOnlySpan", "float", true)]
    [InlineData("array", "int", false)]
    public void TryGetArrayElementType_SystemSpansAndArrays_ExtractsElementType(
        string parameter,
        string elementType,
        bool isConst)
    {
        Assert.True(InitializationSyntaxExtractor.TryGetArrayElementType(GetParameterType(parameter), out var element, out var readOnly));
        Assert.Equal(elementType, GetSimpleTypeName(element));
        Assert.Equal(isConst, readOnly);
    }

    private static string GetSimpleTypeName(ITypeSymbol type) => type?.SpecialType switch
    {
        SpecialType.System_Int32 => "int",
        SpecialType.System_Single => "float",
        SpecialType.System_Double => "double",
        SpecialType.System_Boolean => "bool",
        _ => type?.Name
    };

    // A user type named Span or ReadOnlySpan is a struct, not a stack array
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("userSpan")]
    [InlineData("userReadOnlySpan")]
    public void TryGetArrayElementType_UserTypeWithSpanName_ReturnsFalse(string parameter)
    {
        Assert.False(InitializationSyntaxExtractor.TryGetArrayElementType(GetParameterType(parameter), out _, out _));
    }

    private static ITypeSymbol GetParameterType(string name)
    {
        var (compilation, _, _) = RoslynTestUtility.GetTestCaseContext("Transpiler/InitializationSyntaxExtractorSource.cs");
        var locals = compilation.GetTypeByMetadataName("Locals")
            ?? throw new InvalidOperationException("Test source does not declare 'Locals'.");
        var declare = locals.GetMembers("Declare")[0] as IMethodSymbol
            ?? throw new InvalidOperationException("Test source does not declare 'Locals.Declare'.");
        return declare.Parameters.AsSpan().AsStackEnumerable()
            .First(p => p.Name == name)
            .Type;
    }
}
