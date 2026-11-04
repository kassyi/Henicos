#nullable enable
using static Kassyi.GSharp.Tools.FuzzDiff.Tests.MetamorphicTestSupport;

namespace Kassyi.GSharp.Tools.FuzzDiff.Tests;

/// <summary>
/// Tests that each metamorphic rewrite of <see cref="FuzzMetamorphicRewriter"/> applies to a kernel that allows it and
/// keeps what the kernel computes, and that <c>--metamorphic</c> batches add variants that match their base kernel.
/// The forms each rewrite must leave unchanged are in <see cref="FuzzMetamorphicRejectionTests"/>.
/// </summary>
public class FuzzMetamorphicTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void RenameLocals_LocalsAndParameters_AreRenamedToCppKeywordsAndPreludeNames()
    {
        string source = ReadTestCase("RenameLocals.cs");

        string renamed = FuzzMetamorphicRewriter.RenameLocalsAndParameters(source, seed: 0x1138_01UL, targetMethodName: "KernelRename");

        // Locals and input parameters get C++ keyword or Prelude names; the struct field names after `.` stay.
        // mutant: MetamorphicIdentifierRenamer: drop the `IsMemberAccessRight` guard when replacing references
        // input: locals alpha, beta, pair, combined; parameters inA, inB, inC; field accesses pair.First, pair.Second
        Assert.DoesNotContain("int alpha =", renamed, StringComparison.Ordinal);
        Assert.DoesNotContain("int beta =", renamed, StringComparison.Ordinal);
        Assert.Contains(".First", renamed, StringComparison.Ordinal);
        Assert.Contains(".Second", renamed, StringComparison.Ordinal);
        Assert.Contains(
            FuzzMetamorphicRewriter.CandidateIdentifiers,
            candidate => renamed.Contains($"int {candidate} =", StringComparison.Ordinal));
        AssertCompilesAndMatchesRuntime(source, renamed, "MetaRenameKernels", "KernelRename");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void ExtractSubexpression_ToLocal_DeclaresTheOperandBeforeItsStatement()
    {
        string source = ReadTestCase("ExtractSubexpression.cs");

        string extracted = FuzzMetamorphicRewriter.ExtractPureSubexpressionToLocal(source, seed: 0x1138_02UL, targetMethodName: "KernelExtract");

        // mutant: MetamorphicPurity.IsOnUnconditionalEvaluationPath returns true, so `x / y` leaves its guard
        // input: `((x ^ 0x13579BDF) + (y * 3)) ^ inC[gid]` and `(y != 0 && y != int.MinValue) ? (x / y) : 0`
        Assert.Contains("metaExtracted0 =", extracted, StringComparison.Ordinal);
        Assert.Contains("? (x / y) : 0", extracted, StringComparison.Ordinal);
        AssertCompilesAndMatchesRuntime(source, extracted, "MetaExtractKernels", "KernelExtract");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void ExtractSubexpression_ToDeviceFunction_CallsTheNewHelper()
    {
        string source = ReadTestCase("ExtractSubexpression.cs");

        string extracted = FuzzMetamorphicRewriter.ExtractPureSubexpressionToDeviceFunction(
            source,
            seed: 0x1138_02UL,
            targetMethodName: "KernelExtract",
            helperMethodName: "MetaExtractedHelper_0");

        // mutant: MetamorphicSubexpressionExtractor.ExtractToDeviceFunction does not insert the helper member
        // input: the same kernel as ExtractSubexpression_ToLocal
        Assert.Contains("[GpuDeviceFunction]", extracted, StringComparison.Ordinal);
        Assert.Contains("MetaExtractedHelper_0(", extracted, StringComparison.Ordinal);
        AssertCompilesAndMatchesRuntime(source, extracted, "MetaExtractKernels", "KernelExtract");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void InlineDeviceFunction_PureArguments_ReplacesTheCallWithTheFoldedBody()
    {
        string source = ReadTestCase("InlineDeviceFunction.cs");

        string inlined = FuzzMetamorphicRewriter.InlineDeviceFunctionCall(source, seed: 0x1138_03UL, targetMethodName: "KernelInline");

        // MixHelper declares a local and returns an expression of it; the body folds into one expression.
        // mutant: PureBodyFolder.FoldDeclaration does not record the local, so `step` stays unbound in the result
        // input: `MixHelper(v0 + 7, v1)` with `int step = (a ^ 0x5555AAAA) + b; return (step * 3) ^ a;`
        Assert.DoesNotContain("MixHelper(v0 + 7, v1)", inlined, StringComparison.Ordinal);
        AssertCompilesAndMatchesRuntime(source, inlined, "MetaInlineKernels", "KernelInline");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void ReorderStatements_DisjointPair_IsSwapped()
    {
        string source = ReadTestCase("ReorderStatements.cs");

        string reordered = FuzzMetamorphicRewriter.ReorderIndependentStatements(source, seed: 0x1138_04UL, targetMethodName: "KernelReorder");

        // mutant: MetamorphicStatementEffects.AreIndependent returns false for every pair
        // input: `int a = inA[gid] + 11;` and `int b = inB[gid] ^ 29;`, which share no local
        int idxB = reordered.IndexOf("int b = inB[gid] ^ 29;", StringComparison.Ordinal);
        int idxA = reordered.IndexOf("int a = inA[gid] + 11;", StringComparison.Ordinal);
        Assert.True(idxB >= 0 && idxA > idxB, $"Expected `int b` before `int a`:{Environment.NewLine}{reordered}");
        AssertCompilesAndMatchesRuntime(source, reordered, "MetaReorderKernels", "KernelReorder");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void SwapOperands_PureIntegerAddition_IsSwapped()
    {
        string source = ReadTestCase("SwapOperands.cs");

        string swapped = FuzzMetamorphicRewriter.SwapCommutativeOperands(source, seed: 0x1138_05UL, targetMethodName: "KernelCommutative");

        // mutant: MetamorphicCommutativeSwapper.IsCommutativeBinaryKind leaves out AddExpression
        // input: `(x - 19) + (y << 2)`, whose operands are not commutative themselves
        Assert.Contains("(y << 2) + (x - 19)", swapped, StringComparison.Ordinal);
        AssertCompilesAndMatchesRuntime(source, swapped, "MetaCommutativeKernels", "KernelCommutative");
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(MetamorphicWrapKind.Block, "{\nacc = (acc + inB[gid]) ^ inC[gid];\n}")]
    [InlineData(MetamorphicWrapKind.IfTrue, "if (true)")]
    [InlineData(MetamorphicWrapKind.SingleIterationLoop, "for (int metaOnce0 = 0; metaOnce0 < 1; metaOnce0++)")]
    public void WrapStatement_EachWrapKind_WrapsTheStatementWithoutDoubleUnderscoreNames(MetamorphicWrapKind wrapKind, string expected)
    {
        string source = ReadTestCase("WrapStatement.cs");

        string wrapped = FuzzMetamorphicRewriter.WrapStatementInControlFlow(source, seed: 0x1138_06UL, targetMethodName: "KernelWrap", wrapKind);

        // `__` names are reserved for the compiler (GSH0055); the loop variable is `metaOnce<n>`.
        // mutant: MetamorphicControlFlowWrapper writes `< 2` as the loop bound
        // input: `acc = (acc + inB[gid]) ^ inC[gid];`, the only statement that may be wrapped
        Assert.Contains(expected, wrapped.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("__", wrapped, StringComparison.Ordinal);
        AssertCompilesAndMatchesRuntime(source, wrapped, "MetaWrapKernels", "KernelWrap");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void AddDeadCode_KernelBody_GetsAnUnusedLocalAndAnUnreachableBranch()
    {
        string source = ReadTestCase("AddDeadCode.cs");

        string withDeadCode = FuzzMetamorphicRewriter.AddDeadCode(source, seed: 0x1138_07UL, targetMethodName: "KernelDeadCode");

        // The branch writes `hash` only where it cannot run.
        // mutant: MetamorphicDeadCodeInserter writes `if (true)`
        // input: a kernel that folds one local into `hash`
        Assert.Contains("int metaDeadLocal", withDeadCode, StringComparison.Ordinal);
        Assert.Contains("if (false)", withDeadCode, StringComparison.Ordinal);
        Assert.DoesNotContain("__", withDeadCode, StringComparison.Ordinal);
        AssertCompilesAndMatchesRuntime(source, withDeadCode, "MetaDeadCodeKernels", "KernelDeadCode");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void AddDeadCode_KernelWithBarrier_AddsNoBarrierAndNoDiagnostic()
    {
        string source = ReadTestCase("DeadCodeWithBarrier.cs");
        VerifyZeroGeneratorAndAnalyzerDiagnostics(source, "Cpu");

        string withDeadCode = FuzzMetamorphicRewriter.AddDeadCode(source, seed: 1UL, targetMethodName: "KernelWithBarrier");

        // mutant: MetamorphicDeadCodeInserter emits `Gpu.SyncThreads()` inside `if (false)`
        // input: a kernel that calls `Gpu.SyncThreads()` before folding into `hash`
        Assert.Contains("if (false)", withDeadCode, StringComparison.Ordinal);
        Assert.Equal(
            withDeadCode.IndexOf("Gpu.SyncThreads()", StringComparison.Ordinal),
            withDeadCode.LastIndexOf("Gpu.SyncThreads()", StringComparison.Ordinal));
        VerifyZeroGeneratorAndAnalyzerDiagnostics(withDeadCode, "Cpu");
        VerifyZeroGeneratorAndAnalyzerDiagnostics(withDeadCode, "Cuda");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void GenerateBatch_WithMetamorphicEnabled_EmitsTransformedVariantsAndMatchesBaseKernelOutputs()
    {
        var options = new FuzzOptions
        {
            FuzzSeed = 0x1138_BEEFUL,
            BatchSize = 3,
            Profile = "C",
            Metamorphic = true,
            MetamorphicVariantsPerKernel = 2,
        };

        var batch = new ScalarFuzzGenerator(options).GenerateBatch();

        // BatchSize=3 with MetamorphicVariantsPerKernel=2 emits 3 base kernels + 6 metamorphic variants = 9 kernels total.
        // mutant: ScalarFuzzGenerator.GenerateBatch does not call AddMetamorphicVariants
        // input: FuzzOptions(BatchSize = 3, Metamorphic = true, MetamorphicVariantsPerKernel = 2)
        Assert.Equal(9, batch.Kernels.Count);
        Assert.Contains("Kernel_00_Meta0", batch.KernelsSource, StringComparison.Ordinal);
        Assert.Contains("Kernel_00_Meta1", batch.KernelsSource, StringComparison.Ordinal);
        Assert.Contains("Kernel_00_Meta0_Case", batch.CasesSource, StringComparison.Ordinal);

        VerifyZeroGeneratorAndAnalyzerDiagnostics(batch.KernelsSource, "Cpu");
        VerifyZeroGeneratorAndAnalyzerDiagnostics(batch.KernelsSource, "Cuda");

        var asm = FuzzTestCompilationHelper.CompileToInMemoryAssembly("MetaBatchExecAssembly", batch.KernelsSource);
        var kernelClass = asm.GetType("Kassyi.GSharp.Tests.Shared.Differential.FuzzBatchKernels");
        Assert.NotNull(kernelClass);

        for (int i = 0; i < 3; i++)
        {
            string baseName = $"Kernel_{i:D2}";
            var baseDel = FuzzTestCompilationHelper.CreateKernelDelegate(kernelClass, baseName);
            for (int m = 0; m < 2; m++)
            {
                string metaName = $"{baseName}_Meta{m}";
                var metaDel = FuzzTestCompilationHelper.CreateKernelDelegate(kernelClass, metaName);
                Assert.False(
                    FuzzTestCompilationHelper.EvaluatesToMismatch(metaName, baseDel, metaDel),
                    $"Expected metamorphic variant {metaName} to evaluate identically to {baseName}.");
            }
        }
    }
}
