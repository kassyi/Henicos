#nullable enable
global using Kassyi.GSharp.Runtime.Aot;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Diagnostics.Kernel;
using Kassyi.GSharp.Tests.Shared.Differential;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Compiler.Marshaling
{
    internal static class AotModuleNames
    {
        public static string GetArtifactFileStem(string name) =>
            "Kassyi.GSharp.Kernels_" + string.Create(name.Length, name, static (span, src) =>
            {
                for (int i = 0; i < src.Length; i++)
                {
                    char c = src[i];
                    span[i] = c == '.' ? '_' : c;
                }
            });
    }
}

namespace Kassyi.GSharp.Tests.Shared.Differential
{
    public static class KnownDivergences
    {
        public static IReadOnlyList<KnownDivergence> All { get; } = [];
    }
}

namespace Kassyi.GSharp.Tools.FuzzDiff.Tests
{
    /// <summary>
    /// Shared Roslyn compilation, analyzer/generator driver, and delegate execution helpers for <c>tools/fuzz-diff</c> tests.
    /// </summary>
    internal static class FuzzTestCompilationHelper
    {
        internal static ImmutableArray<DiagnosticAnalyzer> KernelAnalyzers { get; } =
        [
            new GpuKernelAnalyzer(),
            new KernelClassAnalyzer(),
            new KernelDeclarationAnalyzer(),
            new BarrierUsageAnalyzer(),
            new ParallelDependentCallAnalyzer(),
        ];

        internal static string RenderKernelCompilationUnit(
            string className,
            IReadOnlyList<DeviceFunctionSpec> deviceFunctions,
            IReadOnlyList<string> kernelMethodSources,
            string? extraMemberSource = null)
        {
            using var writer = new SourceWriter();
            writer.AppendLine(
                """
                #nullable enable
                #pragma warning disable CS0162, CS0219, CS1717, CS1718
                using System;
                using Kassyi.GSharp.Core;

                namespace Kassyi.GSharp.Tests.Shared.Differential;
                """);
            writer.AppendLine();
            ScalarFuzzGenerator.AppendEnumDeclarations(writer);
            writer.AppendLine();
            FuzzCompositeModel.AppendCompositeStructDeclarations(writer);
            writer.AppendLine();

            using (writer.Scope($"public static partial class {className}"))
            {
                FuzzDeviceFunctionEmitter.AppendDeviceFunctionDeclarations(writer, deviceFunctions);
                writer.AppendLine();

                if (!string.IsNullOrWhiteSpace(extraMemberSource))
                {
                    writer.AppendLine(extraMemberSource);
                    writer.AppendLine();
                }

                for (int i = 0; i < kernelMethodSources.Count; i++)
                {
                    if (i > 0)
                    {
                        writer.AppendLine();
                    }

                    writer.AppendLine(kernelMethodSources[i]);
                }
            }

            return writer.ToString();
        }

        internal static string RenderNode(SyntaxNode node)
        {
            using var sw = new StringWriter();
            node.WriteTo(sw);
            return sw.ToString();
        }

        /// <summary>
        /// Compiles <paramref name="source"/> with the parse options and references of the tool
        /// (<see cref="MetamorphicTypeResolver.CreateCompilation"/>), under <paramref name="assemblyName"/>.
        /// </summary>
        internal static CSharpCompilation CreateCompilation(
            string source,
            string assemblyName,
            OptimizationLevel optimizationLevel,
            out CompilationUnitSyntax root)
        {
            var compilation = MetamorphicTypeResolver.CreateCompilation(source, out root, out _);
            return compilation
                .WithAssemblyName(assemblyName)
                .WithOptions(compilation.Options.WithOptimizationLevel(optimizationLevel));
        }

        internal static Assembly CompileToInMemoryAssembly(
            string assemblyName,
            string source,
            OptimizationLevel optimizationLevel = OptimizationLevel.Release)
        {
            var compilation = CreateCompilation(source, assemblyName, optimizationLevel, out _);

            using var peStream = new MemoryStream();
            var emitResult = compilation.Emit(peStream);
            if (!emitResult.Success)
            {
                var errorLines = new List<string>();
                foreach (var diag in emitResult.Diagnostics)
                {
                    if (diag.Severity == DiagnosticSeverity.Error)
                    {
                        errorLines.Add(diag.ToString());
                    }
                }

                throw new InvalidOperationException(
                    $"In-memory compilation failed:{Environment.NewLine}{string.Join(Environment.NewLine, errorLines)}");
            }

            peStream.Position = 0;
            return AssemblyLoadContext.Default.LoadFromStream(peStream);
        }

        internal static Action<int[], int[], int[], ulong[]> CreateKernelDelegate(Type kernelType, string methodName) =>
            CreateStaticDelegate<Action<int[], int[], int[], ulong[]>>(kernelType, methodName);

        internal static TDelegate CreateStaticDelegate<TDelegate>(Type type, string methodName)
            where TDelegate : Delegate
        {
#pragma warning disable RS0030 // Invokes a method of an assembly compiled at run time; no symbol exists to reference it
            return (type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"Missing method {methodName}.")).CreateDelegate<TDelegate>();
#pragma warning restore RS0030
        }

        internal static bool EvaluatesToMismatch(
            string caseName,
            Action<int[], int[], int[], ulong[]> oracleKernel,
            Action<int[], int[], int[], ulong[]> mutantKernel)
        {
            var testCase = new DifferentialCase($"FuzzMutantProbeKernels.{caseName}")
            {
                Shape = LaunchShapes.PartialWarp(rows: 32, blockX: 16),
                Inputs = static (rng, shape) =>
                {
                    int rows = shape.Rows;
                    var (inA, inB) = DiffValues.Int32Pairs(rng, rows);
                    var inC = DiffValues.Values(rng, rows, DiffValues.ShiftCounts, DiffValues.NextInt32);
                    return new DiffBuffers()
                        .In("inA", inA)
                        .In("inB", inB)
                        .In("inC", inC)
                        .Out<ulong>("checksum", rows, ["hash"], OutputPolicy.Exact);
                },
                Launch = (b, s) => SequentialOracle.RunEachThread(
                    s,
                    ThreadOrder.Forward,
                    () => mutantKernel(b.Get<int>("inA"), b.Get<int>("inB"), b.Get<int>("inC"), b.Get<ulong>("checksum"))),
                Oracle = (b, s) => SequentialOracle.RunEachThread(
                    s,
                    () => oracleKernel(
                        b.Get<int>("inA"),
                        b.Get<int>("inB"),
                        b.Get<int>("inC"),
                        b.Get<ulong>("checksum")
                    )
                ),
            };

            var report = DifferentialRunner.Evaluate(
                testCase,
                seeds: [0, 1],
                paths: [new DiffPathRunner(DiffPath.Native, DiffPaths.DirectRunner)],
                known: [],
                checkStale: false);

            return report.UnexpectedMismatches.Count > 0;
        }

        internal sealed class FuzzTestAnalyzerConfigOptionsProvider(string dialect) : AnalyzerConfigOptionsProvider
        {
            public override AnalyzerConfigOptions GlobalOptions { get; } = new FuzzTestAnalyzerConfigOptions(dialect);

            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
        }

        private sealed class FuzzTestAnalyzerConfigOptions(string dialect) : AnalyzerConfigOptions
        {
#pragma warning disable CS8765
            public override bool TryGetValue(string key, out string? value)
#pragma warning restore CS8765
            {
                switch (key)
                {
                    case "build_property.GSharpTargetDialect":
                        value = dialect;
                        return true;
                    case "build_property.GSharpExportDeviceCode":
                        value = "false";
                        return true;
                    case "build_property.GSharpGenerateSnapshotTestCases":
                        value = "false";
                        return true;
                    default:
                        value = null;
                        return false;
                }
            }
        }
    }
}
