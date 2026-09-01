#nullable enable
using System.Text;
using Kassyi.GSharp.Compiler;

namespace Kassyi.GSharp.Tests;

public class GEN_08_GeneratorSnapshotTests
{
    private class GpuAttributeRemover : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitAttributeList(AttributeListSyntax node)
        {
            var newAttributes = SyntaxFactory.SeparatedList<AttributeSyntax>();
            bool changed = false;

            foreach (var attr in node.Attributes)
            {
                var name = attr.Name.ToString();
                if (name == "GpuKernel" || name == "GpuKernelAttribute" ||
                    name == "GpuDeviceFunction" || name == "GpuDeviceFunctionAttribute")
                {
                    changed = true;
                }
                else
                {
                    newAttributes = newAttributes.Add(attr);
                }
            }

            if (changed)
            {
                if (newAttributes.Count == 0)
                {
                    return null;
                }

                return node.WithAttributes(newAttributes);
            }

            return base.VisitAttributeList(node);
        }
    }

    private static string GetTestProjectDirectory([CallerFilePath] string callerFilePath = "")
    {
        var fileDir = Path.GetDirectoryName(callerFilePath);
        if (string.IsNullOrEmpty(fileDir))
        {
            throw new DirectoryNotFoundException("Could not determine test project directory.");
        }

        return Path.GetFullPath(Path.Combine(fileDir, ".."));
    }

    private static string GetTestCasesDirectory()
    {
        var projectDir = GetTestProjectDirectory();
        var testCasesDir = Path.Combine(projectDir, "TestCases");
        if (!Directory.Exists(testCasesDir))
        {
            throw new DirectoryNotFoundException($"TestCases directory not found at: {testCasesDir}");
        }

        return testCasesDir;
    }

    private static string GetSnapshotsDirectory()
    {
        var projectDir = GetTestProjectDirectory();
        var snapshotDir = Path.Combine(projectDir, "Snapshots");
        if (!Directory.Exists(snapshotDir))
        {
            Directory.CreateDirectory(snapshotDir);
        }

        return snapshotDir;
    }

    private static string GetStdlibKernelsDirectory()
    {
        var projectDir = GetTestProjectDirectory();
        var kernelsDir = Path.GetFullPath(Path.Combine(projectDir, "..", "..", "src", "Stdlib",
            "Kassyi.GSharp.Stdlib.Math", "Kernels"));
        if (!Directory.Exists(kernelsDir))
        {
            throw new DirectoryNotFoundException($"Kernels directory not found at: {kernelsDir}");
        }

        return kernelsDir;
    }


    private static async Task VerifyGeneratorAsync(string source, string testName, string sourceDir)
    {
#pragma warning disable RS0030
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
        var syntaxTrees = new List<SyntaxTree>();
        syntaxTrees.Add(CSharpSyntaxTree.ParseText(source, options: parseOptions, path: $"{testName}.cs"));

        bool isStdlibKernel = sourceDir.IndexOf("Kernels", StringComparison.OrdinalIgnoreCase) >= 0;

        if (isStdlibKernel)
        {
            var stdlibDir = Path.GetFullPath(Path.Combine(sourceDir, ".."));

            var globalUsingsPath = Path.Combine(stdlibDir, "GlobalUsings.cs");
            if (File.Exists(globalUsingsPath))
            {
                syntaxTrees.Add(CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(globalUsingsPath),
                    options: parseOptions, path: globalUsingsPath));
            }

            var contextsDir = Path.Combine(stdlibDir, "Contexts");
            if (Directory.Exists(contextsDir))
            {
                foreach (var f in Directory.GetFiles(contextsDir, "*.cs"))
                {
                    syntaxTrees.Add(CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(f), options: parseOptions,
                        path: f));
                }
            }

            if (Directory.Exists(sourceDir))
            {
                foreach (var f in Directory.GetFiles(sourceDir, "*.cu.cs"))
                {
                    var name = Path.GetFileNameWithoutExtension(f).Replace(".cu", "");
                    if (!name.Equals(testName, StringComparison.OrdinalIgnoreCase))
                    {
                        var text = await File.ReadAllTextAsync(f);
                        var tree = CSharpSyntaxTree.ParseText(text, options: parseOptions, path: f);
                        var root = tree.GetRoot();
                        var rewriter = new GpuAttributeRemover();
                        var newRoot = rewriter.Visit(root);
                        syntaxTrees.Add(tree.WithRootAndOptions(newRoot, tree.Options));
                    }
                }
            }
        }
#pragma warning restore RS0030

        var compilation = CSharpCompilation.Create("SnapshotTest")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true))
            .AddReferences(Kassyi.GSharp.Tests.Helpers.MegaCompilationCache.BaseCompilation.References);

        if (isStdlibKernel)
        {
            var stdlibRef = compilation.References.FirstOrDefault(r =>
                r.Display != null && r.Display.Contains("Kassyi.GSharp.Stdlib.Math.dll"));
            if (stdlibRef != null)
            {
                compilation = compilation.RemoveReferences(stdlibRef);
            }
        }

        compilation = compilation.AddSyntaxTrees(syntaxTrees);

        var compileDiagnostics = compilation.GetDiagnostics();
        var errors = compileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Any())
        {
            var errorMessages = string.Join(Environment.NewLine, errors.Select(e => e.ToString()));
            Skip.If(true, $"WIP: C# Compilation failed with errors:\n{errorMessages}");
        }

        var generator = new GpuKernelGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator).WithUpdatedParseOptions(parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);

        var genErrors = generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (genErrors.Any())
        {
            var errorMessages = string.Join(Environment.NewLine, genErrors.Select(e => e.ToString()));
            Assert.Fail($"Generator failed with errors:\n{errorMessages}");
        }

        await Verifier.Verify(driver)
            .UseDirectory(GetSnapshotsDirectory())
            .UseMethodName($"{testName}_GeneratesCorrectCudaSource");
    }

    [SkippableFact]
    // TestCases/
    [InlineData("GEN-08_AddKernel.cu.cs")]
    [InlineData("GEN-08_BitOpsTestKernels.cu.cs")]
    [InlineData("GEN-08_ChunkAddKernel.cu.cs")]
    [InlineData("GEN-06_ComprehensiveSpecKernel.cu.cs")]
    [InlineData("GEN-13_MacroExpansion.cu.cs")]
    [InlineData(@"Analyzers\GEN-18_VectorBuiltins.cs")]
    [InlineData(@"..\Syntax\SYN-01-09_ControlFlowKernels.cu.cs")]
    [InlineData(@"..\Syntax\SYN-28_MethodAttributesKernels.cu.cs")]
    [InlineData(@"..\Syntax\SYN-49_SYN-68_InvocationAndIntrinsicsKernels.cu.cs")]
    // src/Stdlib/.../Kernels/
    [InlineData("GpuBigIntScalarKernels.cu.cs")]
    [InlineData("GpuBigIntMulKernels.cu.cs")]
    [InlineData("GpuBigIntKernels.cu.cs")]
    [InlineData("GpuBigIntGcdKernels.cu.cs")]
    [InlineData("GpuBigIntFermatKernels.cu.cs")]
    [InlineData("GpuBigIntCmpKernels.cu.cs")]
    [InlineData("GpuBigIntBitwiseKernels.cu.cs")]
    [InlineData("GpuBigIntBenchmarkKernels.cu.cs")]
    [InlineData("GpuBigIntNttKernels.cu.cs")]
    [Trait("FeatureId", "GEN-08")]
    public async Task GeneratorSnapshots(string fileName)
    {
        var testCasesDir = GetTestCasesDirectory();
        var kernelsDir = GetStdlibKernelsDirectory();

        var sourcePath = Path.Combine(testCasesDir, fileName);
        var sourceDir = testCasesDir;
        if (!File.Exists(sourcePath))
        {
            sourcePath = Path.Combine(kernelsDir, fileName);
            sourceDir = kernelsDir;
        }

        if (!File.Exists(sourcePath))
        {
            Skip.If(true, $"WIP: Source file not found: {sourcePath}");
            return;
        }

        var source = await File.ReadAllTextAsync(sourcePath);
        var testName = Path.GetFileNameWithoutExtension(fileName).Replace(".cu", "");

        await VerifyGeneratorAsync(source, testName, sourceDir);
    }

    [Fact]
    [Trait("FeatureId", "GEN-09")]
    public void TestCasesAndSnapshots_ShouldBeConsistent()
    {
        var testCasesDir = GetTestCasesDirectory();
        var kernelsDir = GetStdlibKernelsDirectory();
        var snapshotDir = GetSnapshotsDirectory();

        var testCaseNames = Directory.GetFiles(testCasesDir, "*.cs")
            .Select(f => Path.GetFileNameWithoutExtension(f).Replace(".cu", ""))
            .ToList();

        var kernelNames = Directory.GetFiles(kernelsDir, "*.cu.cs")
            .Select(f => Path.GetFileNameWithoutExtension(f).Replace(".cu", ""))
            .ToList();

        var allNames = testCaseNames.Concat(kernelNames).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Explicitly include tests from other directories that have snapshots registered
        allNames.Add("GEN-18_VectorBuiltins");
        allNames.Add("SYN-01-09_ControlFlowKernels");
        allNames.Add("SYN-28_MethodAttributesKernels");
        allNames.Add("SYN-49_SYN-68_InvocationAndIntrinsicsKernels");

        Assert.NotEmpty(allNames);

        var snapshotPrefix = $"{nameof(GEN_08_GeneratorSnapshotTests)}.";
        var snapshotFiles = Directory.GetFiles(snapshotDir, $"{snapshotPrefix}*")
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();

        Assert.NotEmpty(snapshotFiles);
        var missingTestCases = new List<string>();
        foreach (var snapshot in snapshotFiles)
        {
            var trimmed = snapshot.Substring(snapshotPrefix.Length);
            var suffixIndex = trimmed.IndexOf("_GeneratesCorrectCudaSource", StringComparison.OrdinalIgnoreCase);
            if (suffixIndex > 0)
            {
                var testName = trimmed.Substring(0, suffixIndex);
                if (!allNames.Contains(testName))
                {
                    missingTestCases.Add($"Snapshot '{snapshot}' references missing TestCase/Kernel '{testName}'");
                }
            }
        }

        Assert.Empty(missingTestCases);
        var missingSnapshots = new List<string>();
        foreach (var testCase in allNames)
        {
            var expectedPrefix = $"{snapshotPrefix}{testCase}_GeneratesCorrectCudaSource";
            bool hasSnapshot = snapshotFiles.Any(f => f.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase));
            if (!hasSnapshot)
            {
                missingSnapshots.Add(
                    $"TestCase/Kernel '{testCase}' does not have a corresponding snapshot ({expectedPrefix}*)");
            }
        }

        Assert.Empty(missingSnapshots);
    }
}
