using Xunit;
using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

public class IdentifierHelperTests
{
    // The artifact files keep the name that the native build and the launchers already use: only '.' is replaced
    [Theory]
    [InlineData("MyNamespace.MyClass", "Kassyi.GSharp.Kernels_MyNamespace_MyClass")]
    [InlineData("System.Collections.Generic", "Kassyi.GSharp.Kernels_System_Collections_Generic")]
    [InlineData("NoDotsHere", "Kassyi.GSharp.Kernels_NoDotsHere")]
    [InlineData("my-lib", "Kassyi.GSharp.Kernels_my-lib")]
    [InlineData("", "Kassyi.GSharp.Kernels_")]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void GetArtifactFileStem_ModuleName_ReplacesOnlyDots(string input, string expected) =>
        Assert.Equal(expected, AotModuleNames.GetArtifactFileStem(input));

    // The module context name is a C# identifier for any assembly name; a name that was one already after replacing '.'
    // (letters of any script included) keeps its earlier spelling
    [Theory]
    [InlineData("Kassyi.GSharp.Tests.Cuda", "GSharpModuleContext_Kassyi_GSharp_Tests_Cuda")]
    [InlineData("my-lib", "GSharpModuleContext_my_lib")]
    [InlineData("1lib", "GSharpModuleContext_1lib")]
    [InlineData("a b+c", "GSharpModuleContext_a_b_c")]
    [InlineData("a{b}\"c", "GSharpModuleContext_a_b__c")]
    [InlineData("\u00e9t\u00e9", "GSharpModuleContext_\u00e9t\u00e9")]
    [InlineData("\u65e5\u672c", "GSharpModuleContext_\u65e5\u672c")]
    [InlineData("\u4e2d\u56fd", "GSharpModuleContext_\u4e2d\u56fd")]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void GetModuleContextName_ModuleName_KeepsOnlyIdentifierCharacters(string input, string expected) =>
        Assert.Equal(expected, AotModuleNames.GetModuleContextName(input));

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void ToIdentifierPart_IdentifierCharactersOnly_ReturnsTheInput()
    {
        const string Name = "Already_An_Identifier9";

        Assert.Same(Name, AotModuleNames.ToIdentifierPart(Name));
    }
}
