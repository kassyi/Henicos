#nullable enable
namespace Kassyi.GSharp.Tests;

public partial class GEN_08_GeneratorSnapshotTests : Kassyi.GSharp.Tests.Helpers.TranspilerTestBase
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
        var dir = new DirectoryInfo(Path.GetDirectoryName(callerFilePath)!);
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
            Assert.Fail($"C# Compilation failed with errors:\n{errorMessages}");
        }

        var generator = new GpuKernelGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: parseOptions,
            optionsProvider: new Kassyi.GSharp.Tests.Helpers.TestAnalyzerConfigOptionsProvider(DialectName));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);

        var genErrors = generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (genErrors.Any())
        {
            var errorMessages = string.Join(Environment.NewLine, genErrors.Select(e => e.ToString()));
            Assert.Fail($"Generator failed with errors:\n{errorMessages}");
        }

        await Verifier.Verify(driver)
            .UseDirectory(GetSnapshotsDirectory())
            .UseMethodName($"{testName}_{DialectName}_GeneratesCorrectCudaSource");
    }

    [SkippableTheory]
    [MemberData(nameof(GetSnapshotTestCases))]
    [Trait("FeatureId", "GEN-08")]
    public async Task Test_GEN08_GeneratorSnapshots(string relativeFilePath)
    {
        var rootDir = GetRepositoryRoot();
        var sourcePath = Path.Combine(rootDir, relativeFilePath);
        var sourceDir = Path.GetDirectoryName(sourcePath);

        if (!File.Exists(sourcePath))
        {
            Assert.Fail("Source file not found: " + sourcePath);
            return;
        }

        var source = await File.ReadAllTextAsync(sourcePath);
        var testName = Path.GetFileNameWithoutExtension(sourcePath).Replace(".cu", "");

        await VerifyGeneratorAsync(source, testName, sourceDir!);
    }

    [Fact]
    [Trait("FeatureId", "GEN-09")]
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
            .ToList();

        Assert.NotEmpty(snapshotFiles);

        string GetPureName(string name) => PureNameRegex().Replace(name, "");

        var missingSnapshots = new List<string>();
        foreach (var pureName in mandatorySnapshotKernels)
        {
            var cleanName = GetPureName(pureName);
            if (!snapshotFiles.Any(f => f.IndexOf(cleanName, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                missingSnapshots.Add("TestCase/Kernel '" + pureName + "' does not have any corresponding snapshot.");
            }
        }

        Assert.Empty(missingSnapshots);
    }

    [GeneratedRegex(@"^[A-Z]{3}-\d{2}(_\d{2})?_")]
    private static partial Regex PureNameRegex();
}