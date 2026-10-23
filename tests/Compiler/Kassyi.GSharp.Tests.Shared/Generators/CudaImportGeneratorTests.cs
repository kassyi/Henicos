using Kassyi.GSharp.Compiler.NativeApi;

#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Generators;

[Category(TestCategory.Compiler)]
public class CudaImportGeneratorTests
{
    private static readonly string s_sourceCode =
        RoslynTestUtility.ReadTestCase("Generators/CudaImport_CachedStep.cu.cs");

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_UnrelatedSourceChange_KeepsCudaImportStepCached()
    {
        var compilation = CreateCompilation(s_sourceCode);
        var driver = CreateDriver();

        driver = driver.RunGenerators(compilation);
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var updated = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Other { class Unrelated { } }", parseOptions));
        driver = driver.RunGenerators(updated);

        var runResult = driver.GetRunResult().Results[0];

        Assert.True(runResult.TrackedSteps.TryGetValue(CudaImportGenerator.MethodTrackingName, out var methodSteps));
        var methodOutputs = methodSteps.SelectMany(s => s.Outputs).ToArray();
        Assert.NotEmpty(methodOutputs);
        Assert.All(methodOutputs, o => Assert.True(
            o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"Unexpected method step reason: {o.Reason}"));

        var outputOutputs = runResult.TrackedOutputSteps.SelectMany(s => s.Value).SelectMany(s => s.Outputs).ToArray();
        Assert.NotEmpty(outputOutputs);
        Assert.All(outputOutputs, o => Assert.True(
            o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"Unexpected output step reason: {o.Reason}"));
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        return CSharpCompilation.Create("TestAssembly")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .AddReferences(RoslynTestUtility.BaseReferences)
            .AddSyntaxTrees(syntaxTree);
    }

    private static GeneratorDriver CreateDriver()
    {
        return CSharpGeneratorDriver.Create(
            generators: [new CudaImportGenerator().AsSourceGenerator()],
            additionalTexts: null,
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
    }
}
