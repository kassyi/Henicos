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
    // launch that passes the host address of a reference (#1296). The implementation repeats the parameter modifiers of
    // the declaration (ref kinds, scoped, params), so every method stays implemented and the output compiles without a
    // using directive of the runtime: GSH0110 is the only error of such a method
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void ParameterTheLauncherCannotPass_ThrowsInsteadOfLaunching()
    {
        var compilation = CreateCompilation(
            RoslynTestUtility.ReadTestCase("NativeApi/CudaImportParameterDiagnostics.cs"));

        CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics);
        var source = Assert.Single(output.SyntaxTrees, static t => t.FilePath.EndsWith(".CudaImport.g.cs")).ToString();
        var text = MethodBody(source, "public static partial void Text(string text)");
        Assert.Equal(
            "throw new global::System.NotSupportedException(\"Parameter 'text' of [CudaImport] method " +
            "'ImportedKernels.Text' has type 'string', which the launcher cannot pass to the kernel: " +
            ModuleKernelParameterTypes.ManagedTypeReason + "\");",
            text);
        Assert.Contains("cuLaunchKernel", MethodBody(source, "public static partial void Passable("));
        Assert.Contains("cuLaunchKernel", MethodBody(source, "public static partial void Counted("));
        Assert.Contains("cuLaunchKernel", MethodBody(source, "public static partial void Written("));
        Assert.Contains("cuLaunchKernel", MethodBody(source, "public static partial void Packed("));
        var errors = output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error || d.Id == "CS8500")
            .Select(static d => d.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
    }

    // The implementation part repeats each modifier of the declaration and writes a function pointer type in full
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Written(out int result, in int seed, ref readonly int limit)")]
    [InlineData("Spans(scoped global::System.Span<float> values)")]
    [InlineData("Many(int __gsharp_d_count, params float[] values)")]
    [InlineData("Called(delegate* unmanaged<int, void> callback, delegate*<ref int, float> compute)")]
    public void Signature_ParameterModifiersAndFunctionPointers_MatchTheDeclaration(string signature)
    {
        var compilation = CreateCompilation(
            RoslynTestUtility.ReadTestCase("NativeApi/CudaImportParameterDiagnostics.cs"));

        CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var source = Assert.Single(output.SyntaxTrees, static t => t.FilePath.EndsWith(".CudaImport.g.cs")).ToString();
        Assert.Contains($"public static partial void {signature}", source);
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

    // The namespace of a [CudaImport] class whose name is a keyword is written as C# source spells it, so the generated
    // part compiles
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CudaImportClass_KeywordNamespace_GeneratedPartCompiles()
    {
        var compilation = CreateCompilation(
            RoslynTestUtility.ReadTestCase("Generators/CudaImport_KeywordNamespace.cu.cs")
        );
        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var errors = diagnostics
            .Concat(output.GetDiagnostics())
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        var generated = driver.GetRunResult().GeneratedTrees.Single().GetText().ToString();
        Assert.Contains("namespace @event.Imports;", generated, StringComparison.Ordinal);
    }

    // The launcher declares no local that a parameter name can collide with (a, d_a, kernel, scope, ctx, kernelParams,
    // p_input, h_result) and names every type it uses from global::, so a type of the user's namespace (IntPtr,
    // GpuKernelLoader, GpuBuffer<T>, CudaException, CudaDriverApi, Lazy<T>, AppContext, System, Span<T>) changes
    // nothing and no using of the runtime is needed (#1131). Two module names that map to the same identifier part, and
    // overloads that pack their arguments into a struct, get members of their own.
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LauncherNames_ParametersAndTypesOfTheUserScope_Compile()
    {
        var output = RunGenerator("NativeApi/CudaImportNameCollisions.cs", out var generatorDiagnostics, out var trees);

        Assert.Empty(generatorDiagnostics);
        AssertNoErrors(output);
        Assert.Equal(13, trees.Length);
        Assert.All(trees, static t => Assert.DoesNotContain(
            t.GetRoot().DescendantNodes(),
            static node => node is UsingDirectiveSyntax));
    }

    // The generated part repeats the declaration (#1290): the ref kinds, params, keyword names with @, an unsafe
    // container, nested, generic, struct, ref struct, record struct and record containers. Classes of the same name in
    // other namespaces (#1288) or in another type of the same namespace each get a part of their own.
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Declarations_ModifiersKeywordsAndContainers_Compile()
    {
        var output = RunGenerator("NativeApi/CudaImportDeclarations.cs", out var generatorDiagnostics, out var trees);

        Assert.Empty(generatorDiagnostics);
        AssertNoErrors(output);
        Assert.Equal(13, trees.Length);
        var hintNames = trees.Select(static t => Path.GetFileName(t.FilePath)).ToArray();
        Assert.Equal(hintNames.Length, hintNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(hintNames, static n => Assert.EndsWith(".CudaImport.g.cs", n, StringComparison.Ordinal));
    }

    // A declaration that G# reports with GSH0113 gets no generated part: the generator neither fails (CS8785) nor
    // adds a second body or a part of a type that is not partial; the other methods of the class are still generated
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Declarations_GeneratorCannotImplement_GenerateNothingForThem()
    {
        RunGenerator("NativeApi/CudaImportDeclarationDiagnostics.cs", out var generatorDiagnostics, out var trees);

        Assert.Empty(generatorDiagnostics);
        var source = Assert.Single(trees).GetText().ToString();
        Assert.Contains(" Passable(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(" External(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(" Bodied(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(" Generic", source, StringComparison.Ordinal);
        Assert.DoesNotContain(" Counted(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(" Implemented(", source, StringComparison.Ordinal);
    }

    private static Compilation RunGenerator(string testCase, out ImmutableArray<Diagnostic> generatorDiagnostics,
        out ImmutableArray<SyntaxTree> generatedTrees)
    {
        var driver = CreateDriver()
            .RunGeneratorsAndUpdateCompilation(
                CreateCompilation(RoslynTestUtility.ReadTestCase(testCase)),
                out var output,
                out generatorDiagnostics
            );
        generatedTrees = driver.GetRunResult().GeneratedTrees;
        return output;
    }

    private static void AssertNoErrors(Compilation output)
    {
        var errors = output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error || d.Id == "CS8500")
            .Select(static d => d.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
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
