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

    // The module context name is a C# identifier for any assembly name, and two names give two identifiers: ASCII
    // letters and digits stay, '_' becomes _u and any other character _x and six hex digits of its code point
    [Theory]
    [InlineData("Kassyi.GSharp.Tests.Cuda", "GSharpModuleContext_Kassyi_x00002eGSharp_x00002eTests_x00002eCuda")]
    [InlineData("my-lib", "GSharpModuleContext_my_x00002dlib")]
    [InlineData("1lib", "GSharpModuleContext_1lib")]
    [InlineData("a b+c", "GSharpModuleContext_a_x000020b_x00002bc")]
    [InlineData("a{b}\"c", "GSharpModuleContext_a_x00007bb_x00007d_x000022c")]
    [InlineData("\u00e9t\u00e9", "GSharpModuleContext__x0000e9t_x0000e9")]
    [InlineData("\u65e5\u672c", "GSharpModuleContext__x0065e5_x00672c")]
    [InlineData("\u4e2d\u56fd", "GSharpModuleContext__x004e2d_x0056fd")]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void GetModuleContextName_ModuleName_EscapesInjectively(string input, string expected) =>
        Assert.Equal(expected, AotModuleNames.GetModuleContextName(input));

    // Names that differ only in characters an identifier cannot hold give distinct identifier parts (each of them was
    // '_' when every such character was replaced by '_')
    [Theory]
    [InlineData("a-b", "a_x00002db")]
    [InlineData("a_b", "a_ub")]
    [InlineData("a.b", "a_x00002eb")]
    [InlineData("a{b}", "a_x00007bb_x00007d")]
    [InlineData("a_b_", "a_ub_u")]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ToIdentifierPart_NamesDifferingInPunctuation_StayDistinct(string input, string expected) =>
        Assert.Equal(expected, AotModuleNames.ToIdentifierPart(input));

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void ToIdentifierPart_AsciiLettersAndDigitsOnly_ReturnsTheInput()
    {
        const string Name = "AlreadyAnIdentifier9";

        Assert.Same(Name, AotModuleNames.ToIdentifierPart(Name));
    }
}
