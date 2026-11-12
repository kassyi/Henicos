#nullable enable
using System.Collections.Generic;
using System.Linq;
using Kassyi.GSharp.Tests.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// Tests of <see cref="DifferentialCaseRegistryGenerator"/>: which static members the generated
/// <c>DifferentialCaseRegistry.All</c> lists, read from the generated tree by the symbols its entries reference.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Compiler)]
public class DifferentialCaseRegistryGeneratorTests
{
    private const string TestCaseFolder = "Generators/DifferentialCaseRegistry/";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Registry_EveryDeclarationForm_ListsEachCaseMember()
    {
        // mutant: IsStaticMemberDeclaration drops the PropertyDeclarationSyntax arm (properties are not listed)
        // mutant: CaseMembersOf takes only field.Declaration.Variables[0] (the second declarator is not listed)
        // mutant: IsCollectionOf returns false (collections are not listed)
        var (compilation, entries) = RegistryEntries("CaseType.cs", "CaseHolders.cs");

        // (recorded name, containing type by metadata name, member, collection)
        (string Name, string Type, string Member, bool IsCollection)[] expected =
            [
                ("Holders.CollectionHolder.Array", "Holders.CollectionHolder", "Array", true),
                ("Holders.CollectionHolder.List", "Holders.CollectionHolder", "List", true),
                ("Holders.CollectionHolder.ReadOnlyList", "Holders.CollectionHolder", "ReadOnlyList", true),
                ("Holders.FieldHolder.First", "Holders.FieldHolder", "First", false),
                ("Holders.FieldHolder.Mutable", "Holders.FieldHolder", "Mutable", false),
                ("Holders.FieldHolder.Second", "Holders.FieldHolder", "Second", false),
                ("Holders.FieldHolder.Single", "Holders.FieldHolder", "Single", false),
                ("Holders.Outer.Nested.Inner", "Holders.Outer+Nested", "Inner", false),
                ("Holders.PropertyHolder.AutoProperty", "Holders.PropertyHolder", "AutoProperty", false),
                ("Holders.PropertyHolder.ExpressionProperty", "Holders.PropertyHolder", "ExpressionProperty", false),
            ];
        Assert.Equal(expected.Select(static e => (e.Name, e.IsCollection)), entries.Select(static e => (e.Name, e.IsCollection)));
        for (int i = 0; i < expected.Length; i++)
        {
            var member = compilation.GetTypeByMetadataName(expected[i].Type)?.GetMembers(expected[i].Member).Single();
            Assert.True(
                SymbolEqualityComparer.Default.Equals(member, entries[i].Member),
                $"{expected[i].Name} references {entries[i].Member.Kind} {entries[i].Member.Name}"
            );
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Registry_StaticMembersOfOtherTypes_AreNotListed()
    {
        // mutant: AddIfCase compares memberType.Name with "DifferentialCase" (Other.DifferentialCase is listed)
        // mutant: IsCollectionOf accepts any IEnumerable<T> (List<string> is listed)
        var (_, entries) = RegistryEntries("CaseType.cs", "OtherStaticMembers.cs");

        Assert.Empty(entries);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Registry_PrivateCaseField_IsACompileErrorInTheRegistry()
    {
        // A case the registry cannot reach fails the build instead of being left out
        // mutant: AddIfCase skips members whose DeclaredAccessibility is Private
        var (compilation, registry) = RunGenerator("CaseType.cs", "PrivateCaseField.cs");

        var error = Assert.Single(compilation.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("CS0122", error.Id);
        Assert.Same(registry, error.Location.SourceTree);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Registry_OnlyATypeOfTheSameSimpleName_GeneratesNothing()
    {
        // mutant: hasCaseType is always true (a registry naming a type that does not exist)
        // The base references include the test assembly, which defines the real case type, so the probe keeps only
        // the core library
        var compilation = RoslynTestUtility.CreateCompilation(ReadSources("SameSimpleNameOnly.cs"))
            .WithReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var result = CreateDriver(compilation)
            .RunGenerators(compilation)
            .GetRunResult();

        Assert.Empty(result.GeneratedTrees);
        Assert.Empty(result.Diagnostics);
    }

    // The entries of DifferentialCaseRegistry.All as (member name it records, member it references, collection),
    // in the generated order; the generated code must compile
    private static (Compilation Compilation, List<(string Name, ISymbol Member, bool IsCollection)> Entries) RegistryEntries(
        params string[] files)
    {
        var (compilation, registry) = RunGenerator(files);
        Assert.Empty(compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        var model = compilation.GetSemanticModel(registry);
        var all = registry.GetRoot()
            .DescendantNodes()
            .OfType<PropertyDeclarationSyntax>()
            .Single(static p => p.Identifier.ValueText == "All");
        var collection = Assert.IsType<CollectionExpressionSyntax>(all.Initializer?.Value);

        var entries = new List<(string Name, ISymbol Member, bool IsCollection)>();
        foreach (var element in collection.Elements)
        {
            var (arguments, isCollection) = element switch
            {
                ExpressionElementSyntax { Expression: TupleExpressionSyntax tuple } =>
                    (tuple.Arguments.Select(static a => a.Expression).ToList(), false),
                SpreadElementSyntax { Expression: InvocationExpressionSyntax each } =>
                    (each.ArgumentList.Arguments.Select(static a => a.Expression).ToList(), true),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected registry element kind {element.Kind()}"),
            };
            string name = Assert.IsType<string>(model.GetConstantValue(arguments[0]).Value);
            var member = model.GetSymbolInfo(arguments[1]).Symbol;
            Assert.NotNull(member);
            Assert.True(member is IFieldSymbol or IPropertySymbol, $"{name} references {member.Kind} {member.Name}");
            entries.Add((name, member, isCollection));
        }

        return (compilation, entries);
    }

    private static (Compilation Compilation, SyntaxTree Registry) RunGenerator(params string[] files)
    {
        var compilation = RoslynTestUtility.CreateCompilation(ReadSources(files));
        var result = CreateDriver(compilation)
            .RunGenerators(compilation)
            .GetRunResult();

        Assert.Empty(result.Diagnostics);
        var registry = Assert.Single(result.GeneratedTrees);
        return (compilation.AddSyntaxTrees(registry), registry);
    }

    // The generated tree is parsed with the language version of the input, so that it can join the compilation
    private static GeneratorDriver CreateDriver(Compilation compilation) =>
        CSharpGeneratorDriver.Create(
            [new DifferentialCaseRegistryGenerator().AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options
        );

    private static string[] ReadSources(params string[] files) =>
        files.Select(static f => RoslynTestUtility.ReadTestCase(TestCaseFolder + f)).ToArray();
}
