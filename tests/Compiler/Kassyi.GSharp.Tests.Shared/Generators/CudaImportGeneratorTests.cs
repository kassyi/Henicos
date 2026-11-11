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
    public void UnrelatedSourceChange_KeepsCudaImportStepCached()
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

    // A method with a parameter the launcher cannot pass gets a body that throws the GSH0110 message instead of a
    // launch that passes the host address of a reference (#1296); it stays implemented, so no CS8795 joins the
    // diagnostic
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void ParameterTheLauncherCannotPass_ThrowsInsteadOfLaunching()
    {
        var compilation = CreateCompilation(
            RoslynTestUtility.ReadTestCase("NativeApi/CudaImportParameterDiagnostics.cs"));

        CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics);
        var source = Assert.Single(output.SyntaxTrees, static t => t.FilePath.EndsWith("_CudaImport.g.cs")).ToString();
        var text = MethodBody(source, "public static partial void Text(string text)");
        Assert.Equal(
            "throw new global::System.NotSupportedException(\"Parameter 'text' of [CudaImport] method " +
            "'ImportedKernels.Text' has type 'string', which the launcher cannot pass to the kernel: " +
            ModuleKernelParameterTypes.ManagedTypeReason + "\");",
            text);
        Assert.Contains("cuLaunchKernel", MethodBody(source, "public static partial void Passable("));
        var diagnostics = output.GetDiagnostics();
        Assert.DoesNotContain(diagnostics, static d => d.Id == "CS8500");
        string[] rejected = ["Text", "Callback", "Rows", "Streamed", "Optional"];
        Assert.All(rejected, method => Assert.DoesNotContain(diagnostics, d =>
            d.Id == "CS8795" && d.GetMessage().Contains($"ImportedKernels.{method}(", StringComparison.Ordinal)));
    }

    // The statements between the braces that follow <signature>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No method '{signature}' in the generated source.");
        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            depth += source[i] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return source[(open + 1)..i].Trim();
            }
        }

        throw new Xunit.Sdk.XunitException($"The body of '{signature}' is not closed.");
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
