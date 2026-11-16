using Kassyi.GSharp.Compiler.Transpiler.Helpers.StackArrays;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>Verifies which local types become a fixed-size C++ array in a stack array declaration.</summary>
[Category(TestCategory.Compiler)]
public class InitializationSyntaxExtractorTests
{
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("span", SpecialType.System_Int32, false)]
    [InlineData("readOnlySpan", SpecialType.System_Single, true)]
    [InlineData("array", SpecialType.System_Int32, false)]
    public void TryGetArrayElementType_SystemSpansAndArrays_ExtractsElementType(
        string parameter,
        SpecialType elementType,
        bool isConst)
    {
        Assert.True(InitializationSyntaxExtractor.TryGetArrayElementType(GetParameterType(parameter), out var element, out var readOnly));
        Assert.Equal<ISymbol>(TestCompilation.GetSpecialType(elementType), element, SymbolEqualityComparer.Default);
        Assert.Equal(isConst, readOnly);
    }

    // A user type named Span or ReadOnlySpan is a struct, not a stack array
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("userSpan")]
    [InlineData("userReadOnlySpan")]
    public void TryGetArrayElementType_UserTypeWithSpanName_ReturnsFalse(string parameter)
    {
        Assert.False(InitializationSyntaxExtractor.TryGetArrayElementType(GetParameterType(parameter), out _, out _));
    }

    private static Compilation TestCompilation =>
        RoslynTestUtility.GetTestCaseContext("Transpiler/InitializationSyntaxExtractorSource.cs").Compilation;

    private static ITypeSymbol GetParameterType(string name)
    {
        var locals = TestCompilation.GetTypeByMetadataName("Locals")
            ?? throw new InvalidOperationException("Test source does not declare 'Locals'.");
        var declare = locals.GetMembers("Declare")[0] as IMethodSymbol
            ?? throw new InvalidOperationException("Test source does not declare 'Locals.Declare'.");
        var parameter = declare.Parameters.FirstOrDefault(p => p.Name == name)
            ?? throw new InvalidOperationException($"'Locals.Declare' has no parameter '{name}'.");
        return parameter.Type;
    }
}
