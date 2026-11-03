#nullable enable
global using System.Linq;
global using Kassyi.GSharp.Runtime.Aot;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
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
        public static string ToSafeIdentifier(string name) => name.Replace('.', '_');
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
    /// In-memory property and soundness tests for the differential kernel fuzzer (<see cref="ScalarFuzzGenerator"/>).
    /// </summary>
    public class ScalarFuzzGeneratorTests
    {
        private static readonly Lazy<IReadOnlyList<MetadataReference>> s_baseReferences = new(CreateBaseReferences);
        private static readonly CSharpParseOptions s_parseOptions = new(LanguageVersion.Preview);

        private static readonly ImmutableArray<DiagnosticAnalyzer> s_kernelAnalyzers =
        [
            new GpuKernelAnalyzer(),
            new KernelClassAnalyzer(),
            new BarrierUsageAnalyzer(),
            new ParallelDependentCallAnalyzer(),
        ];

        [Fact]
        [TestTarget(ExecutionTarget.CpuExecution)]
        [Category(TestCategory.Unit)]
        [Scenario(TestScenario.HappyPath)]
        public void FuzzGenerator_SameSeedAndSwarm_EmitsBitIdenticalSource()
        {
            for (ulong seed = 1UL; seed <= 16UL; seed++)
            {
                int size = 1 + (int)((seed - 1UL) % 6UL);
                var optionsA = new FuzzOptions
                {
                    FuzzSeed = seed,
                    InputSeed = (int)(seed % 5UL),
                    BatchSize = 4,
                    Size = size,
                };
                var optionsB = new FuzzOptions
                {
                    FuzzSeed = seed,
                    InputSeed = (int)(seed % 5UL),
                    BatchSize = 4,
                    Size = size,
                };

                var batchA = new ScalarFuzzGenerator(optionsA).GenerateBatch();
                var batchB = new ScalarFuzzGenerator(optionsB).GenerateBatch();

                // Two independent runs with identical (--fuzz-seed, --input-seed, size) must produce byte-identical kernel and case sources.
                // mutant: ScalarFuzzGenerator.GenerateBatch() line 48: var masterRng = new FuzzRandom(_options.FuzzSeed ^ (ulong)GetHashCode());
                // input: seed = 1UL .. 16UL, InputSeed = (int)(seed % 5UL), BatchSize = 4, Size = 1..6
                Assert.Equal(batchA.KernelsSource, batchB.KernelsSource);
                Assert.Equal(batchA.CasesSource, batchB.CasesSource);

                var nextBatch = new ScalarFuzzGenerator(optionsA with { FuzzSeed = seed + 100UL }).GenerateBatch();
                Assert.NotEqual(batchA.KernelsSource, nextBatch.KernelsSource);
            }
        }

        [Fact]
        [TestTarget(ExecutionTarget.CpuExecution)]
        [Category(TestCategory.Compiler)]
        [Scenario(TestScenario.Boundary)]
        public async Task FuzzGenerator_SwarmSweep100Seeds_ProducesZeroRoslynAndGSharpDiagnostics()
        {
            var sb = new StringBuilder();
            sb.AppendLine("#nullable enable");
            sb.AppendLine("#pragma warning disable CS0162, CS0219, CS1717, CS1718");
            sb.AppendLine("using System;");
            sb.AppendLine("using Kassyi.GSharp.Core;");
            sb.AppendLine();
            sb.AppendLine("namespace Kassyi.GSharp.Tests.Shared.Differential;");
            sb.AppendLine();
            ScalarFuzzGenerator.AppendEnumDeclarations(sb);
            sb.AppendLine();
            sb.AppendLine("public static partial class FuzzSweep100Kernels");
            sb.AppendLine("{");

            var generator = new ScalarFuzzGenerator(new FuzzOptions { FuzzSeed = 1UL, BatchSize = 1 });
            for (ulong seed = 0UL; seed < 100UL; seed++)
            {
                int size = 1 + (int)(seed % 8UL);
                var spec = generator.GenerateSingleKernel($"SweepKernel_{seed:D3}", seed, size);
                sb.AppendLine(spec.KernelMethodSource);
                sb.AppendLine();
            }

            sb.AppendLine("}");
            string combinedSource = sb.ToString();

            foreach (string dialect in new[] { "Cpu", "Cuda" })
            {
                var tree = CSharpSyntaxTree.ParseText(combinedSource, s_parseOptions, path: $"FuzzSweep100Kernels_{dialect}.cu.cs");
                var compilation = CSharpCompilation.Create(
                    $"FuzzSweep100_{dialect}",
                    [tree],
                    s_baseReferences.Value,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true));

                var configProvider = new FuzzTestAnalyzerConfigOptionsProvider(dialect);
                var analyzerOptions = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, configProvider);
                var analyzerDiags = await compilation
                    .WithAnalyzers(s_kernelAnalyzers, analyzerOptions)
                    .GetAnalyzerDiagnosticsAsync();

                GeneratorDriver driver = CSharpGeneratorDriver.Create(
                    generators: [new GpuKernelGenerator().AsSourceGenerator()],
                    additionalTexts: null,
                    parseOptions: s_parseOptions,
                    optionsProvider: configProvider);
                driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiags);

                var csErrors = outputCompilation
                    .GetDiagnostics()
                    .Where(static d => d.Severity == DiagnosticSeverity.Error)
                    .ToArray();
                var gshDiags = analyzerDiags
                    .Concat(generatorDiags)
                    .Concat(outputCompilation.GetDiagnostics())
                    .Where(static d => d.Id.StartsWith("GSH", StringComparison.Ordinal) &&
                                       d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
                    .ToArray();

                // Generated kernels across all 100 seeds must compile with zero C# errors and zero GSH* errors/warnings.
                // mutant: ScalarFuzzGenerator.EmitRandomStatement() line 340: state.Statements.Add($"var {newName} = 0;"); (triggers CS0029 on bool local)
                // input: seed = 0UL .. 99UL, dialect in ["Cpu", "Cuda"]
                Assert.Empty(csErrors);
                // mutant: ScalarFuzzGenerator.InitializeLocals() line 166: state.Statements.Add("object v_heap = new object();"); (triggers GSH0001)
                // input: seed = 0UL .. 99UL, dialect in ["Cpu", "Cuda"]
                Assert.Empty(gshDiags);
            }
        }

        [Fact]
        [TestTarget(ExecutionTarget.CpuExecution)]
        [Category(TestCategory.Compiler)]
        [Scenario(TestScenario.Boundary)]
        public void FuzzGenerator_DynamicGuards_NeverThrowOrRaceInSequentialAndLockstepOracles()
        {
            const int KernelCount = 50;
            var generator = new ScalarFuzzGenerator(new FuzzOptions { FuzzSeed = 0UL, BatchSize = 1 });
            var specs = new List<GeneratedKernelSpec>(KernelCount);

            var sb = new StringBuilder();
            sb.AppendLine("#nullable enable");
            sb.AppendLine("#pragma warning disable CS0162, CS0219");
            sb.AppendLine("using System;");
            sb.AppendLine("using Kassyi.GSharp.Core;");
            sb.AppendLine("namespace Kassyi.GSharp.Tests.Shared.Differential;");
            ScalarFuzzGenerator.AppendEnumDeclarations(sb);
            sb.AppendLine("public static partial class FuzzGuardTestKernels");
            sb.AppendLine("{");

            for (int i = 0; i < KernelCount; i++)
            {
                var spec = generator.GenerateSingleKernel($"GuardKernel_{i:D2}", (ulong)i, size: 1 + (i % 6));
                specs.Add(spec);
                sb.AppendLine(spec.KernelMethodSource);
            }

            sb.AppendLine("}");

            var assembly = CompileToInMemoryAssembly("FuzzGuardTestAssembly", sb.ToString());
            var kernelType = assembly.GetType("Kassyi.GSharp.Tests.Shared.Differential.FuzzGuardTestKernels")
                ?? throw new InvalidOperationException("Failed to resolve FuzzGuardTestKernels type.");

            var shape = LaunchShapes.PartialWarp(rows: 64, blockX: 32);
            int[] seeds = [0, 1, 2, 3, 4];

            foreach (var spec in specs)
            {
                var method = kernelType.GetMethod(spec.KernelName, BindingFlags.Public | BindingFlags.Static)
                    ?? throw new InvalidOperationException($"Missing kernel method {spec.KernelName}.");
                var action = method.CreateDelegate<Action<int[], int[], int[], ulong[]>>();

                foreach (int seed in seeds)
                {
                    var rng = new DiffRandom(seed);
                    var (inA, inB) = DiffValues.Int32Pairs(rng, shape.Rows);
                    var inC = DiffValues.Values(rng, shape.Rows, DiffValues.ShiftCounts, DiffValues.NextInt32);
                    if (seed == 0 && shape.Rows >= 4)
                    {
                        // Guarantee direct (int.MinValue, -1) and (int.MinValue, 0) coverage witnesses in the same row.
                        inA[0] = int.MinValue;
                        inB[0] = -1;
                        inA[1] = int.MinValue;
                        inB[1] = 0;
                    }

                    ulong[] forwardOut = new ulong[shape.Rows];
                    ulong[] reverseOut = new ulong[shape.Rows];
                    ulong[] permutedOut = new ulong[shape.Rows];

                    var forwardException = Record.Exception(() =>
                        SequentialOracle.Run(shape, ThreadOrder.Forward, () => action(inA, inB, inC, forwardOut)));
                    var reverseException = Record.Exception(() =>
                        SequentialOracle.Run(shape, ThreadOrder.Reverse, () => action(inA, inB, inC, reverseOut)));
                    var permutedException = Record.Exception(() =>
                        SequentialOracle.Run(shape, ThreadOrder.Permutation(seed), () => action(inA, inB, inC, permutedOut)));

                    // Every generated kernel must complete without DivideByZeroException / OverflowException and produce identical outputs across all thread orders.
                    // mutant: ScalarFuzzGenerator.BuildGuardedDivisionOrModulo() line 692: remove "|| (({leftExpr}) == int.MinValue && ({rightExpr}) == -1)" from int division guard
                    // input: GuardKernel_00 .. GuardKernel_49 with seed = 0 (inA[0] = int.MinValue, inB[0] = -1; inA[1] = int.MinValue, inB[1] = 0)
                    Assert.Null(forwardException);
                    Assert.Null(reverseException);
                    Assert.Null(permutedException);
                    Assert.Equal(forwardOut, reverseOut);
                    Assert.Equal(forwardOut, permutedOut);
                }
            }
        }

        [Fact]
        [TestTarget(ExecutionTarget.CpuExecution)]
        [Category(TestCategory.Compiler)]
        [Scenario(TestScenario.SadPath)]
        public void FuzzGenerator_PlantedTranspilerMutants_AreCaughtWithinFixedSeedBudget()
        {
            const int SeedBudget = 16;
            var generator = new ScalarFuzzGenerator(new FuzzOptions { FuzzSeed = 1UL, BatchSize = 1 });
            var specs = new List<GeneratedKernelSpec>(SeedBudget);

            var sb = new StringBuilder();
            sb.AppendLine("#nullable enable");
            sb.AppendLine("#pragma warning disable CS0162, CS0219");
            sb.AppendLine("using System;");
            sb.AppendLine("using Kassyi.GSharp.Core;");
            sb.AppendLine("namespace Kassyi.GSharp.Tests.Shared.Differential;");
            ScalarFuzzGenerator.AppendEnumDeclarations(sb);
            sb.AppendLine("public static partial class FuzzMutantProbeKernels");
            sb.AppendLine("{");

            for (int i = 1; i <= SeedBudget; i++)
            {
                var spec = generator.GenerateSingleKernel($"Probe_{i:D2}", (ulong)i, size: 4);
                specs.Add(spec);
                sb.AppendLine(spec.KernelMethodSource);
                sb.AppendLine(MutateDropShiftMask(spec.KernelMethodSource, $"Probe_{i:D2}", $"Probe_{i:D2}_MutShift"));
                sb.AppendLine(MutateReverseInterferingOperands(spec.KernelMethodSource, $"Probe_{i:D2}", $"Probe_{i:D2}_MutOrder"));
                sb.AppendLine(MutateCorruptNarrowCompound(spec.KernelMethodSource, $"Probe_{i:D2}", $"Probe_{i:D2}_MutNarrow"));
            }

            sb.AppendLine("}");

            var assembly = CompileToInMemoryAssembly("FuzzMutantProbeAssembly", sb.ToString());
            var kernelType = assembly.GetType("Kassyi.GSharp.Tests.Shared.Differential.FuzzMutantProbeKernels")
                ?? throw new InvalidOperationException("Failed to resolve FuzzMutantProbeKernels.");

            int caughtShiftMutant = 0;
            int caughtOrderMutant = 0;
            int caughtNarrowMutant = 0;

            for (int i = 1; i <= SeedBudget; i++)
            {
                string baseName = $"Probe_{i:D2}";
                var oracleDel = CreateKernelDelegate(kernelType, baseName);
                var shiftDel = CreateKernelDelegate(kernelType, $"{baseName}_MutShift");
                var orderDel = CreateKernelDelegate(kernelType, $"{baseName}_MutOrder");
                var narrowDel = CreateKernelDelegate(kernelType, $"{baseName}_MutNarrow");

                if (EvaluatesToMismatch(baseName, oracleDel, shiftDel))
                {
                    caughtShiftMutant++;
                }

                if (EvaluatesToMismatch(baseName, oracleDel, orderDel))
                {
                    caughtOrderMutant++;
                }

                if (EvaluatesToMismatch(baseName, oracleDel, narrowDel))
                {
                    caughtNarrowMutant++;
                }
            }

            // Within the fixed 16-seed budget, the generated kernels and FNV-1a checksum must catch all three planted transpiler mutants.
            // mutant: ScalarFuzzGenerator.EmitChecksumFoldSingle() line 747: $"hash = (hash ^ {siteId}UL) * {FnvPrime}UL;" (omitting local value bits from FNV-1a fold)
            // input: Probe_01 .. Probe_16 (seeds 1..16, size = 4) compared against _MutShift, _MutOrder, and _MutNarrow across DifferentialSeeds.Fixed
            Assert.True(caughtShiftMutant > 0, "Expected shift-mask mutant to be caught within 16 seeds.");
            Assert.True(caughtOrderMutant > 0, "Expected operand evaluation-order mutant to be caught within 16 seeds.");
            Assert.True(caughtNarrowMutant > 0, "Expected narrow compound-assignment mutant to be caught within 16 seeds.");
        }

        private static Action<int[], int[], int[], ulong[]> CreateKernelDelegate(Type kernelType, string methodName)
        {
            var method = kernelType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"Missing method {methodName}.");
            return method.CreateDelegate<Action<int[], int[], int[], ulong[]>>();
        }

        private static bool EvaluatesToMismatch(
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

        // Planted mutant 1: simulates C++ undefined/unmasked shift where counts >= 32 or < 0 yield 0 instead of C# `& 31` masking.
        private static string MutateDropShiftMask(string source, string originalName, string mutatedName)
        {
            string renamed = source.Replace($"void {originalName}(", $"void {mutatedName}(", StringComparison.Ordinal);
            return Regex.Replace(
                renamed,
                @"\((?<lhs>v\d+)\s*(?<op><<|>>>|>>)\s*(?<rhs>inC\[gid\]|\(inC\[gid\]\s*\+\s*\d+\))\)",
                static m =>
                {
                    string lhs = m.Groups["lhs"].Value;
                    string op = m.Groups["op"].Value;
                    string rhs = m.Groups["rhs"].Value;
                    return $"((uint)({rhs}) >= 32U ? 0 : ({lhs} {op} {rhs}))";
                });
        }

        // Planted mutant 2: reverses evaluation order of interfering binary operands right-to-left.
        private static string MutateReverseInterferingOperands(string source, string originalName, string mutatedName)
        {
            string renamed = source.Replace($"void {originalName}(", $"void {mutatedName}(", StringComparison.Ordinal);
            return ReverseInterferingLineInCSharp(renamed);
        }

        private static string ReverseInterferingLineInCSharp(string source)
        {
            return Regex.Replace(
                source,
                @"(?<target>v\d+)\s*=\s*\(\(\((?<left>v\d+\s*(?:\+=|\*)\s*\(\d+\))\)\s*(?<op>[+\-\^])\s*\((?<right>v\d+\s*(?:\+=|\*)\s*\(\d+\))\)\)\);",
                static m =>
                {
                    string target = m.Groups["target"].Value;
                    string left = m.Groups["left"].Value;
                    string op = m.Groups["op"].Value;
                    string right = m.Groups["right"].Value;
                    return $"{{ var __r = ({right}); var __l = ({left}); {target} = (__l {op} __r); }}";
                });
        }

        // Planted mutant 3: corrupts narrow integer compound assignment by dropping the low-bit contribution on narrowing.
        private static string MutateCorruptNarrowCompound(string source, string originalName, string mutatedName)
        {
            string renamed = source.Replace($"void {originalName}(", $"void {mutatedName}(", StringComparison.Ordinal);
            return Regex.Replace(
                renamed,
                @"(?<target>v\d+)\s*(?<op>\+=|-=|\*=|\^=)\s*\(\((?<type>sbyte|byte|short|ushort)\)\(\((?<ivar>v\d+)\s*\+=\s*5\)\s*\+\s*129\)\);",
                static m =>
                {
                    string target = m.Groups["target"].Value;
                    string type = m.Groups["type"].Value;
                    string ivar = m.Groups["ivar"].Value;
                    return $"{target} = ({type})((({ivar} += 5) + 129) ^ 0x55);";
                });
        }

        private static Assembly CompileToInMemoryAssembly(string assemblyName, string source)
        {
            var tree = CSharpSyntaxTree.ParseText(source, s_parseOptions, path: $"{assemblyName}.cu.cs");
            var compilation = CSharpCompilation.Create(
                assemblyName,
                [tree],
                s_baseReferences.Value,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithAllowUnsafe(true)
                    .WithOptimizationLevel(OptimizationLevel.Release));

            using var peStream = new MemoryStream();
            var emitResult = compilation.Emit(peStream);
            if (!emitResult.Success)
            {
                string errors = string.Join(
                    Environment.NewLine,
                    emitResult.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
                throw new InvalidOperationException($"In-memory compilation failed:{Environment.NewLine}{errors}");
            }

            peStream.Position = 0;
            return AssemblyLoadContext.Default.LoadFromStream(peStream);
        }

        private static IReadOnlyList<MetadataReference> CreateBaseReferences()
        {
            string baseDir = AppContext.BaseDirectory;
            string trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
            var list = new List<MetadataReference>();
            foreach (string path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (path.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(MetadataReference.CreateFromImage(File.ReadAllBytes(path)));
                }
                else
                {
                    list.Add(MetadataReference.CreateFromFile(path));
                }
            }

            string corePath = typeof(Gpu).Assembly.Location;
            if (!list.Any(r => string.Equals(r.Display, corePath, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(MetadataReference.CreateFromImage(File.ReadAllBytes(corePath)));
            }

            return list;
        }

        private sealed class FuzzTestAnalyzerConfigOptionsProvider(string dialect) : AnalyzerConfigOptionsProvider
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
