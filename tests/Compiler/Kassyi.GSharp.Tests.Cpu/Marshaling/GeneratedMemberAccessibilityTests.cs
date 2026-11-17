using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

/// <summary>
/// Verifies the accessibility that a generated member gets from the types it names and the class it is declared in:
/// the widest that C# accepts (CS0050 to CS0053), never wider, and the narrowest meet of two accessibilities.
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

    // Protected domains also reach derived types: C# compares the domain of the member (its keyword, the class and the
    // classes around it) with the domain of each type it names, so what is accepted follows from where the types and the
    // class are declared. Each row is a shape of the C# oracle (a small project that tried every keyword)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    // A protected type of the class itself: protected and private protected reach exactly the class and its derived types
    [InlineData("Host", "Host+ProtectedS", Accessibility.Protected)]
    [InlineData("Host", "Host+PrivateProtectedS", Accessibility.ProtectedAndInternal)]
    [InlineData("Host", "Host+ProtectedInternalS", Accessibility.ProtectedOrInternal)]
    // A container that is protected in the outer class of the type already has that domain: public is accepted, also
    // in a static class (which cannot hold a protected member)
    [InlineData("ProtectedContainer+StaticK", "ProtectedContainer+S", Accessibility.Public)]
    [InlineData("ProtectedContainer+StaticK", "Inner", Accessibility.Internal)]
    [InlineData("Host+ProtectedNested", "Host+PrivateS", Accessibility.Private)]
    [InlineData("Host+ProtectedNested", "Host+InternalS", Accessibility.Internal)]
    [InlineData("Host+ProtectedNested", "Open", Accessibility.Public)]
    // A public container of the outer class does not: derived types of the container need not derive from the outer class
    [InlineData("ProtectedContainer+PublicStaticK", "ProtectedContainer+S", Accessibility.Private)]
    [InlineData("ProtectedContainer+InstanceK", "ProtectedContainer+S", Accessibility.Private)]
    // The class that declares the protected type, or derives from it, may name it in a protected member
    [InlineData("ProtectedContainer+InstanceK", "ProtectedContainer+InstanceK+Own", Accessibility.Protected)]
    [InlineData("ProtectedDerived", "ProtectedBase+S", Accessibility.Protected)]
    [InlineData("ProtectedDerived", "ProtectedBase+SPrivateProtected", Accessibility.ProtectedAndInternal)]
    [InlineData("ProtectedDerived", "ProtectedBase+SInternal", Accessibility.ProtectedOrInternal)]
    // A static, sealed, or struct container takes no protected member, so the next keyword that C# accepts is chosen
    [InlineData("StaticDerivedHost", "ProtectedBase+S", Accessibility.Private)]
    [InlineData("SealedDerived", "ProtectedBase+S", Accessibility.Private)]
    [InlineData("Host+ProtectedStructNested", "Host+InternalS", Accessibility.Internal)]
    [InlineData("StructDerivedFromInterface", "IProtectedBase+S", Accessibility.Private)]
    [InlineData("ProtectedDerivedFromInterface", "IProtectedBase+S", Accessibility.Protected)]
    // A protected internal type reaches the assembly and the derived types: from a public container only internal is
    // accepted, from a derived class protected internal
    [InlineData("FamilyOrAssemblyHost+InstanceK", "FamilyOrAssemblyHost+S", Accessibility.Internal)]
    // A private protected type reaches the derived types of its class within the assembly: nothing of a public nested
    // container reaches no further
    [InlineData("FamilyAndAssemblyHost+InstanceK", "FamilyAndAssemblyHost+S", Accessibility.Private)]
    public void ChooseIn_ProtectedTypeOrContainer_FollowsTheDomains(
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
    [InlineData("Host+PrivateProtectedS", false)]
    [InlineData("ProtectedContainer+S", false)]
    [InlineData("Host+ProtectedS*", false)]
    [InlineData("Host+ProtectedInternalS[]", true)]
    [InlineData("Generic<Host+ProtectedS>", false)]
    [InlineData("Generic<Host+ProtectedInternalS>", true)]
    [InlineData("Host+Hidden+Deep", false)]
    [InlineData("Host+PrivateS", false)]
    [InlineData("Generic<Host+PrivateS>", false)]
    [InlineData("Host+PrivateS[]", false)]
    public void IsAtLeastInternal_Type_TellsWhetherTheAssemblyCanNameIt(string type, bool expected)
    {
        Assert.Equal(expected, GeneratedMemberAccessibility.IsAtLeastInternal(Resolve(type)));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(Accessibility.Public, Accessibility.Public, Accessibility.Public)]
    [InlineData(Accessibility.Public, Accessibility.ProtectedOrInternal, Accessibility.ProtectedOrInternal)]
    [InlineData(Accessibility.Public, Accessibility.Internal, Accessibility.Internal)]
    [InlineData(Accessibility.Public, Accessibility.Protected, Accessibility.Protected)]
    [InlineData(Accessibility.Public, Accessibility.ProtectedAndInternal, Accessibility.ProtectedAndInternal)]
    [InlineData(Accessibility.Public, Accessibility.Private, Accessibility.Private)]
    [InlineData(
        Accessibility.ProtectedOrInternal, Accessibility.ProtectedOrInternal, Accessibility.ProtectedOrInternal)]
    [InlineData(Accessibility.ProtectedOrInternal, Accessibility.Internal, Accessibility.Internal)]
    [InlineData(Accessibility.ProtectedOrInternal, Accessibility.Protected, Accessibility.Protected)]
    [InlineData(
        Accessibility.ProtectedOrInternal, Accessibility.ProtectedAndInternal, Accessibility.ProtectedAndInternal)]
    [InlineData(Accessibility.ProtectedOrInternal, Accessibility.Private, Accessibility.Private)]
    [InlineData(Accessibility.Internal, Accessibility.Internal, Accessibility.Internal)]
    [InlineData(Accessibility.Internal, Accessibility.Protected, Accessibility.ProtectedAndInternal)]
    [InlineData(Accessibility.Internal, Accessibility.ProtectedAndInternal, Accessibility.ProtectedAndInternal)]
    [InlineData(Accessibility.Internal, Accessibility.Private, Accessibility.Private)]
    [InlineData(Accessibility.Protected, Accessibility.Protected, Accessibility.Protected)]
    [InlineData(Accessibility.Protected, Accessibility.ProtectedAndInternal, Accessibility.ProtectedAndInternal)]
    [InlineData(Accessibility.Protected, Accessibility.Private, Accessibility.Private)]
    [InlineData(
        Accessibility.ProtectedAndInternal, Accessibility.ProtectedAndInternal, Accessibility.ProtectedAndInternal)]
    [InlineData(Accessibility.ProtectedAndInternal, Accessibility.Private, Accessibility.Private)]
    [InlineData(Accessibility.Private, Accessibility.Private, Accessibility.Private)]
    public void Narrowest_TwoAccessibilities_PicksTheNarrower(
        Accessibility first,
        Accessibility second,
        Accessibility expected)
    {
        Assert.Equal(expected, GeneratedMemberAccessibility.Narrowest(first, second));
        Assert.Equal(expected, GeneratedMemberAccessibility.Narrowest(second, first));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void ProtectedNestedLauncher_DerivedAndUnrelatedCallers_CompileWithoutDiagnostics(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilationAt(
                ("protected.cs", RoslynTestUtility.ReadTestCase("Marshaling/ProtectedNestedLauncherSource.cs")))
            .WithAssemblyName("ProtectedLauncherTest");
        var driver = CSharpGeneratorDriver.Create(
            generators: [new GpuKernelGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
            optionsProvider: new TestAnalyzerConfigOptionsProvider(dialect));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var errors = output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
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
