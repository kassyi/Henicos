#nullable enable
using static Kassyi.GSharp.Tools.FuzzDiff.Tests.MetamorphicTestSupport;

namespace Kassyi.GSharp.Tools.FuzzDiff.Tests;

/// <summary>
/// One test per form that a metamorphic rewrite of <see cref="FuzzMetamorphicRewriter"/> must leave unchanged. Each
/// input kernel contains that form and nothing else the rewrite could apply to, so a test fails exactly when the
/// rejection of its form breaks.
/// </summary>
public class FuzzMetamorphicRejectionTests
{
    /// <summary>The two ways <see cref="FuzzMetamorphicRewriter"/> extracts a subexpression.</summary>
    public enum ExtractionTarget
    {
        /// <summary><see cref="FuzzMetamorphicRewriter.ExtractPureSubexpressionToLocal"/>.</summary>
        Local,

        /// <summary><see cref="FuzzMetamorphicRewriter.ExtractPureSubexpressionToDeviceFunction"/>.</summary>
        DeviceFunction,
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void RenameLocals_OnlyReservedNames_IsUnchanged()
    {
        // `gid`, `hash` and `checksum` are what the harness and the other rewrites look for.
        // mutant: MetamorphicIdentifierRenamer.IsProtectedSpecialIdentifier returns false
        // input: a kernel with only gid, hash and checksum
        AssertUnchanged("RenameReservedOnly.cs", static s => FuzzMetamorphicRewriter.RenameLocalsAndParameters(s, 1UL, "KernelReservedOnly"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void RenameLocals_MethodWithSemanticError_IsUnchanged()
    {
        // mutant: MetamorphicIdentifierRenamer.Apply drops the HasSemanticErrorsInMethod check
        // input: a local of the unknown type UnknownMysteryType
        AssertUnchanged("RenameSemanticError.cs", static s => FuzzMetamorphicRewriter.RenameLocalsAndParameters(s, 1UL, "KernelBroken"));
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("ConstantByteProduct.cs", "KernelConstantByte")]
    [InlineData("ConstantShortShiftOr.cs", "KernelConstantShort")]
    [InlineData("ConstantSByteNegation.cs", "KernelConstantSByte")]
    [InlineData("ConstantUShortMaskSum.cs", "KernelConstantUShort")]
    public void ExtractSubexpression_ConstantOperandOfImplicitNarrowing_IsNotExtracted(string fileName, string kernelName)
    {
        string source = ReadTestCase(fileName);

        // `byte b = (1 + 2) * 3;` compiles only because the whole initializer is a constant that fits in `byte`;
        // moving `1 + 2` into an `int` local makes the initializer non-constant (CS0266).
        // mutant: MetamorphicSubexpressionExtractor: drop the `GetConstantValue(...).HasValue` check
        // input: one constant subexpression under an implicit constant narrowing to byte, short, sbyte or ushort
        for (ulong seed = 0; seed < 6; seed++)
        {
            string rewritten = FuzzMetamorphicRewriter.ExtractPureSubexpressionToLocal(source, seed, kernelName);
            AssertNoCompileErrors(rewritten);
            Assert.Equal(source, rewritten);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void ExtractSubexpression_OperandOfConditionalBranchOrShortCircuit_IsNotHoisted()
    {
        // Evaluating `x + 99` before `?:` or `(x + 5) > 0` before `&&` would run what the condition skips.
        // mutant: MetamorphicPurity.IsOnUnconditionalEvaluationPath returns true
        // input: `flag ? (x + 99) : (y * 3)` and `flag && ((x + 5) > 0)`
        AssertUnchanged("ExtractGuardedArms.cs", static s => FuzzMetamorphicRewriter.ExtractPureSubexpressionToLocal(s, 1UL, "KernelGuardedArms"));
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(ExtractionTarget.Local)]
    [InlineData(ExtractionTarget.DeviceFunction)]
    public void ExtractSubexpression_StatementWithIncrement_IsNotExtracted(ExtractionTarget target)
    {
        // Moving `y ^ 7` before `++x` is harmless here, but the statement is not side-effect free, so nothing in it moves.
        // mutant: SideEffectFreeChecker.VisitPrefixUnaryExpression accepts PreIncrementExpression
        // input: `int mutatedRhs = (++x) + (y ^ 7);`
        AssertUnchanged("ExtractIncrement.cs", s => Extract(target, s, "KernelIncrement"));
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(ExtractionTarget.Local)]
    [InlineData(ExtractionTarget.DeviceFunction)]
    public void ExtractSubexpression_DivisionOrRemainder_IsNotExtracted(ExtractionTarget target)
    {
        // A division may throw; it must not run where it did not run before.
        // mutant: PureExpressionChecker.VisitBinaryExpression accepts DivideExpression and ModuloExpression
        // input: `int divExpr = (x / 3) ^ (y % 5);`
        AssertUnchanged("ExtractDivision.cs", s => Extract(target, s, "KernelDivision"));
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(ExtractionTarget.Local)]
    [InlineData(ExtractionTarget.DeviceFunction)]
    public void ExtractSubexpression_PropertyRead_IsNotExtracted(ExtractionTarget target)
    {
        // mutant: SideEffectFreeChecker.VisitMemberAccessExpression accepts IPropertySymbol
        // input: `int propExpr = (s.Computed + 11) ^ 3;` with a computed property
        AssertUnchanged("ExtractPropertyRead.cs", s => Extract(target, s, "KernelPropertyRead"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void ExtractSubexpression_ToDeviceFunction_ArrayElementRead_IsNotExtracted()
    {
        // A device function cannot read the kernel's arrays without taking them as parameters.
        // mutant: MetamorphicSubexpressionExtractor.ExtractToDeviceFunction drops the ContainsElementAccess check
        // input: `int arrExpr = (inA[gid] + inB[gid]) ^ 9;`
        AssertUnchanged("ExtractArrayRead.cs", static s => Extract(ExtractionTarget.DeviceFunction, s, "KernelArrayRead"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void ExtractSubexpression_ToDeviceFunction_StructFieldRead_IsNotExtracted()
    {
        // mutant: TryCollectValueTypeFreeVariables accepts an instance field access
        // input: `int fieldExpr = (holder.Val + 9) ^ 5;`
        AssertUnchanged("ExtractStructField.cs", static s => Extract(ExtractionTarget.DeviceFunction, s, "KernelStructField"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void InlineDeviceFunction_RefParameter_IsNotInlined()
    {
        // mutant: MetamorphicDeviceFunctionInliner drops the HasOnlySupportedByValueParameters check
        // input: `HelperByRef(ref v0, inB[gid])`
        AssertUnchanged("InlineRefParameter.cs", static s => FuzzMetamorphicRewriter.InlineDeviceFunctionCall(s, 1UL, "KernelRefParameter"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void InlineDeviceFunction_HelperWithBarrier_IsNotInlined()
    {
        // mutant: MetamorphicDeviceFunctionInliner drops the ContainsBarrierOrWarpCall check
        // input: `HelperWithBarrier(inA[gid])`, whose body calls Gpu.SyncThreads()
        AssertUnchanged("InlineBarrierHelper.cs", static s => FuzzMetamorphicRewriter.InlineDeviceFunctionCall(s, 1UL, "KernelBarrierHelper"));
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("InlineIncrementArgument.cs", "KernelIncrementArgument")]
    [InlineData("InlineAssignmentArgument.cs", "KernelAssignmentArgument")]
    [InlineData("InlineDivisionArgument.cs", "KernelDivisionArgument")]
    public void InlineDeviceFunction_ArgumentWithEffectOrDivision_IsNotInlined(string fileName, string kernelName)
    {
        // `a` appears twice in `(a ^ b) + a`, so an inlined `++v0` or `v0 += 3` would run twice; `v0 / 3` may throw.
        // mutant: MetamorphicDeviceFunctionInliner drops the AreAllArgumentsPure check
        // input: `HelperPure(++v0, ...)`, `HelperPure(v0 += 3, ...)` and `HelperPure(v0 / 3, ...)`
        AssertUnchanged(fileName, s => FuzzMetamorphicRewriter.InlineDeviceFunctionCall(s, 1UL, kernelName));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void ReorderStatements_DependentStatements_KeepTheirOrder()
    {
        // mutant: ExpressionEffectCollector.VisitIdentifierName does not record reads
        // input: `int a = ...; int dependent = (a * 5) + 3; a = dependent + inC[gid];`
        AssertUnchanged("ReorderDependentPair.cs", static s => FuzzMetamorphicRewriter.ReorderIndependentStatements(s, 1UL, "KernelDependentPair"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void ReorderStatements_ArrayWriteAndReadOfAnotherArray_AreNotSwapped()
    {
        // `arrA` and `arrB` may be the same array at the call site.
        // mutant: ExpressionEffectCollector.TryRecordAssignmentTarget accepts an element access as a write of the array
        //         and VisitElementAccessExpression accepts any element access
        // input: `arrA[gid] = 42; int fromB = arrB[gid] + 1;`
        AssertUnchanged("ReorderArrayWrite.cs", static s => FuzzMetamorphicRewriter.ReorderIndependentStatements(s, 1UL, "KernelArrayWrite"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void ReorderStatements_ReadOfArrayTheKernelWrites_IsNotMoved()
    {
        // Only `input[gid]` reads of arrays the kernel never writes are described by the read set; any other element
        // access is a wall, even next to a statement that does not touch memory.
        // mutant: ExpressionEffectCollector.VisitElementAccessExpression accepts any element access
        // input: `int fromA = arrA[gid] + 1;` (arrA is written) next to `int other = inC[gid] ^ 7;`
        AssertUnchanged("ReorderReadOfWrittenArray.cs", static s => FuzzMetamorphicRewriter.ReorderIndependentStatements(s, 1UL, "KernelReadOfWrittenArray"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void ReorderStatements_Barrier_IsNotCrossed()
    {
        // mutant: MetamorphicStatementEffects.AreIndependent drops the ContainsBarrierOrWarpCall checks
        // input: `int fromA = ...; Gpu.SyncThreads(); int fromB = ...;`
        AssertUnchanged("ReorderBarrier.cs", static s => FuzzMetamorphicRewriter.ReorderIndependentStatements(s, 1UL, "KernelBarrier"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void SwapOperands_FloatingPoint_IsNotSwapped()
    {
        // With two NaN operands, `a + b` and `b + a` may return different NaN payloads.
        // mutant: MetamorphicCommutativeSwapper drops the IsFloatingPointType check
        // input: float `fA + fB`, `fA * fB` and double `(double)fA + (double)fB`
        AssertUnchanged("SwapFloatingPoint.cs", static s => FuzzMetamorphicRewriter.SwapCommutativeOperands(s, 1UL, "KernelFloatingPoint"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void SwapOperands_NarrowOperandsPromotedToInt_AreNotSwapped()
    {
        // mutant: MetamorphicCommutativeSwapper drops the `Is32Or64BitIntegerType` check for `+` and `*`
        // input: `int narrowSum = sA + sB;` with short operands
        AssertUnchanged("SwapNarrowPromotion.cs", static s => FuzzMetamorphicRewriter.SwapCommutativeOperands(s, 1UL, "KernelNarrowPromotion"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void SwapOperands_Subtraction_IsNotSwapped()
    {
        // mutant: MetamorphicCommutativeSwapper.IsCommutativeBinaryKind includes SubtractExpression
        // input: `int diff = x - y;`
        AssertUnchanged("SwapSubtraction.cs", static s => FuzzMetamorphicRewriter.SwapCommutativeOperands(s, 1UL, "KernelSubtraction"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void SwapOperands_OperandWithAssignment_IsNotSwapped()
    {
        // C# evaluates `x += 3` before `x * 5`; swapping would read `x` before the assignment.
        // mutant: MetamorphicPurity.TryGetTopLevelPureRhsExpression drops the IsSideEffectFree check
        // input: `int sideEffect = (x += 3) + (x * 5);`
        AssertUnchanged("SwapAssignmentOperand.cs", static s => FuzzMetamorphicRewriter.SwapCommutativeOperands(s, 1UL, "KernelAssignmentOperand"));
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("WrapBarrierCall.cs", "KernelBarrierCall")]
    [InlineData("WrapBlockWithBarrier.cs", "KernelBlockWithBarrier")]
    [InlineData("WrapHelperWithBarrier.cs", "KernelHelperWithBarrier")]
    public void WrapStatement_BarrierDirectNestedOrThroughHelper_IsNotWrapped(string fileName, string kernelName)
    {
        // A barrier under `if (true)` or a loop is a barrier in divergent control flow for the compiler.
        // mutant: MetamorphicControlFlowWrapper.IsWrappableExecutableStatement drops the ContainsBarrierOrWarpCall check
        // input: `Gpu.SyncThreads();`, `{ Gpu.SyncThreads(); }` and `HelperSync();` whose body calls it
        foreach (var wrapKind in Enum.GetValues<MetamorphicWrapKind>())
        {
            AssertUnchanged(fileName, s => FuzzMetamorphicRewriter.WrapStatementInControlFlow(s, 1UL, kernelName, wrapKind));
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void AddDeadCode_KernelWithOnlyThePrologue_IsUnchanged()
    {
        // mutant: MetamorphicDeadCodeInserter drops the `minInsertIdx >= statements.Count` guard
        // input: a kernel whose body ends after `ulong hash = ...;`
        AssertUnchanged("DeadCodePrologueOnly.cs", static s => FuzzMetamorphicRewriter.AddDeadCode(s, 1UL, "KernelPrologueOnly"));
    }

    private static string Extract(ExtractionTarget target, string source, string kernelName) =>
        target == ExtractionTarget.Local
            ? FuzzMetamorphicRewriter.ExtractPureSubexpressionToLocal(source, 1UL, kernelName)
            : FuzzMetamorphicRewriter.ExtractPureSubexpressionToDeviceFunction(source, 1UL, kernelName);

    private static void AssertUnchanged(string fileName, Func<string, string> rewrite)
    {
        string source = ReadTestCase(fileName);
        Assert.Equal(source, rewrite(source));
    }
}
