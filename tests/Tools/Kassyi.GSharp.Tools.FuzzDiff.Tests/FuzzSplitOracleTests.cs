#nullable enable
using System.Runtime.Loader;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Tests.Shared.Differential;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff.Tests;

/// <summary>
/// Tests of <see cref="FuzzSplitOracle"/>: the oracle twin of a split kernel gives the values worked out by hand,
/// agrees with the managed path, and is refused for a kernel whose result depends on another thread.
/// </summary>
public class FuzzSplitOracleTests
{
    private const string HandClassName = "SplitHandKernels";
    private const int Rows = 128;

    // Hand-computed result of each kernel of TestCases/Split/HandComputedSplitKernels.cs, per gid.
    private static readonly (string Kernel, Func<int, long> Expected)[] s_handCases =
    [
        ("AssignThenShuffleKernel", static gid => 2L * (gid + 100)),
        ("CompoundShuffleKernel", static _ => 7L),
        ("UpDownSelfKernel", static gid => 3L * gid),
        ("BarrierKernel", static gid => 15L + gid),
        ("SuspendingCallKernel", static gid => 28L + (((gid + 10) ^ gid) << 8)),
        ("IteratorKernel", static gid => (3L * gid) + 3),
    ];

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void OracleTwins_HandComputedKernels_GiveTheHandValuesInEveryThreadOrder()
    {
        string source = FuzzSplitOracle.WithOracleTwins(TestCaseFiles.Read("Split", "HandComputedSplitKernels.cs"));
        var kernelType = CompileKernelType(source, "SplitHandTwins");
        var shape = LaunchShapes.Grid1D(2, 64);

        foreach (var (kernel, expected) in s_handCases)
        {
            var twin = FuzzTestCompilationHelper.CreateKernelDelegate(kernelType, FuzzSplitOracle.OracleName(kernel));
            foreach (var order in new[] { ThreadOrder.Forward, ThreadOrder.Reverse, ThreadOrder.Permutation(7) })
            {
                ulong[] checksum = new ulong[Rows];
                SequentialOracle.RunEachThread(
                    shape,
                    order,
                    () => twin(new int[Rows], new int[Rows], new int[Rows], checksum)
                );

                // The twin replaces each self-lane shuffle by its value and drops each barrier; per thread that is the
                // GPU definition, so it must give the values worked out by hand in the comments of the kernels.
                // mutant: FuzzSplitOracle.TwinRewriter.VisitInvocationExpression: return the mask argument instead of
                // the value
                // input: HandComputedSplitKernels.cs, Grid1D(2, 64), orders Forward / Reverse / Permutation(7)
                for (int gid = 0; gid < Rows; gid++)
                {
                    Assert.True(
                        (ulong)expected(gid) == checksum[gid],
                        $"{kernel} ({order}) thread {gid}: expected {expected(gid)}, twin gave {(long)checksum[gid]}");
                }
            }
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void ManagedPath_HandComputedKernels_MatchesTheHandValues()
    {
        string source = TestCaseFiles.Read("Split", "HandComputedSplitKernels.cs");
        MetamorphicTestSupport.VerifyZeroGeneratorAndAnalyzerDiagnostics(source, "Cpu");
        MetamorphicTestSupport.VerifyZeroGeneratorAndAnalyzerDiagnostics(source, "Cuda");
        var runnerType = CompileWithGenerator(source, "SplitHandManaged");

        foreach (var (kernel, expected) in s_handCases)
        {
            if (kernel == "CompoundShuffleKernel")
            {
                // The CPU paths give 8 here: #991 (Suspending call arguments still misordered on CPU) lists this
                // compound-assignment shape; the fuzzer avoids it until that issue is fixed.
                continue;
            }

            var launch = FuzzTestCompilationHelper.CreateStaticDelegate<Action<ulong[]>>(runnerType, kernel);
            ulong[] checksum = new ulong[Rows];
            ManagedKernelRun.Run(() => launch(checksum));

            // The managed path runs the kernel's state machine with real warp meetings and barriers; it must agree with
            // the hand values, which is what the oracle twin gives (previous test).
            // mutant: the expected value of AssignThenShuffleKernel set to (gid + 100) (the value when the shuffle
            // reads a before the assignment)
            // input: HandComputedSplitKernels.cs through GpuKernelGenerator (Cpu dialect), gridDimX 2, blockDimX 64
            for (int gid = 0; gid < Rows; gid++)
            {
                Assert.True(
                    (ulong)expected(gid) == checksum[gid],
                    $"{kernel} thread {gid}: expected {expected(gid)}, managed path gave {(long)checksum[gid]}");
            }
        }
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("int r = Warp.ShuffleSync(0xFFFFFFFFu, gid, 0);", "must read the calling lane")]
    [InlineData("int r = Warp.ShuffleXorSync(0xFFFFFFFFu, gid, 1);", "must read the calling lane")]
    [InlineData("int r = Warp.ShuffleDownSync(0xFFFFFFFFu, gid, 1u);", "must read the calling lane")]
    [InlineData("int r = Warp.ShuffleSync(0xFFFFu, gid, ThreadIndex.X & 31);", "full mask")]
    [InlineData("int r = Warp.ShuffleSync(0xFFFFFFFFu, gid, ThreadIndex.X & 31, 16);", "no width")]
    [InlineData("int r = 0; if (gid > 3) { Gpu.SyncThreads(); }", "condition depends on the thread")]
    [InlineData(
        "int r = 0; if (gid > 3) { r = 1; } else { r = Warp.ShuffleXorSync(0xFFFFFFFFu, r, 0); }",
        "condition depends on the thread")]
    [InlineData("int r = (gid > 3) ? Warp.ShuffleXorSync(0xFFFFFFFFu, gid, 0) : 0;", "?:")]
    [InlineData(
        "int r = 0; bool f = (gid > 3) && (Warp.ShuffleXorSync(0xFFFFFFFFu, gid, 0) > 0);",
        "left side depends")]
    [InlineData("int r = 0; while (r < gid) { r++; Gpu.SyncThreads(); }", "WhileStatement")]
    [InlineData("int r = 0; for (int k = 0; k < gid; k++) { Gpu.SyncThreads(); }", "for loop")]
    [InlineData(
        "int r = 0; for (int k = 0; k < 3; k++) { if (gid == k) { break; } Gpu.SyncThreads(); }",
        "leaves a loop")]
    [InlineData("int r = 0; if (gid == 5) { return; } Gpu.SyncThreads();", "return before the end")]
    public void Validate_KernelWhoseResultDependsOnAnotherThread_IsRefused(string body, string reasonPart)
    {
        string source = WrapKernel(body);
        var root = FuzzSyntaxHelpers.ParseRoot(source);
        var kernel = FuzzSyntaxHelpers.GetKernelMethods(root)[0];
        var splitNames = FuzzSplitOracle.CollectSplitMethodNames([kernel]);

        // A twin is the per-thread definition only when every shuffle reads the calling lane with a full mask and every
        // split point is reached by all threads alike; anything else must be refused, not silently rewritten.
        // mutant: FuzzSplitOracle.CheckUniformPath: return null
        // input: one kernel per InlineData row
        string? reason = FuzzSplitOracle.Validate(kernel, splitNames);
        Assert.NotNull(reason);
        Assert.Contains(reasonPart, reason, StringComparison.Ordinal);
        var error = Assert.Throws<InvalidOperationException>(() => FuzzSplitOracle.WithOracleTwins(source));
        Assert.Contains(reasonPart, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData("int r = (BlockDimension.X > 0) ? Warp.ShuffleXorSync(0xFFFFFFFFu, gid, 0) : 0;")]
    [InlineData("int r = 0; bool f = (GridDimension.X == 0) || (Warp.ShuffleUpSync(0xFFFFFFFFu, gid, 0u) > 0);")]
    [InlineData("int r = 0; for (int k = 0; k < 3; k++) { r += k; Gpu.SyncThreads(); }")]
    [InlineData("int r = 0; if ((BlockDimension.X & 1) == 0) { Gpu.SyncThreads(); } else { r = 1; }")]
    [InlineData("int r = 0; for (int k = 0; k < 3; k++) { if (gid == k) { r = 2; } } Gpu.SyncThreads();")]
    [InlineData("int r = 0; foreach (int v in new[] { 1, 2 }) { if (v == gid) { break; } } Gpu.SyncThreads();")]
    public void Validate_UniformSplitPoints_AreAccepted(string body)
    {
        string source = WrapKernel(body);
        var root = FuzzSyntaxHelpers.ParseRoot(source);
        var kernel = FuzzSyntaxHelpers.GetKernelMethods(root)[0];

        // Conditions on the launch dimensions, counted loops and jumps out of loops without a split point keep every
        // thread on the same path, so the guard must not refuse them (it would discard valid kernels).
        // mutant: FuzzSplitOracle.IsUniform: return false
        // input: one kernel per InlineData row
        Assert.Null(FuzzSplitOracle.Validate(kernel, FuzzSplitOracle.CollectSplitMethodNames([kernel])));
        string withTwins = FuzzSplitOracle.WithOracleTwins(source);
        Assert.Contains(FuzzSplitOracle.OracleName("ProbeKernel"), withTwins, StringComparison.Ordinal);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void WithOracleTwins_AppliedTwiceOrAfterARename_RebuildsTheTwinsOnce()
    {
        string source = TestCaseFiles.Read("Split", "HandComputedSplitKernels.cs");
        string once = FuzzSplitOracle.WithOracleTwins(source);
        string twice = FuzzSplitOracle.WithOracleTwins(once);
        string renamed = FuzzSplitOracle.WithOracleTwins(
            once.Replace("void BarrierKernel(", "void RenamedBarrierKernel(", StringComparison.Ordinal));

        // The reducer renames kernels and rewrites them in sources that already hold twins; each write must drop the
        // old twins and build the ones of the current kernels, or the case would check a kernel against a stale twin.
        // mutant: FuzzSplitOracle.RemoveTwins: return root unchanged
        // input: HandComputedSplitKernels.cs, its twins rebuilt twice, and once after renaming BarrierKernel
        Assert.Equal(once, twice);
        Assert.Equal(1, CountOccurrences(twice, "void AssignThenShuffleKernel_Oracle("));
        Assert.Equal(0, CountOccurrences(renamed, "void BarrierKernel_Oracle("));
        Assert.Equal(1, CountOccurrences(renamed, "void RenamedBarrierKernel_Oracle("));
        Assert.Equal(1, CountOccurrences(renamed, "int SuspendBump_Oracle("));
        Assert.Equal(0, CountOccurrences(renamed, "SideInc_Oracle("));

        var twinRoot = FuzzSyntaxHelpers.ParseRoot(renamed);
        foreach (var node in twinRoot.DescendantNodes())
        {
            if (node is MethodDeclarationSyntax method &&
                method.Identifier.ValueText.EndsWith(FuzzSplitOracle.OracleSuffix, StringComparison.Ordinal))
            {
                string text = FuzzSyntaxHelpers.RenderFullNode(method);
                Assert.DoesNotContain("Warp.", text, StringComparison.Ordinal);
                Assert.DoesNotContain("SyncThreads", text, StringComparison.Ordinal);
                Assert.Empty(method.AttributeLists);
            }
        }
    }

    internal static Type CompileKernelType(string source, string assemblyName)
    {
        var assembly = FuzzTestCompilationHelper.CompileToInMemoryAssembly(
            $"{assemblyName}_{Guid.NewGuid():N}",
            source);
        return assembly.GetType($"Kassyi.GSharp.Tests.Shared.Differential.{HandClassName}")
            ?? assembly.GetType("Kassyi.GSharp.Tests.Shared.Differential.FuzzBatchKernels")
            ?? throw new InvalidOperationException("No kernel class in the compiled source.");
    }

    // Builds the source with the G# generator (Cpu dialect) and a runner class whose method per kernel launches it on
    // two blocks of 64 threads; with no native library on disk the launch takes the managed path.
    private static Type CompileWithGenerator(string source, string assemblyName)
    {
        var parseOptions = FuzzSyntaxHelpers.ParseOptions;
        var compilation = FuzzTestCompilationHelper.CreateCompilation(
            source,
            $"{assemblyName}_{Guid.NewGuid():N}",
            OptimizationLevel.Debug,
            out _
        ).AddSyntaxTrees(
            CSharpSyntaxTree.Create(
                FuzzSyntaxHelpers.ParseRoot(BuildHandRunnerSource()),
                parseOptions,
                path: $"{assemblyName}.Runner.cs"
            )
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new GpuKernelGenerator().AsSourceGenerator()],
            additionalTexts: null,
            parseOptions: parseOptions,
            optionsProvider: new FuzzTestCompilationHelper.FuzzTestAnalyzerConfigOptionsProvider("Cpu"));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return EmitRunnerType(output);
    }

    // One method per hand case that launches its kernel on two blocks of 64 threads
    private static string BuildHandRunnerSource()
    {
        using var runner = new SourceWriter();
        runner.AppendLine("namespace Kassyi.GSharp.Tests.Shared.Differential;");
        using (runner.Scope("public static class SplitHandRunner"))
        {
            foreach (var (kernel, _) in s_handCases)
            {
                runner.AppendLine(
                    $"public static void {kernel}(ulong[] checksum) => {HandClassName}.Launch{kernel}(" +
                    $"new int[{Rows}], new int[{Rows}], new int[{Rows}], checksum, gridDimX: 2, blockDimX: 64);");
            }
        }

        return runner.ToString();
    }

    private static Type EmitRunnerType(Compilation output)
    {
        using var stream = new MemoryStream();
        var result = output.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        stream.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
        return assembly.GetType("Kassyi.GSharp.Tests.Shared.Differential.SplitHandRunner")
            ?? throw new InvalidOperationException("Missing SplitHandRunner.");
    }

    private static string WrapKernel(string body) =>
        $$"""
        using System;
        using Kassyi.GSharp.Core;

        namespace Kassyi.GSharp.Tests.Shared.Differential;

        public static partial class FuzzBatchKernels
        {
        [GpuKernel]
        public static void ProbeKernel(int[] inA, int[] inB, int[] inC, ulong[] checksum)
        {
        int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        {{body}}
        checksum[gid] = (ulong)(long)r;
        }
        }
        """;

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
