using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Generator;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class SharedMemberErrorSiteGeneratorTests
{
    private static readonly CSharpParseOptions s_parseOptions = new(LanguageVersion.Preview);

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cuda")]
    [InlineData("Cpu")]
    public void Generate_SharedStructMemberWithCheckedForm_ProducesNoSiteCollisionOrErrors(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilationAt(
                ("SharedMemberErrorSiteCases.cs",
                    RoslynTestUtility.ReadTestCase("Generators/SharedMemberErrorSiteCases.cs")))
            .WithAssemblyName("SharedMemberErrorSiteTest");

        var driver = CSharpGeneratorDriver.Create(
                generators: [new GpuKernelGenerator().AsSourceGenerator()],
                additionalTexts: null,
                parseOptions: s_parseOptions,
                optionsProvider: new TestAnalyzerConfigOptionsProvider(dialect))
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        var result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);

        var collisions = generatorDiagnostics
            .Where(static d => d.Id == GSharpDiagnostics.ErrorSiteIdCollision.Id)
            .Select(static d => d.ToString())
            .ToList();
        Assert.True(collisions.Count == 0, string.Join(Environment.NewLine, collisions));

        var genErrors = generatorDiagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToList();
        Assert.True(genErrors.Count == 0, string.Join(Environment.NewLine, genErrors));

        var compileErrors = output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToList();
        Assert.True(compileErrors.Count == 0, string.Join(Environment.NewLine, compileErrors));

        AssertErrorSiteMembers(result, "DisposeSwitchKernels.g.cs", 12, 18, "Dispose");
        AssertErrorSiteMembers(result, "CtorSwitchKernels.g.cs", 45, 50, ".ctor");
        AssertErrorSiteMembers(result, "DisposeThrowKernels.g.cs", 77, 85, "Dispose");
    }

    private static void AssertErrorSiteMembers(
        GeneratorRunResult result,
        string hintSuffix,
        int memberStartLine,
        int memberEndLine,
        string expectedMember)
    {
        var source = result.GeneratedSources
            .Single(s => s.HintName.EndsWith(hintSuffix, StringComparison.Ordinal));
        var sites = source.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Where(static c => c.Type is QualifiedNameSyntax { Right.Identifier.ValueText: "GSharpErrorSite" })
            .Select(static c =>
            {
                if (c.ArgumentList is { Arguments.Count: >= 5 } args &&
                    args.Arguments[2].Expression is LiteralExpressionSyntax lineLiteral &&
                    lineLiteral.Token.Value is int line &&
                    args.Arguments[4].Expression is LiteralExpressionSyntax memberLiteral)
                {
                    return (Line: line, MemberName: memberLiteral.Token.ValueText);
                }

                return (Line: -1, MemberName: "");
            })
            .ToList();

        var sharedMemberSites = sites
            .Where(s => s.Line >= memberStartLine && s.Line <= memberEndLine)
            .ToList();
        Assert.NotEmpty(sharedMemberSites);
        Assert.All(sharedMemberSites, s => Assert.Equal(expectedMember, s.MemberName));
    }
}
