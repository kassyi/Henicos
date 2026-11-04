#nullable enable
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Tests.Shared.Differential;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tools.FuzzDiff.Tests;

/// <summary>
/// Reads the kernel sources under <c>TestCases/Metamorphic</c> and checks that a rewritten kernel still compiles
/// and computes what the original computes.
/// </summary>
internal static class MetamorphicTestSupport
{
    /// <summary>Reads a kernel source from <c>TestCases/Metamorphic</c>.</summary>
    internal static string ReadTestCase(string fileName) => TestCaseFiles.Read("Metamorphic", fileName);

    /// <summary>
    /// Asserts that <paramref name="afterSource"/> builds without G# diagnostics on both dialects and that the kernel
    /// <paramref name="methodName"/> writes the same checksums as in <paramref name="beforeSource"/>.
    /// </summary>
    internal static void AssertCompilesAndMatchesRuntime(
        string beforeSource,
        string afterSource,
        string className,
        string methodName)
    {
        VerifyZeroGeneratorAndAnalyzerDiagnostics(beforeSource, "Cpu");
        VerifyZeroGeneratorAndAnalyzerDiagnostics(afterSource, "Cpu");
        VerifyZeroGeneratorAndAnalyzerDiagnostics(afterSource, "Cuda");

        string uniqueTag = Guid.NewGuid().ToString("N")[..8];
        var beforeAsm = FuzzTestCompilationHelper.CompileToInMemoryAssembly($"{className}_Before_{uniqueTag}", beforeSource);
        var afterAsm = FuzzTestCompilationHelper.CompileToInMemoryAssembly($"{className}_After_{uniqueTag}", afterSource);

        var beforeType = beforeAsm.GetType($"Kassyi.GSharp.Tests.Shared.Differential.{className}");
        var afterType = afterAsm.GetType($"Kassyi.GSharp.Tests.Shared.Differential.{className}");
        Assert.NotNull(beforeType);
        Assert.NotNull(afterType);

        var beforeDel = FuzzTestCompilationHelper.CreateKernelDelegate(beforeType, methodName);
        var afterDel = FuzzTestCompilationHelper.CreateKernelDelegate(afterType, methodName);

        var testCase = new DifferentialCase($"{className}.{methodName}")
        {
            Shape = LaunchShapes.PartialWarp(rows: 32, blockX: 16),
            Inputs = static (rng, shape) =>
            {
                int rows = shape.Rows;
                var (inA, inB) = DiffValues.Int32Pairs(rng, rows);
                if (inB.Length > 0)
                {
                    inB[0] = 0;
                }

                var inC = DiffValues.Values(rng, rows, DiffValues.ShiftCounts, DiffValues.NextInt32);
                return new DiffBuffers()
                    .In("inA", inA)
                    .In("inB", inB)
                    .In("inC", inC)
                    .Out<ulong>("checksum", rows, ["hash"], OutputPolicy.Exact);
            },
            Launch = (b, s) => SequentialOracle.Run(
                s,
                ThreadOrder.Forward,
                () => afterDel(b.Get<int>("inA"), b.Get<int>("inB"), b.Get<int>("inC"), b.Get<ulong>("checksum"))),
            Oracle = (b, _) => beforeDel(b.Get<int>("inA"), b.Get<int>("inB"), b.Get<int>("inC"), b.Get<ulong>("checksum")),
        };

        var report = DifferentialRunner.Evaluate(
            testCase,
            seeds: DifferentialSeeds.Fixed,
            paths: [new DiffPathRunner(DiffPath.Native, DiffPaths.DirectRunner)],
            known: [],
            checkStale: false);

        Assert.Empty(report.UnexpectedMismatches);
    }

    /// <summary>Asserts that <paramref name="source"/> compiles as C# without errors.</summary>
    internal static void AssertNoCompileErrors(string source)
    {
        var compilation = CreateCompilation(source, "MetaCompileCheck");
        foreach (var d in compilation.GetDiagnostics())
        {
            Assert.False(d.Severity == DiagnosticSeverity.Error, $"Compilation error: {d}{Environment.NewLine}{source}");
        }
    }

    /// <summary>
    /// Asserts that the G# generator and the kernel analyzers report no error or warning for <paramref name="source"/>
    /// built for <paramref name="dialect"/> (<c>Cpu</c> or <c>Cuda</c>), and that the generated output compiles.
    /// </summary>
    internal static void VerifyZeroGeneratorAndAnalyzerDiagnostics(string source, string dialect)
    {
        var compilation = CreateCompilation(source, $"MetaVerify_{dialect}");
        var configProvider = new FuzzTestCompilationHelper.FuzzTestAnalyzerConfigOptionsProvider(dialect);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new GpuKernelGenerator().AsSourceGenerator()],
            additionalTexts: null,
            parseOptions: FuzzTestCompilationHelper.ParseOptions,
            optionsProvider: configProvider);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiags);

        foreach (var d in generatorDiags)
        {
            Assert.False(d.Severity == DiagnosticSeverity.Error, $"Generator error ({dialect}): {d}");
        }

        foreach (var d in outputCompilation.GetDiagnostics())
        {
            if (d.Severity == DiagnosticSeverity.Error)
            {
                string[] lines = source.Split('\n');
                int errLine = d.Location.GetLineSpan().StartLinePosition.Line;
                string snippet = errLine >= 0 && errLine < lines.Length ? lines[errLine] : string.Empty;
                Assert.Fail($"Compilation error ({dialect}): {d} | Line {errLine + 1}: {snippet}");
            }
        }

        var analyzerOptions = new AnalyzerOptions([], configProvider);
        var withAnalyzers = compilation.WithAnalyzers(FuzzTestCompilationHelper.KernelAnalyzers, analyzerOptions);
        var analyzerDiags = withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
        foreach (var d in analyzerDiags)
        {
            if (d.Id.StartsWith("GSH", StringComparison.Ordinal) &&
                d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            {
                Assert.Fail($"Unexpected G# analyzer diagnostic ({dialect}): {d}");
            }
        }
    }

    private static CSharpCompilation CreateCompilation(string source, string assemblyName)
    {
        var tree = CSharpSyntaxTree.ParseText(source, FuzzTestCompilationHelper.ParseOptions, path: $"{assemblyName}.cu.cs");
        return CSharpCompilation.Create(
            assemblyName,
            [tree],
            FuzzTestCompilationHelper.BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithAllowUnsafe(true)
                .WithOptimizationLevel(OptimizationLevel.Release));
    }
}
