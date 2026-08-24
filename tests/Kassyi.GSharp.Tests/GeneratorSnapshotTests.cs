using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VerifyXunit;
using Kassyi.GSharp.Compiler;
using Xunit;

namespace Kassyi.GSharp.Tests;

public class GeneratorSnapshotTests
{
    private static string GetTestCasesDirectory([CallerFilePath] string callerFilePath = "")
    {
        var dir = Path.GetDirectoryName(callerFilePath);
        if (string.IsNullOrEmpty(dir))
        {
            throw new DirectoryNotFoundException("Could not determine test project directory.");
        }

        var testCasesDir = Path.Combine(dir, "TestCases");
        if (!Directory.Exists(testCasesDir))
        {
            throw new DirectoryNotFoundException($"TestCases directory not found at: {testCasesDir}");
        }

        return testCasesDir;
    }

    private static string GetSnapshotsDirectory([CallerFilePath] string callerFilePath = "")
    {
        var dir = Path.GetDirectoryName(callerFilePath);
        if (string.IsNullOrEmpty(dir))
        {
            throw new DirectoryNotFoundException("Could not determine test project directory.");
        }

        var snapshotDir = Path.Combine(dir, "Snapshots");
        if (!Directory.Exists(snapshotDir))
        {
            Directory.CreateDirectory(snapshotDir);
        }

        return snapshotDir;
    }

    private static string GetStdlibKernelsDirectory([CallerFilePath] string callerFilePath = "")
    {
        var dir = Path.GetDirectoryName(callerFilePath);
        if (string.IsNullOrEmpty(dir))
        {
            throw new DirectoryNotFoundException("Could not determine test project directory.");
        }

        var kernelsDir = Path.GetFullPath(Path.Combine(dir, "..", "..", "src", "Stdlib", "Kassyi.GSharp.Stdlib.Math", "Kernels"));
        if (!Directory.Exists(kernelsDir))
        {
            throw new DirectoryNotFoundException($"Kernels directory not found at: {kernelsDir}");
        }

        return kernelsDir;
    }

    private static async Task VerifyGeneratorAsync(string source, string testName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Kassyi.GSharp.Core.GpuKernelAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Kassyi.GSharp.Core.ThreadIndex).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true));

        var generator = new GpuKernelGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        await Verifier.Verify(driver)
            .UseDirectory("Snapshots")
            .UseMethodName($"{testName}_GeneratesCorrectCudaSource");
    }

    [Theory]
    // TestCases/
    [InlineData("AddKernel.cs")]
    [InlineData("BitOpsTestKernels.cs")]
    [InlineData("ChunkAddKernel.cs")]
    [InlineData("ComprehensiveSpecKernel.cs")]
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
    public async Task GeneratorSnapshots(string fileName)
    {
        var testCasesDir = GetTestCasesDirectory();
        var kernelsDir = GetStdlibKernelsDirectory();
        
        var sourcePath = Path.Combine(testCasesDir, fileName);
        if (!File.Exists(sourcePath))
        {
            sourcePath = Path.Combine(kernelsDir, fileName);
        }

        var source = await File.ReadAllTextAsync(sourcePath);
        var testName = Path.GetFileNameWithoutExtension(fileName).Replace(".cu", "");

        await VerifyGeneratorAsync(source, testName);
    }

    [Fact]
    public void TestCasesAndSnapshots_ShouldBeConsistent()
    {
        var testCasesDir = GetTestCasesDirectory();
        var kernelsDir = GetStdlibKernelsDirectory();
        var snapshotDir = GetSnapshotsDirectory();

        var testCaseNames = Directory.GetFiles(testCasesDir, "*.cs")
            .Select(Path.GetFileNameWithoutExtension)
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
            var testName = trimmed.Split(new[] { '_', '.', '#' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrEmpty(testName) && !allNames.Contains(testName))
            {
                missingTestCases.Add($"Snapshot '{snapshot}' references missing TestCase/Kernel '{testName}'");
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
