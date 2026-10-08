#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Generator;

public partial class GeneratorSnapshotTests : Kassyi.GSharp.Tests.Shared.Helpers.TranspilerTestBase
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

    private static string GetRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string callerFilePath = "")
    {
        var dirPath = Path.GetDirectoryName(callerFilePath) ?? AppContext.BaseDirectory;
        var dir = new DirectoryInfo(dirPath);
        while (dir != null)
        {
            if (dir.GetFiles("Henicos.sln").Any() || dir.GetDirectories(".git").Any())
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static string GetSnapshotsDirectory()
    {
        var rootDir = GetRepositoryRoot();
        var snapshotDir = Path.Combine(rootDir, "tests", "Kassyi.GSharp.Tests.Shared", "Generator", "Snapshots");
        if (!Directory.Exists(snapshotDir))
        {
            Directory.CreateDirectory(snapshotDir);
        }

        return snapshotDir;
    }

    public static TheoryData<string> GetSnapshotTestCases() => SnapshotTestCaseSource.GetTestCases();

    private async Task VerifyGeneratorAsync(string source, string testName, string sourceDir)
    {
#pragma warning disable RS0030
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
#pragma warning restore RS0030
        var compilation = await BuildCompilationAsync(source, testName, sourceDir, parseOptions);
        var runResult = RunGenerator(compilation, parseOptions);
        await VerifySnapshotAsync(runResult, testName);
    }

    private async Task<CSharpCompilation> BuildCompilationAsync(
        string source, string testName, string sourceDir, CSharpParseOptions parseOptions)
    {
        bool isStdlibKernel = sourceDir.IndexOf("Kernels", StringComparison.OrdinalIgnoreCase) >= 0 &&
                              sourceDir.Contains("Stdlib");

        var syntaxTrees = await CollectSyntaxTreesAsync(source, testName, sourceDir, parseOptions, isStdlibKernel);

        var compilation = CSharpCompilation.Create("SnapshotTest")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true))
            .AddReferences(Kassyi.GSharp.Tests.Shared.Helpers.MegaCompilationCache.BaseCompilation.References);

        if (isStdlibKernel)
        {
            var stdlibRef = compilation.References.FirstOrDefault(r =>
                r.Display != null && r.Display.Contains("Kassyi.GSharp.Stdlib.GpuMath.dll"));
            if (stdlibRef != null)
            {
                compilation = compilation.RemoveReferences(stdlibRef);
            }
        }

        compilation = compilation.AddSyntaxTrees(syntaxTrees);

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795")
            .ToList();
        if (errors.Count > 0)
        {
            var errorMessages = string.Join(Environment.NewLine, errors.Select(e => e.ToString()));
            Assert.Fail($"C# Compilation failed with errors:\n{errorMessages}");
        }

        return compilation;
    }

    private static async Task<List<SyntaxTree>> CollectSyntaxTreesAsync(
        string source, string testName, string sourceDir, CSharpParseOptions parseOptions, bool isStdlibKernel)
    {
#pragma warning disable RS0030
        var syntaxTrees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(source, options: parseOptions, path: $"{testName}.cs")
        };

        var parentDir = Path.GetFullPath(Path.Combine(sourceDir, ".."));
        var globalUsingsPath = Path.Combine(parentDir, "GlobalUsings.cs");
        if (!File.Exists(globalUsingsPath))
        {
            globalUsingsPath = Path.Combine(parentDir, "..", "GlobalUsings.cs");
        }

        if (File.Exists(globalUsingsPath))
        {
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(
                await File.ReadAllTextAsync(globalUsingsPath),
                options: parseOptions,
                path: globalUsingsPath));
        }

        if (isStdlibKernel)
        {
            var contextsDir = Path.Combine(parentDir, "Contexts");
            if (!Directory.Exists(contextsDir))
            {
                contextsDir = Path.Combine(parentDir, "..", "Contexts");
            }

            if (Directory.Exists(contextsDir))
            {
                foreach (var f in Directory.GetFiles(contextsDir, "*.cs"))
                {
                    syntaxTrees.Add(CSharpSyntaxTree.ParseText(
                        await File.ReadAllTextAsync(f),
                        options: parseOptions,
                        path: f));
                }
            }

            if (Directory.Exists(sourceDir))
            {
                var rewriter = new GpuAttributeRemover();
                foreach (var f in Directory.GetFiles(sourceDir, "*.cu.cs"))
                {
                    var name = Path.GetFileNameWithoutExtension(f).Replace(".cu", "");
                    if (!name.Equals(testName, StringComparison.OrdinalIgnoreCase))
                    {
                        var text = await File.ReadAllTextAsync(f);
                        var tree = CSharpSyntaxTree.ParseText(text, options: parseOptions, path: f);
                        var newRoot = rewriter.Visit(tree.GetRoot());
                        syntaxTrees.Add(tree.WithRootAndOptions(newRoot, tree.Options));
                    }
                }
            }
        }
#pragma warning restore RS0030

        return syntaxTrees;
    }

    private GeneratorDriverRunResult RunGenerator(CSharpCompilation compilation, CSharpParseOptions parseOptions)
    {
        var generator = new GpuKernelGenerator();
        var cudaGenerator = new CudaImportGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new ISourceGenerator[] { generator.AsSourceGenerator(), cudaGenerator.AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: parseOptions,
            optionsProvider: new Kassyi.GSharp.Tests.Shared.Helpers.TestAnalyzerConfigOptionsProvider(DialectName));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);

        var genErrors = generatorDiagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (genErrors.Count > 0)
        {
            var errorMessages = string.Join(Environment.NewLine, genErrors.Select(e => e.ToString()));
            Assert.Fail($"Generator failed with errors:\n{errorMessages}");
        }

        return driver.GetRunResult();
    }

    private async Task VerifySnapshotAsync(GeneratorDriverRunResult runResult, string testName)
    {
        var nonSharedTrees = runResult.GeneratedTrees
            .Where(t => !t.FilePath.Contains("GSharpShared") || t.FilePath.Contains("DeviceCode"))
            .OrderBy(t => t.FilePath)
            .Select(t => $"// File: {Path.GetFileName(t.FilePath)}\n{t.GetText().ToString()}");
        var combinedSource = string.Join("\n\n", nonSharedTrees);

        await Verifier.Verify(combinedSource, "cs")
            .UseDirectory(GetSnapshotsDirectory())
            .UseFastHashSnapshotName(testName, DialectName);
    }

    [SkippableTheory]
    [MemberData(nameof(GetSnapshotTestCases))]
    public async Task Test_GeneratorSnapshots(string relativeFilePath)
    {
        var rootDir = GetRepositoryRoot();
        var sourcePath = Path.Combine(rootDir, relativeFilePath);
        var sourceDir = Path.GetDirectoryName(sourcePath) ?? rootDir;

        if (!File.Exists(sourcePath))
        {
            Assert.Fail("Source file not found: " + sourcePath);
            return;
        }

        var source = await File.ReadAllTextAsync(sourcePath);
        var testName = Path.GetFileNameWithoutExtension(sourcePath).Replace(".cu", "");

        await VerifyGeneratorAsync(source, testName, sourceDir);
    }

    [Fact]
    public async Task Test_SharedPreludeHeader_Snapshot()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var src = @"using Kassyi.GSharp.Core; public class Dummy { [GpuKernel] public static void Kernel() {} }";
        var syntaxTrees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(src, options: parseOptions) };
        var compilation = CSharpCompilation.Create("DummyCompilation")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true))
            .AddReferences(Kassyi.GSharp.Tests.Shared.Helpers.MegaCompilationCache.BaseCompilation.References)
            .AddSyntaxTrees(syntaxTrees);

        var generator = new GpuKernelGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: parseOptions,
            optionsProvider: new Kassyi.GSharp.Tests.Shared.Helpers.TestAnalyzerConfigOptionsProvider(DialectName));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var runResult = driver.GetRunResult();
        var sharedHeader = runResult.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("Prelude"))
            ?.GetText().ToString();

        Assert.NotNull(sharedHeader);
        Assert.NotEmpty(sharedHeader);

        await Verifier.Verify(sharedHeader, "cs")
            .UseDirectory(GetSnapshotsDirectory())
            .UseFileName($"Snap.SharedHeader_{DialectName}");
    }

    [Fact]
    public void TestCasesAndSnapshots_ShouldBeConsistent()
    {
        var rootDir = GetRepositoryRoot();
        var mandatorySnapshotKernels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in GetSnapshotTestCases())
        {
            var pureName = Path.GetFileNameWithoutExtension(relativePath).Replace(".cu", "");
            mandatorySnapshotKernels.Add(pureName);
        }

        var snapshotDirs = new[]
        {
            Path.Combine(rootDir, "tests", "Kassyi.GSharp.Tests.Shared", "Generator", "Snapshots"),
            Path.Combine(rootDir, "tests", "Kassyi.GSharp.Tests.Cpu", "Snapshots"),
            Path.Combine(rootDir, "tests", "Kassyi.GSharp.Tests.Cuda", "Snapshots")
        };

        var snapshotFiles = snapshotDirs
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.GetFiles(dir, "*.verified.cs", SearchOption.AllDirectories))
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();

        Assert.NotEmpty(snapshotFiles);

        var missingSnapshots = new List<string>();
        foreach (var pureName in mandatorySnapshotKernels)
        {
            var prefix = SnapshotNamingHelper.GetMeaningfulPrefix(pureName);
            var hash10 = HashHelper.ComputeShortHash(pureName);
            var expectedPattern = $"{prefix}_{hash10}";

            if (!snapshotFiles.Any(f => f.Contains(expectedPattern, StringComparison.OrdinalIgnoreCase)))
            {
                missingSnapshots.Add(
                    $"TestCase/Kernel '{pureName}' does not have any corresponding snapshot ({expectedPattern}).");
            }
        }

        Assert.Empty(missingSnapshots);
    }

    [GeneratedRegex(@"^[A-Z]{3}-\d{2}(_\d{2})?_")]
    private static partial Regex PureNameRegex();
}

