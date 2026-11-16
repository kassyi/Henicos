using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

/// <summary>
/// Verifies the accessibility that a generated member gets from the types it names and the class it is declared in:
/// the widest that C# accepts (CS0050 to CS0053), never wider, with protected domains taken on the safe side.
/// </summary>
[Category(TestCategory.Compiler)]
public class GeneratedMemberAccessibilityTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Marshaling/GeneratedMemberAccessibilitySource.cs")
    );

    // A type is named as the type itself, T[] (array), T* (pointer) or Generic<T>; a type name is a metadata name
    // under Access ('+' for a nested type)
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Host", "Open", Accessibility.Public)]
    [InlineData("Host", "Inner", Accessibility.Internal)]
    [InlineData("Host", "Host+InternalS", Accessibility.Internal)]
    [InlineData("Host", "Host+PrivateS", Accessibility.Private)]
    [InlineData("Host+PublicNested", "Host+PrivateS", Accessibility.Private)]
    [InlineData("Host+InternalNested", "Host+InternalS", Accessibility.Public)]
    [InlineData("Host+PrivateNested", "Host+PrivateS", Accessibility.Public)]
    [InlineData("Host+PrivateNested+InPrivate", "Host+PrivateS", Accessibility.Public)]
    [InlineData("Host+PrivateNested", "Inner", Accessibility.Public)]
    public void ChooseIn_TypeAndContainer_PicksTheWidestThatCompiles(
        string container,
        string type,
        Accessibility expected)
    {
        var chosen = Named(Resolve(type)).ChooseIn(GetType(container));

        Assert.Equal(expected, chosen);
    }

    // Arrays, pointers and type arguments name their element and arguments; two levels of nesting narrow to the outer
    // private class
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Host+PrivateS[]", Accessibility.Private)]
    [InlineData("Host+PrivateS*", Accessibility.Private)]
    [InlineData("Generic<Host+PrivateS>", Accessibility.Private)]
    [InlineData("Generic<Inner>[]", Accessibility.Internal)]
    [InlineData("Generic<Open>", Accessibility.Public)]
    [InlineData("Host+Hidden+Deep", Accessibility.Private)]
    public void ChooseIn_ComposedTypes_NarrowsToTheirParts(string type, Accessibility expected)
    {
        Assert.Equal(
            expected,
            Named(Resolve(type)).ChooseIn(GetType("Host")));
    }

    // Protected domains also reach derived types, which the plan cannot express: a protected type is taken as private
    // to its container, a protected container as reaching everywhere, so the member always compiles
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Host", "Host+ProtectedS", Accessibility.Private)]
    [InlineData("Host", "Host+PrivateProtectedS", Accessibility.Private)]
    [InlineData("Host", "Host+ProtectedInternalS", Accessibility.Internal)]
    [InlineData("Host+ProtectedNested", "Host+PrivateS", Accessibility.Private)]
    [InlineData("Host+ProtectedNested", "Host+InternalS", Accessibility.Internal)]
    [InlineData("Host+ProtectedNested", "Open", Accessibility.Public)]
    public void ChooseIn_ProtectedTypeOrContainer_StaysOnTheSafeSide(
        string container,
        string type,
        Accessibility expected)
    {
        Assert.Equal(
            expected,
            Named(Resolve(type)).ChooseIn(GetType(container)));
    }

    // No accessibility of a member of an unrelated class can name a private nested type: the plan falls back to private
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void ChooseIn_TypeNotNameableFromContainer_FallsBackToPrivate()
    {
        Assert.Equal(
            Accessibility.Private,
            Named(Resolve("Host+PrivateS")).ChooseIn(GetType("Open")));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ChooseIn_NoTypes_KeepsPublic()
    {
        Assert.Equal(Accessibility.Public, default(GeneratedMemberAccessibility.NamedTypes).ChooseIn(GetType("Host")));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Open", true)]
    [InlineData("Inner", true)]
    [InlineData("Host+InternalS", true)]
    [InlineData("Host+ProtectedInternalS", true)]
    [InlineData("Host+ProtectedS", false)]
    [InlineData("Host+PrivateS", false)]
    [InlineData("Generic<Host+PrivateS>", false)]
    [InlineData("Host+PrivateS[]", false)]
    public void IsAtLeastInternal_Type_TellsWhetherTheAssemblyCanNameIt(string type, bool expected)
    {
        Assert.Equal(expected, GeneratedMemberAccessibility.IsAtLeastInternal(Resolve(type)));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(Accessibility.Public, Accessibility.Internal, Accessibility.Internal)]
    [InlineData(Accessibility.Private, Accessibility.Internal, Accessibility.Private)]
    [InlineData(Accessibility.Public, Accessibility.Public, Accessibility.Public)]
    public void Narrowest_TwoAccessibilities_PicksTheNarrower(
        Accessibility first,
        Accessibility second,
        Accessibility expected)
    {
        Assert.Equal(expected, GeneratedMemberAccessibility.Narrowest(first, second));
        Assert.Equal(expected, GeneratedMemberAccessibility.Narrowest(second, first));
    }

    private static GeneratedMemberAccessibility.NamedTypes Named(ITypeSymbol type) =>
        default(GeneratedMemberAccessibility.NamedTypes).With(type);

    private static INamedTypeSymbol GetType(string metadataName) =>
        s_compilation.GetTypeByMetadataName("Access." + metadataName)
        ?? throw new InvalidOperationException($"Type 'Access.{metadataName}' not found.");

    private static ITypeSymbol Resolve(string type)
    {
        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            return s_compilation.CreateArrayTypeSymbol(Resolve(type[..^2]));
        }

        if (type.EndsWith('*'))
        {
            return s_compilation.CreatePointerTypeSymbol(Resolve(type[..^1]));
        }

        const string GenericPrefix = "Generic<";
        return type.StartsWith(GenericPrefix, StringComparison.Ordinal)
            ? GetType("Generic`1").Construct(Resolve(type[GenericPrefix.Length..^1]))
            : GetType(type);
    }
}
