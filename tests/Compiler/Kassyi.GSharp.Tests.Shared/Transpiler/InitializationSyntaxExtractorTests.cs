using Kassyi.GSharp.Compiler.Transpiler.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>Verifies which local types become a fixed-size C++ array in a stack array declaration.</summary>
[Category(TestCategory.Compiler)]
public class InitializationSyntaxExtractorTests
{
    private static readonly string s_source = RoslynTestUtility.ReadTestCase("Transpiler/InitializationSyntaxExtractorSource.cs");

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("span", "int", false)]
    [InlineData("readOnlySpan", "float", true)]
    [InlineData("array", "int", false)]
    public void Test_TryGetArrayElementType_SystemSpansAndArrays(string parameter, string elementType, bool isConst)
    {
        Assert.True(InitializationSyntaxExtractor.TryGetArrayElementType(GetParameterType(parameter), out var element, out var readOnly));
        Assert.Equal(elementType, element?.ToDisplayString());
        Assert.Equal(isConst, readOnly);
    }

    // A user type named Span or ReadOnlySpan is a struct, not a stack array
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("userSpan")]
    [InlineData("userReadOnlySpan")]
    public void Test_TryGetArrayElementType_UserTypeWithSpanName_IsNotAnArray(string parameter)
    {
        Assert.False(InitializationSyntaxExtractor.TryGetArrayElementType(GetParameterType(parameter), out _, out _));
    }

    private static ITypeSymbol GetParameterType(string name)
    {
        var compilation = CSharpCompilation.Create("InitializationSyntaxExtractorTests")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(RoslynTestUtility.BaseReferences)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(s_source));
        var locals = compilation.GetTypeByMetadataName("Locals")
            ?? throw new InvalidOperationException("Test source does not declare 'Locals'.");
        var declare = locals.GetMembers("Declare")[0] as IMethodSymbol
            ?? throw new InvalidOperationException("Test source does not declare 'Locals.Declare'.");
        foreach (var parameter in declare.Parameters)
        {
            if (parameter.Name == name)
            {
                return parameter.Type;
            }
        }

        throw new InvalidOperationException($"'Locals.Declare' has no parameter '{name}'.");
    }
}
