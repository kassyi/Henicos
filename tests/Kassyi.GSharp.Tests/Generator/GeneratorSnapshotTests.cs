#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VerifyXunit;
using Kassyi.GSharp.Compiler;
using Xunit;

namespace Kassyi.GSharp.Tests;

public class GeneratorSnapshotTests
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
                    name == "GpuDevice" || name == "GpuDeviceAttribute")
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
        var kernelsDir = Path.GetFullPath(Path.Combine(projectDir, "..", "..", "src", "Stdlib", "Kassyi.GSharp.Stdlib.Math", "Kernels"));
        if (!Directory.Exists(kernelsDir))
        {
            throw new DirectoryNotFoundException($"Kernels directory not found at: {kernelsDir}");
        }

        return kernelsDir;
    }

    private static MetadataReference[]? _cachedReferencesWithStdlib;
    private static MetadataReference[]? _cachedReferencesWithoutStdlib;
    private static readonly object _refsLock = new();

    private static MetadataReference[] GetReferences(bool includeStdlib)
    {
        lock (_refsLock)
        {
            if (includeStdlib && _cachedReferencesWithStdlib != null) return _cachedReferencesWithStdlib;
            if (!includeStdlib && _cachedReferencesWithoutStdlib != null) return _cachedReferencesWithoutStdlib;

            var refs = new List<MetadataReference>();
            var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var appDir = AppDomain.CurrentDomain.BaseDirectory;

            void SafeAddReference(string path)
            {
                if (!File.Exists(path)) return;
                try
                {
                    System.Reflection.AssemblyName.GetAssemblyName(path);
                    refs.Add(MetadataReference.CreateFromFile(path));
                }
                catch (BadImageFormatException)
                {
                    // Ignore native or invalid DLLs
                }
                catch (Exception)
                {
                    // Ignore locks or other IO errors
                }
            }

            // Core system libraries
            foreach (var dll in Directory.GetFiles(coreDir, "System*.dll"))
            {
                SafeAddReference(dll);
            }
            SafeAddReference(Path.Combine(coreDir, "mscorlib.dll"));
            SafeAddReference(Path.Combine(coreDir, "netstandard.dll"));

            // App dependencies
            foreach (var dll in Directory.GetFiles(appDir, "*.dll"))
            {
                if (!includeStdlib && Path.GetFileName(dll).Equals("Kassyi.GSharp.Stdlib.Math.dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                SafeAddReference(dll);
            }

            var result = refs.ToArray();
            if (includeStdlib) _cachedReferencesWithStdlib = result;
            else _cachedReferencesWithoutStdlib = result;

            return result;
        }
    }

    private static async Task VerifyGeneratorAsync(string source, string testName, string sourceDir)
    {
        var syntaxTrees = new List<SyntaxTree>();
        syntaxTrees.Add(CSharpSyntaxTree.ParseText(source, path: $"{testName}.cs"));

        bool isStdlibKernel = sourceDir.IndexOf("Kernels", StringComparison.OrdinalIgnoreCase) >= 0;

        if (isStdlibKernel)
        {
            var stdlibDir = Path.GetFullPath(Path.Combine(sourceDir, ".."));
            
            var globalUsingsPath = Path.Combine(stdlibDir, "GlobalUsings.cs");
            if (File.Exists(globalUsingsPath))
            {
                syntaxTrees.Add(CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(globalUsingsPath), path: globalUsingsPath));
            }

            var contextsDir = Path.Combine(stdlibDir, "Contexts");
            if (Directory.Exists(contextsDir))
            {
                foreach (var f in Directory.GetFiles(contextsDir, "*.cs"))
                {
                    syntaxTrees.Add(CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(f), path: f));
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
                        var tree = CSharpSyntaxTree.ParseText(text, path: f);
                        var root = tree.GetRoot();
                        var rewriter = new GpuAttributeRemover();
                        var newRoot = rewriter.Visit(root);
                        syntaxTrees.Add(tree.WithRootAndOptions(newRoot, tree.Options));
                    }
                }
            }
        }

        var refs = GetReferences(!isStdlibKernel);
        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            syntaxTrees,
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true));

        var compileDiagnostics = compilation.GetDiagnostics();
        var errors = compileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Any())
        {
            var errorMessages = string.Join(Environment.NewLine, errors.Select(e => e.ToString()));
            var loadedRefs = string.Join(Environment.NewLine, refs.Select(r => r.Display));
            Assert.Fail($"Compilation failed with errors:\n{errorMessages}\n\nLoaded References:\n{loadedRefs}");
        }

        var generator = new GpuKernelGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

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

    [Theory]
    // TestCases/
    [InlineData("GEN-08_AddKernel.cu.cs")]
    [InlineData("GEN-08_BitOpsTestKernels.cu.cs")]
    [InlineData("GEN-08_ChunkAddKernel.cu.cs")]
    [InlineData("GEN-06_ComprehensiveSpecKernel.cu.cs")]
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

        Assert.NotEmpty(allNames);

        var snapshotPrefix = $"{nameof(GeneratorSnapshotTests)}.";
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
                missingSnapshots.Add($"TestCase/Kernel '{testCase}' does not have a corresponding snapshot ({expectedPrefix}*)");
            }
        }

        Assert.Empty(missingSnapshots);
    }
}
