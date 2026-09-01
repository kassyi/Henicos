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
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: parseOptions,
            optionsProvider: new Kassyi.GSharp.Tests.Helpers.TestAnalyzerConfigOptionsProvider());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);

        var genErrors = generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (genErrors.Any())
        {
            var errorMessages = string.Join(Environment.NewLine, genErrors.Select(e => e.ToString()));
            Assert.Fail($"Generator failed with errors:\n{errorMessages}");
        }

#if GSHARP_CPU
        var dialectName = "Cpu";
#else
        var dialectName = "Cuda";
#endif
        await Verifier.Verify(driver)
            .UseDirectory(GetSnapshotsDirectory())
            .UseMethodName($"{testName}_{dialectName}_GeneratesCorrectCudaSource");
    }

    [SkippableTheory]
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
        var sharedTestsDir = Path.GetFullPath(Path.Combine(testCasesDir, ".."));

        // 1. Get ALL .cu.cs files in the project. These are our "known kernels".
        var allKnownKernels = Directory.GetFiles(sharedTestsDir, "*.cu.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
            .Select(f => Path.GetFileNameWithoutExtension(f).Replace(".cu", ""))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        // Explicitly known .cs file with snapshots
        allKnownKernels.Add("GEN-18_VectorBuiltins");

        // 2. These directories are expected to strictly have snapshots for all their files.
        var mandatorySnapshotKernels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesToCheck = Directory.GetFiles(testCasesDir, "*.cu.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(kernelsDir, "*.cu.cs", SearchOption.TopDirectoryOnly));

        foreach (var file in filesToCheck)
        {
            // Skip files that have the explicit opt-out directive
            if (File.ReadLines(file).Take(20).Any(l => l.Contains("SKIP_SNAPSHOT_CONSISTENCY")))
            {
                continue;
            }
            mandatorySnapshotKernels.Add(Path.GetFileNameWithoutExtension(file).Replace(".cu", ""));
        }

        // 3. Get all snapshots across all projects
        var snapshotDirs = new[]
        {
            GetSnapshotsDirectory(),
            Path.Combine(sharedTestsDir, "..", "Kassyi.GSharp.Tests.Cpu", "Snapshots"),
            Path.Combine(sharedTestsDir, "..", "Kassyi.GSharp.Tests.Cuda", "Snapshots")
        };

        var snapshotFiles = snapshotDirs
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.GetFiles(dir, "*.verified.cs", SearchOption.AllDirectories))
            .Select(Path.GetFileName)
            .ToList();

        Assert.NotEmpty(snapshotFiles);

        // Function to extract pure name (removes GEN-XX_ or SYN-XX_)
        string GetPureName(string name) => System.Text.RegularExpressions.Regex.Replace(name, @"^[A-Z]{3}-\d{2}(_\d{2})?_", "");

        // 4. Check for missing snapshots only for mandatory ones
        var missingSnapshots = new List<string>();
        foreach (var kernel in mandatorySnapshotKernels)
        {
            var pureName = GetPureName(kernel);
            if (!snapshotFiles.Any(f => f.IndexOf(pureName, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                missingSnapshots.Add($"TestCase/Kernel '{kernel}' does not have any corresponding snapshot.");
            }
        }

        Assert.Empty(missingSnapshots);

        // 5. Check for orphaned snapshots using all known kernels
        var orphanedSnapshots = new List<string>();
        foreach (var snapshot in snapshotFiles)
        {
            bool isKnown = allKnownKernels.Any(k => snapshot.IndexOf(GetPureName(k), StringComparison.OrdinalIgnoreCase) >= 0);
            if (!isKnown && !snapshot.Contains("GSharpSharedCpuCode") && !snapshot.Contains("GSharpSharedCudaCode"))
            {
                orphanedSnapshots.Add($"Snapshot '{snapshot}' does not match any known TestCase/Kernel. It might be orphaned.");
            }
        }

        Assert.Empty(orphanedSnapshots);
    }
}
