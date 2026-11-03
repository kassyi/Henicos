#nullable enable
global using Kassyi.GSharp.Runtime.Aot;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Diagnostics.Kernel;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Differential;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Compiler.Marshaling
{
    internal static class AotHostNames
    {
        public static string ToSafeIdentifier(string name) =>
            string.Create(name.Length, name, static (span, src) =>
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
    public sealed record KnownDivergence(
        string Case,
        string Column,
        DiffPath Paths,
        int Issue,
        DiffHost Hosts = DiffHost.Any,
        Predicate<LaunchShape>? ShapePredicate = null)
    {
        public const string ExceptionColumn = "$exception";

        public bool AppliesToThisHost => (Hosts & (OperatingSystem.IsWindows() ? DiffHost.Windows : DiffHost.Unix)) != 0;

        public bool Covers(string caseName, string column, DiffPath path) =>
            Case == caseName && Column == column && (Paths & path) != 0 && ShapePredicate is null;

        public bool Covers(string caseName, string column, DiffPath path, LaunchShape shape) =>
            Case == caseName &&
            Column == column &&
            (Paths & path) != 0 &&
            (ShapePredicate is null || ShapePredicate(shape));

        public override string ToString() => $"\"{Column}\" on {DiffPaths.Format(Paths)} (#{Issue})";
    }

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
        private static readonly Lazy<IReadOnlyList<MetadataReference>> s_baseReferences = new(CreateBaseReferences);

        internal static CSharpParseOptions ParseOptions { get; } = new(LanguageVersion.Preview);

        internal static IReadOnlyList<MetadataReference> BaseReferences => s_baseReferences.Value;

        internal static ImmutableArray<DiagnosticAnalyzer> KernelAnalyzers { get; } =
        [
            new GpuKernelAnalyzer(),
            new KernelClassAnalyzer(),
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

        internal static Assembly CompileToInMemoryAssembly(
            string assemblyName,
            string source,
            OptimizationLevel optimizationLevel = OptimizationLevel.Release)
        {
            var tree = CSharpSyntaxTree.ParseText(source, ParseOptions, path: $"{assemblyName}.cu.cs");
            var compilation = CSharpCompilation.Create(
                assemblyName,
                [tree],
                BaseReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithAllowUnsafe(true)
                    .WithOptimizationLevel(optimizationLevel));

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

        internal static Action<int[], int[], int[], ulong[]> CreateKernelDelegate(Type kernelType, string methodName)
        {
            var method = kernelType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"Missing method {methodName}.");
            return method.CreateDelegate<Action<int[], int[], int[], ulong[]>>();
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
                Launch = (b, s) => SequentialOracle.Run(
                    s,
                    ThreadOrder.Forward,
                    () => mutantKernel(b.Get<int>("inA"), b.Get<int>("inB"), b.Get<int>("inC"), b.Get<ulong>("checksum"))),
                Oracle = (b, _) => oracleKernel(b.Get<int>("inA"), b.Get<int>("inB"), b.Get<int>("inC"), b.Get<ulong>("checksum")),
            };

            var report = DifferentialRunner.Evaluate(
                testCase,
                seeds: [0, 1],
                paths: [new DiffPathRunner(DiffPath.Native, DiffPaths.DirectRunner)],
                known: [],
                checkStale: false);

            return report.UnexpectedMismatches.Count > 0;
        }

        private static IReadOnlyList<MetadataReference> CreateBaseReferences()
        {
            string baseDir = AppContext.BaseDirectory;
            string trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
            var list = new List<MetadataReference>();
            int start = 0;
            for (int i = 0; i <= trusted.Length; i++)
            {
                if (i == trusted.Length || trusted[i] == Path.PathSeparator)
                {
                    if (i > start)
                    {
                        string path = trusted[start..i];
                        if (path.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                        {
                            list.Add(MetadataReference.CreateFromImage(File.ReadAllBytes(path)));
                        }
                        else
                        {
                            list.Add(MetadataReference.CreateFromFile(path));
                        }
                    }

                    start = i + 1;
                }
            }

            string corePath = typeof(Gpu).Assembly.Location;
            bool hasCore = false;
            foreach (var r in list)
            {
                if (string.Equals(r.Display, corePath, StringComparison.OrdinalIgnoreCase))
                {
                    hasCore = true;
                    break;
                }
            }

            if (!hasCore)
            {
                list.Add(MetadataReference.CreateFromImage(File.ReadAllBytes(corePath)));
            }

            return list;
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
