using Kassyi.GSharp.Naming;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>Verifies that the C++ names of types derive injectively from their symbols.</summary>
[Category(TestCategory.Compiler)]
public class GlobalCppNamesTests
{
    private const string Source = """
        namespace Repro
        {
            public struct A { public struct B { public struct C { } public struct B_C { } } }
            public struct Plain { }
            public class Outer<T> { public struct Inner<U> { } }
            public struct Generic<T> { }
            public struct Generic<T, U> { }
        }
        namespace N1 { public struct Outer { public struct Inner { } } }
        namespace N2 { public struct Outer_Inner { } }
        public struct GlobalType { }
        """;

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Repro.Plain", "__gsharp_ty_Repro__Plain")]
    [InlineData("GlobalType", "__gsharp_ty_GlobalType")]
    [InlineData("Repro.A+B+C", "__gsharp_ty_Repro__A__B__C")]
    public void Test_GetTypeName_JoinsNamespacesAndContainingTypes(string metadataName, string expected)
    {
        Assert.Equal(expected, GlobalCppNames.GetTypeName(GetType(metadataName)));
    }

    // The #402 report: these pairs used to share one C++ name.
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Repro.A+B+C", "Repro.A+B+B_C")]
    [InlineData("N1.Outer+Inner", "N2.Outer_Inner")]
    [InlineData("Repro.Generic`1", "Repro.Generic`2")]
    public void Test_GetTypeName_PreviouslyCollidingTypes_AreDistinct(string first, string second)
    {
        Assert.NotEqual(GlobalCppNames.GetTypeName(GetType(first)), GlobalCppNames.GetTypeName(GetType(second)));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_GetTypeName_GenericDefinition_CarriesArity()
    {
        Assert.Equal("__gsharp_ty_Repro__Generic_a1", GlobalCppNames.GetTypeName(GetType("Repro.Generic`1")));
        Assert.Equal("__gsharp_ty_Repro__Generic_a2", GlobalCppNames.GetTypeName(GetType("Repro.Generic`2")));
        Assert.Equal("__gsharp_ty_Repro__Outer_a1__Inner_a1",
            GlobalCppNames.GetTypeName(GetType("Repro.Outer`1+Inner`1")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_GetTypeName_ConstructedType_UsesTheDefinitionName()
    {
        var compilation = CreateCompilation();
        var definition = GetType(compilation, "Repro.Generic`1");
        var constructed = definition.Construct(compilation.GetSpecialType(SpecialType.System_Int32));

        Assert.Equal(GlobalCppNames.GetTypeName(definition), GlobalCppNames.GetTypeName(constructed));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_GetTypeName_DecodesBackToTheCsharpPath()
    {
        var name = GlobalCppNames.GetTypeName(GetType("Repro.A+B+B_C"));

        Assert.True(GSharpNameEncoding.TryDecode(name, out var display));
        Assert.Equal("Repro.A.B.B_C", display);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_GetTypeParameterName_UsesTypeParameterPrefix()
    {
        var parameter = GetType("Repro.Generic`2").TypeParameters[1];

        Assert.Equal("__gsharp_tp_U", GlobalCppNames.GetTypeParameterName(parameter));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Shorten_ShortName_IsUnchanged()
    {
        var name = GSharpNameEncoding.TypePrefix + new string('a', GlobalCppNames.ShortenThreshold - GSharpNameEncoding.TypePrefix.Length);

        Assert.Equal(name, GlobalCppNames.Shorten(GSharpNameEncoding.TypePrefix, name[GSharpNameEncoding.TypePrefix.Length..]));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Shorten_LongName_IsBoundedDeterministicAndDistinct()
    {
        var prefix = GSharpNameEncoding.KernelPrefix;
        var first = GlobalCppNames.Shorten(prefix, new string('a', 400) + "X");
        var second = GlobalCppNames.Shorten(prefix, new string('a', 400) + "Y");

        Assert.True(first.Length <= GlobalCppNames.ShortenThreshold);
        Assert.StartsWith(prefix, first, StringComparison.Ordinal);
        Assert.Matches("_h[0-9a-f]{10}$", first);
        Assert.NotEqual(first, second);
        Assert.Equal(first, GlobalCppNames.Shorten(prefix, new string('a', 400) + "X"));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Shorten_HeadEndingWithUnderscore_IsTrimmedBeforeTheMarker()
    {
        var prefix = GSharpNameEncoding.TypePrefix;
        var headLength = GlobalCppNames.ShortenThreshold - prefix.Length - "_h".Length - 10;
        var body = new string('a', headLength - 1) + "_" + new string('b', 50);

        var shortened = GlobalCppNames.Shorten(prefix, body);

        Assert.DoesNotContain("__h", shortened, StringComparison.Ordinal);
        Assert.Matches("a_h[0-9a-f]{10}$", shortened);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_GetTypeName_FileTypesWithTheSameName_AreDistinctAndOrderedByPath()
    {
        var compilation = CreateCompilation(
            ("src/b/Second.cs", "namespace F { file struct Local { } }"),
            ("src/a/First.cs", "namespace F { file struct Local { } }"),
            ("src/c/Public.cs", "namespace F { public struct Local { } }"));
        var names = GetTypeNamesByFile(compilation, "Local");

        Assert.Equal("__gsharp_ty_F__Local_f0", names["src/a/First.cs"]);
        Assert.Equal("__gsharp_ty_F__Local_f1", names["src/b/Second.cs"]);
        Assert.Equal("__gsharp_ty_F__Local", names["src/c/Public.cs"]);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_GetTypeName_NestedTypeOfFileType_InheritsTheFileIdentity()
    {
        var compilation = CreateCompilation(
            ("a.cs", "namespace F { file struct Outer { public struct Inner { } } }"),
            ("b.cs", "namespace F { file struct Outer { public struct Inner { } } }"));
        var names = GetTypeNamesByFile(compilation, "Inner");

        Assert.Equal("__gsharp_ty_F__Outer_f0__Inner", names["a.cs"]);
        Assert.Equal("__gsharp_ty_F__Outer_f1__Inner", names["b.cs"]);
    }

    private static Dictionary<string, string> GetTypeNamesByFile(CSharpCompilation compilation, string typeName)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<StructDeclarationSyntax>())
            {
                if (declaration.Identifier.ValueText == typeName &&
                    model.GetDeclaredSymbol(declaration) is { } symbol)
                {
                    names.Add(tree.FilePath, GlobalCppNames.GetTypeName(symbol));
                }
            }
        }

        return names;
    }

    private static CSharpCompilation CreateCompilation(params (string Path, string Source)[] files) =>
        CSharpCompilation.Create("GlobalCppNamesFileTests")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(RoslynTestUtility.BaseReferences)
            .AddSyntaxTrees(files.Select(f =>
                CSharpSyntaxTree.ParseText(f.Source, new CSharpParseOptions(LanguageVersion.Preview), f.Path)));

    private static INamedTypeSymbol GetType(string metadataName) => GetType(CreateCompilation(), metadataName);

    private static INamedTypeSymbol GetType(CSharpCompilation compilation, string metadataName) =>
        compilation.GetTypeByMetadataName(metadataName)
        ?? throw new InvalidOperationException($"Test source does not declare '{metadataName}'.");

    private static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create("GlobalCppNamesTests")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(RoslynTestUtility.BaseReferences)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.Preview)));
}
