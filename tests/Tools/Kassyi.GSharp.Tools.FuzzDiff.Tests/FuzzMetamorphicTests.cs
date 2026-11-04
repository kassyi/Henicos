#nullable enable
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Tests.Shared.Differential;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tools.FuzzDiff.Tests;

/// <summary>
/// Unit and property tests for <see cref="FuzzMetamorphicRewriter"/> and <c>--metamorphic</c> batch generation (#1138).
/// </summary>
public class FuzzMetamorphicTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Transform1_RenameLocalsAndParameters_RenamesToCppKeywordsAndPreludeNamesPreservingSemantics()
    {
        const string BeforeSource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public struct MetaPair
            {
            public int First;
            public int Second;
            }

            public static partial class MetaRenameKernels
            {
            [GpuKernel]
            public static void KernelRename(int[] inA, int[] inB, int[] inC, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            int alpha = inA[gid] + inB[gid];
            int beta = alpha ^ inC[gid];
            MetaPair pair = new MetaPair { First = alpha, Second = beta };
            int combined = pair.First + pair.Second;
            hash = (hash ^ (ulong)combined) * 1099511628211UL;
            checksum[gid] = hash;
            }
            }
            """;

        string afterSource = FuzzMetamorphicRewriter.RenameLocalsAndParameters(BeforeSource, seed: 0x1138_01UL, targetMethodName: "KernelRename");

        // Local variables (`alpha`, `beta`, `pair`, `combined`) and input parameters (`inA`, `inB`, `inC`) are renamed to C++ keywords / Prelude names without `__`, while struct field names (`First`, `Second`) on member accesses remain untouched.
        // mutant: FuzzMetamorphicRewriter.RenameLocalsAndParameters(): remove `!FuzzSyntaxHelpers.IsMemberAccessRight(id)` guard when replacing identifier references
        // input: BeforeSource with locals `alpha`, `beta`, `pair`, `combined`, parameters `inA`, `inB`, `inC`, and struct field accesses `pair.First`, `pair.Second`
        Assert.NotEqual(BeforeSource, afterSource);
        Assert.DoesNotContain("int alpha =", afterSource, StringComparison.Ordinal);
        Assert.DoesNotContain("int beta =", afterSource, StringComparison.Ordinal);
        Assert.Contains(".First", afterSource, StringComparison.Ordinal);
        Assert.Contains(".Second", afterSource, StringComparison.Ordinal);
        Assert.Contains(
            FuzzMetamorphicRewriter.CandidateIdentifiers,
            candidate => afterSource.Contains($"int {candidate} =", StringComparison.Ordinal));

        AssertCompilesAndMatchesRuntime(BeforeSource, afterSource, "MetaRenameKernels", "KernelRename");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Transform2_ExtractPureSubexpression_ExtractsToLocalAndDeviceFunctionAndNeverHoistsGuardedBranch()
    {
        const string BeforeSource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public static partial class MetaExtractKernels
            {
            [GpuKernel]
            public static void KernelExtract(int[] inA, int[] inB, int[] inC, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            int x = inA[gid];
            int y = inB[gid];
            int z = ((x ^ 0x13579BDF) + (y * 3)) ^ inC[gid];
            int safeDiv = (y != 0 && y != int.MinValue) ? (x / y) : 0;
            hash = (hash ^ (ulong)(z ^ safeDiv)) * 1099511628211UL;
            checksum[gid] = hash;
            }
            }
            """;

        string localExtracted = FuzzMetamorphicRewriter.ExtractPureSubexpressionToLocal(
            BeforeSource,
            seed: 0x1138_02UL,
            targetMethodName: "KernelExtract");
        string fnExtracted = FuzzMetamorphicRewriter.ExtractPureSubexpressionToDeviceFunction(
            BeforeSource,
            seed: 0x1138_02UL,
            targetMethodName: "KernelExtract",
            helperMethodName: "MetaExtractedHelper_0");

        // Unconditionally-evaluated pure subexpressions are extracted into a preceding local (`metaExtracted0`) or a `[GpuDeviceFunction]` helper (`MetaExtractedHelper_0`), whereas expressions inside conditional arms (`?:`, `&&`, `||`, `??`) are never hoisted out of their guard.
        // mutant: FuzzMetamorphicRewriter.IsOnUnconditionalEvaluationPath(): return true unconditionally so expressions inside `?:` or `&&` are hoisted into preceding locals
        // input: BeforeSource and GuardedArmsOnlySource where arithmetic expressions only appear inside `?:` and `&&` right-hand arms
        Assert.Contains("metaExtracted0 =", localExtracted, StringComparison.Ordinal);
        Assert.Contains("? (x / y) : 0", localExtracted, StringComparison.Ordinal);
        Assert.Contains("[GpuDeviceFunction]", fnExtracted, StringComparison.Ordinal);
        Assert.Contains("MetaExtractedHelper_0(", fnExtracted, StringComparison.Ordinal);

        const string GuardedArmsOnlySource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public static partial class MetaExtractKernels
            {
            [GpuKernel]
            public static void KernelGuardedOnly(int[] inA, int[] inB, int[] inC, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            int x = inA[gid];
            int y = inB[gid];
            int guarded = true ? (x + 99) : (y * 3);
            hash = (hash ^ (ulong)guarded) * 1099511628211UL;
            checksum[gid] = hash;
            }
            }
            """;
        Assert.Equal(
            GuardedArmsOnlySource,
            FuzzMetamorphicRewriter.ExtractPureSubexpressionToLocal(GuardedArmsOnlySource, seed: 1UL, targetMethodName: "KernelGuardedOnly"));

        AssertCompilesAndMatchesRuntime(BeforeSource, localExtracted, "MetaExtractKernels", "KernelExtract");
        AssertCompilesAndMatchesRuntime(BeforeSource, fnExtracted, "MetaExtractKernels", "KernelExtract");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Transform3_InlineDeviceFunctionCall_InlinesPureCallAndSkipsImpureMultiUseArguments()
    {
        const string BeforeSource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public static partial class MetaInlineKernels
            {
            [GpuDeviceFunction]
            public static int MixHelper(int a, int b)
            {
            int step = (a ^ 0x5555AAAA) + b;
            return (step * 3) ^ a;
            }

            [GpuKernel]
            public static void KernelInline(int[] inA, int[] inB, int[] inC, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            int v0 = inA[gid];
            int v1 = inB[gid] + inC[gid];
            int pureCall = MixHelper(v0 + 7, v1);
            int impureCall = MixHelper(v0 += 3, v1);
            hash = (hash ^ (ulong)(pureCall ^ impureCall ^ v0)) * 1099511628211UL;
            checksum[gid] = hash;
            }
            }
            """;

        string afterSource = FuzzMetamorphicRewriter.InlineDeviceFunctionCall(
            BeforeSource,
            seed: 0x1138_03UL,
            targetMethodName: "KernelInline");

        // `MixHelper(v0 + 7, v1)` has pure arguments and is inlined in-place, whereas `MixHelper(v0 += 3, v1)` has a mutating argument for parameter `a` (which is referenced twice in `MixHelper`) and must remain a call so `v0 += 3` executes once.
        // mutant: FuzzMetamorphicRewriter.InlineDeviceFunctionCall(): remove `AreAllArgumentsPure(inv, safeInputArrays)` check so `MixHelper(v0 += 3, v1)` is also inlined and increments `v0` twice
        // input: BeforeSource calling `MixHelper(v0 + 7, v1)` and `MixHelper(v0 += 3, v1)` where `a` appears twice in `MixHelper`
        Assert.DoesNotContain("MixHelper(v0 + 7, v1)", afterSource, StringComparison.Ordinal);
        Assert.Contains("MixHelper(v0 += 3, v1)", afterSource, StringComparison.Ordinal);

        string secondPass = FuzzMetamorphicRewriter.InlineDeviceFunctionCall(
            afterSource,
            seed: 0x1138_03UL,
            targetMethodName: "KernelInline");
        Assert.Contains("MixHelper(v0 += 3, v1)", secondPass, StringComparison.Ordinal);

        AssertCompilesAndMatchesRuntime(BeforeSource, afterSource, "MetaInlineKernels", "KernelInline");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Transform4_ReorderIndependentStatements_SwapsDisjointPairAndPreservesDependentOrder()
    {
        const string BeforeSource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public static partial class MetaReorderKernels
            {
            [GpuKernel]
            public static void KernelReorder(int[] inA, int[] inB, int[] inC, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            int a = inA[gid] + 11;
            int b = inB[gid] ^ 29;
            int dependent = (a * 5) + b;
            a = dependent + inC[gid];
            hash = (hash ^ (ulong)(a ^ b ^ dependent)) * 1099511628211UL;
            checksum[gid] = hash;
            }
            }
            """;

        string afterSource = FuzzMetamorphicRewriter.ReorderIndependentStatements(
            BeforeSource,
            seed: 0x1138_04UL,
            targetMethodName: "KernelReorder");

        int idxB = afterSource.IndexOf("int b = inB[gid] ^ 29;", StringComparison.Ordinal);
        int idxA = afterSource.IndexOf("int a = inA[gid] + 11;", StringComparison.Ordinal);
        int idxDep = afterSource.IndexOf("int dependent = (a * 5) + b;", StringComparison.Ordinal);
        int idxMutA = afterSource.IndexOf("a = dependent + inC[gid];", StringComparison.Ordinal);

        // Disjoint statements `int a = inA[gid] + 11;` and `int b = inB[gid] ^ 29;` are reordered (`b` before `a`), whereas RAW/WAR-dependent statements `int dependent = (a * 5) + b;` and `a = dependent + inC[gid];` remain in order after `int a`.
        // mutant: FuzzMetamorphicRewriter.AreStatementsIndependent(): return true unconditionally ignoring `w1.Overlaps(r2)` and `w2.Overlaps(r1)`
        // input: BeforeSource with independent (`a`, `b`) and dependent (`dependent = (a * 5) + b;`, `a = dependent + inC[gid];`) adjacent statements
        Assert.True(idxB >= 0 && idxA > idxB, $"Expected `int b` to appear before `int a` after reordering:{Environment.NewLine}{afterSource}");
        Assert.True(idxDep > idxA && idxMutA > idxDep, $"Expected dependent statements to stay ordered:{Environment.NewLine}{afterSource}");

        AssertCompilesAndMatchesRuntime(BeforeSource, afterSource, "MetaReorderKernels", "KernelReorder");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Transform5_SwapCommutativeOperands_SwapsPureCommutativeAndSkipsNonCommutativeOrImpure()
    {
        const string BeforeSource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public static partial class MetaCommutativeKernels
            {
            [GpuKernel]
            public static void KernelCommutative(int[] inA, int[] inB, int[] inC, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            int x = inA[gid];
            int y = inB[gid];
            int sum = (x ^ 19) + (y * 7);
            int diff = x - y;
            int sideEffect = ((x += 3) + (x * 5)) ^ inC[gid];
            hash = (hash ^ (ulong)(sum ^ diff ^ sideEffect)) * 1099511628211UL;
            checksum[gid] = hash;
            }
            }
            """;

        string afterSource = FuzzMetamorphicRewriter.SwapCommutativeOperands(
            BeforeSource,
            seed: 0x1138_05UL,
            targetMethodName: "KernelCommutative");

        // Pure commutative `(x ^ 19) + (y * 7)` is swapped, whereas non-commutative `x - y` and impure `((x += 3) + (x * 5))` (where C# left-to-right evaluation mutates `x` before `x * 5`) are never swapped.
        // mutant: FuzzMetamorphicRewriter.SwapCommutativeOperands(): allow `SubtractExpression` or skip `IsPureExpression` check on operands
        // input: BeforeSource containing `(x ^ 19) + (y * 7)`, `x - y`, and `((x += 3) + (x * 5))`
        Assert.NotEqual(BeforeSource, afterSource);
        Assert.Contains("x - y", afterSource, StringComparison.Ordinal);
        Assert.Contains("(x += 3) + (x * 5)", afterSource, StringComparison.Ordinal);

        AssertCompilesAndMatchesRuntime(BeforeSource, afterSource, "MetaCommutativeKernels", "KernelCommutative");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Transform6_WrapStatementInControlFlow_WrapsInBlockIfTrueAndSingleIterationLoopWithoutDoubleUnderscore()
    {
        const string BeforeSource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public static partial class MetaWrapKernels
            {
            [GpuKernel]
            public static void KernelWrap(int[] inA, int[] inB, int[] inC, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            int acc = inA[gid];
            acc = (acc + inB[gid]) ^ inC[gid];
            hash = (hash ^ (ulong)acc) * 1099511628211UL;
            checksum[gid] = hash;
            }
            }
            """;

        string wrappedBlock = FuzzMetamorphicRewriter.WrapStatementInControlFlow(
            BeforeSource,
            seed: 0x1138_06UL,
            targetMethodName: "KernelWrap",
            wrapKind: MetamorphicWrapKind.Block);
        string wrappedIfTrue = FuzzMetamorphicRewriter.WrapStatementInControlFlow(
            BeforeSource,
            seed: 0x1138_06UL,
            targetMethodName: "KernelWrap",
            wrapKind: MetamorphicWrapKind.IfTrue);
        string wrappedLoop = FuzzMetamorphicRewriter.WrapStatementInControlFlow(
            BeforeSource,
            seed: 0x1138_06UL,
            targetMethodName: "KernelWrap",
            wrapKind: MetamorphicWrapKind.SingleIterationLoop);

        // Wrapping produces `{ S }`, `if (true) { S }`, and `for (int metaOnce0 = 0; metaOnce0 < 1; metaOnce0++) { S }` without `__` identifiers (which would trigger GSH0055), and never wraps local variable declarations needed by subsequent statements.
        // mutant: FuzzMetamorphicRewriter.WrapStatementInControlFlow(): change loop bound `< 1` to `< 2` in `SingleIterationLoop`
        // input: BeforeSource mutating `acc = (acc + inB[gid]) ^ inC[gid];` and `hash = (hash ^ (ulong)acc) * 1099511628211UL;`
        Assert.NotEqual(BeforeSource, wrappedBlock);
        Assert.Contains("if (true)", wrappedIfTrue, StringComparison.Ordinal);
        Assert.Contains("for (int metaOnce", wrappedLoop, StringComparison.Ordinal);
        Assert.DoesNotContain("__", wrappedLoop, StringComparison.Ordinal);

        AssertCompilesAndMatchesRuntime(BeforeSource, wrappedBlock, "MetaWrapKernels", "KernelWrap");
        AssertCompilesAndMatchesRuntime(BeforeSource, wrappedIfTrue, "MetaWrapKernels", "KernelWrap");
        AssertCompilesAndMatchesRuntime(BeforeSource, wrappedLoop, "MetaWrapKernels", "KernelWrap");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Transform7_AddDeadCode_InsertsUnusedLocalAndUnreachableBranchPreservingSemantics()
    {
        const string BeforeSource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public static partial class MetaDeadCodeKernels
            {
            [GpuKernel]
            public static void KernelDeadCode(int[] inA, int[] inB, int[] inC, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            int v0 = (inA[gid] + inB[gid]) ^ inC[gid];
            hash = (hash ^ (ulong)v0) * 1099511628211UL;
            checksum[gid] = hash;
            }
            }
            """;

        string afterSource = FuzzMetamorphicRewriter.AddDeadCode(
            BeforeSource,
            seed: 0x1138_07UL,
            targetMethodName: "KernelDeadCode");

        // Dead code insertion adds both a pure unused local (`metaDeadLocal`) and an unreachable branch (`if (false)`) that mutates `hash` only inside the unreachable block.
        // mutant: FuzzMetamorphicRewriter.AddDeadCode(): change `if (false)` to `if (true)`
        // input: BeforeSource computing `v0 = (inA[gid] + inB[gid]) ^ inC[gid]` and folding into `hash`
        Assert.Contains("int metaDeadLocal", afterSource, StringComparison.Ordinal);
        Assert.Contains("if (false)", afterSource, StringComparison.Ordinal);
        Assert.DoesNotContain("__", afterSource, StringComparison.Ordinal);

        AssertCompilesAndMatchesRuntime(BeforeSource, afterSource, "MetaDeadCodeKernels", "KernelDeadCode");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Transform1_Negative_RenameLocalsAndParameters_SkipsReservedIdentifiersAndUnresolvedSymbols()
    {
        const string ReservedOnlySource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public static partial class MetaNegativeRenameKernels
            {
            [GpuKernel]
            public static void KernelReservedOnly(ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            checksum[gid] = hash;
            }
            }
            """;

        const string UnresolvedErrorSource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public static partial class MetaNegativeRenameKernels
            {
            [GpuKernel]
            public static void KernelBroken(int[] inA, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            UnknownMysteryType broken = inA[gid];
            checksum[gid] = (ulong)broken;
            }
            }
            """;

        // Reserved identifiers (`gid`, `hash`, `checksum`) and methods containing semantic errors (`UnknownMysteryType`) must not be rewritten.
        // mutant: MetamorphicIdentifierRenamer.IsProtectedSpecialIdentifier(): return false so `gid`, `hash`, and `checksum` are renamed
        // input: ReservedOnlySource (only `gid`, `hash`, `checksum`) and UnresolvedErrorSource (`UnknownMysteryType`)
        Assert.Equal(
            ReservedOnlySource,
            FuzzMetamorphicRewriter.RenameLocalsAndParameters(ReservedOnlySource, seed: 1UL, targetMethodName: "KernelReservedOnly"));
        Assert.Equal(
            UnresolvedErrorSource,
            FuzzMetamorphicRewriter.RenameLocalsAndParameters(UnresolvedErrorSource, seed: 1UL, targetMethodName: "KernelBroken"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Transform2_Negative_ExtractPureSubexpression_RejectsSideEffectsDivisionsPropertiesAndArrayAccesses()
    {
        const string ImpureOrUnsafeSource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public struct MetaPropStruct
            {
            public int Raw;
            public int Computed => Raw + 1;
            }

            public static partial class MetaNegativeExtractKernels
            {
            [GpuKernel]
            public static void KernelNoExtract(int[] inA, int[] inB, int[] inC, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            int x = inA[gid];
            int y = inB[gid];
            int mutatedRhs = (++x) + (y ^ 7);
            int divExpr = (x / 3) ^ (y % 5);
            MetaPropStruct s = new MetaPropStruct { Raw = x };
            int propExpr = s.Computed + 11;
            hash = (hash ^ (ulong)(mutatedRhs ^ divExpr ^ propExpr)) * 1099511628211UL;
            checksum[gid] = hash;
            }
            }
            """;

        const string ArrayAndStructFieldSource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public struct MetaFieldHolder
            {
            public int Val;
            }

            public static partial class MetaNegativeExtractKernels
            {
            [GpuKernel]
            public static void KernelArrayOnly(int[] inA, int[] inB, int[] inC, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            MetaFieldHolder holder = new MetaFieldHolder { Val = 3 };
            int arrExpr = (inA[gid] + inB[gid]) ^ (holder.Val + 9);
            hash = (hash ^ (ulong)arrExpr) * 1099511628211UL;
            checksum[gid] = hash;
            }
            }
            """;

        // Statements with side effects (`++x`), division/modulo (`x / 3`, `y % 5`), or property getters (`s.Computed`) must not have subexpressions extracted; device-function extraction must also reject array element reads and struct instance field accesses.
        // mutant: MetamorphicEffectAnalyzer.IsSideEffectFreeExpression(): allow `PreIncrementExpression` and `IPropertySymbol`
        // input: ImpureOrUnsafeSource (`(++x) + (y ^ 7)`, `s.Computed + 11`) and ArrayAndStructFieldSource (`inA[gid] + inB[gid]`, `holder.Val + 9`)
        Assert.Equal(
            ImpureOrUnsafeSource,
            FuzzMetamorphicRewriter.ExtractPureSubexpressionToLocal(ImpureOrUnsafeSource, seed: 1UL, targetMethodName: "KernelNoExtract"));
        Assert.Equal(
            ImpureOrUnsafeSource,
            FuzzMetamorphicRewriter.ExtractPureSubexpressionToDeviceFunction(ImpureOrUnsafeSource, seed: 1UL, targetMethodName: "KernelNoExtract"));
        Assert.Equal(
            ArrayAndStructFieldSource,
            FuzzMetamorphicRewriter.ExtractPureSubexpressionToDeviceFunction(ArrayAndStructFieldSource, seed: 1UL, targetMethodName: "KernelArrayOnly"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Transform3_Negative_InlineDeviceFunctionCall_RejectsByRefHelperBarrierHelperAndDivisionCall()
    {
        const string NonInlinableCallsSource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public static partial class MetaNegativeInlineKernels
            {
            [GpuDeviceFunction]
            public static int HelperByRef(ref int a, int b)
            {
            a += b;
            return a;
            }

            [GpuDeviceFunction]
            public static int HelperWithBarrier(int a)
            {
            Gpu.SyncThreads();
            return a + 1;
            }

            [GpuDeviceFunction]
            public static int HelperPure(int a, int b) => (a ^ b) + a;

            [GpuKernel]
            public static void KernelNoInline(int[] inA, int[] inB, int[] inC, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            int v0 = inA[gid];
            int v1 = inB[gid];
            int r1 = HelperByRef(ref v0, v1);
            int r2 = HelperWithBarrier(v0);
            int r3 = HelperPure(++v0, v1);
            int r4 = HelperPure(v0 / 3, v1);
            hash = (hash ^ (ulong)(r1 ^ r2 ^ r3 ^ r4)) * 1099511628211UL;
            checksum[gid] = hash;
            }
            }
            """;

        // Calls to `ref`-parameter helpers (`HelperByRef`), barrier-containing helpers (`HelperWithBarrier`), or pure helpers passed mutating/dividing arguments (`HelperPure(++v0, v1)`, `HelperPure(v0 / 3, v1)`) must never be inlined.
        // mutant: MetamorphicDeviceFunctionInliner.Apply(): skip `HasOnlySupportedByValueParameters`, `ContainsBarrierOrWarpCall`, or `AreAllArgumentsPure`
        // input: NonInlinableCallsSource with `HelperByRef`, `HelperWithBarrier`, `HelperPure(++v0, v1)`, and `HelperPure(v0 / 3, v1)`
        Assert.Equal(
            NonInlinableCallsSource,
            FuzzMetamorphicRewriter.InlineDeviceFunctionCall(NonInlinableCallsSource, seed: 1UL, targetMethodName: "KernelNoInline"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Transform4_Negative_ReorderIndependentStatements_RejectsPotentiallyAliasedArraysAndBarriers()
    {
        const string AliasedArraysAndBarrierSource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public static partial class MetaNegativeReorderKernels
            {
            [GpuKernel]
            public static void KernelAliasedAndBarrier(int[] arrA, int[] arrB, int[] inC, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            arrA[gid] = 42;
            int fromB = arrB[gid] + 1;
            Gpu.SyncThreads();
            int afterBarrier = inC[gid] ^ 7;
            hash = (hash ^ (ulong)(fromB ^ afterBarrier)) * 1099511628211UL;
            checksum[gid] = hash;
            }
            }
            """;

        // Writing to `arrA[gid]` and reading from `arrB[gid]` must not be reordered because `arrA` and `arrB` can alias the same array at the call site, and `Gpu.SyncThreads()` is an impenetrable reordering barrier.
        // mutant: MetamorphicEffectAnalyzer.TryCollectExpressionEffects(): treat `arrA[gid] = 42` as writing only symbol `arrA` and allow reordering across `arrB[gid]` or `Gpu.SyncThreads()`
        // input: AliasedArraysAndBarrierSource with `arrA[gid] = 42; int fromB = arrB[gid] + 1; Gpu.SyncThreads(); int afterBarrier = inC[gid] ^ 7;`
        Assert.Equal(
            AliasedArraysAndBarrierSource,
            FuzzMetamorphicRewriter.ReorderIndependentStatements(AliasedArraysAndBarrierSource, seed: 1UL, targetMethodName: "KernelAliasedAndBarrier"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Transform5_Negative_SwapCommutativeOperands_RejectsFloatingPointNaNPayloadAndNarrowPromotion()
    {
        const string FloatAndNarrowSource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public static partial class MetaNegativeSwapKernels
            {
            [GpuKernel]
            public static void KernelFloatAndNarrow(float[] fInA, float[] fInB, double[] dInA, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            float fA = fInA[gid];
            float fB = fInB[gid];
            float fSum = fA + fB;
            float fProd = fA * fB;
            double dA = dInA[gid];
            double dB = (double)fB;
            double dSum = dA + dB;
            short sA = (short)1;
            short sB = (short)2;
            int narrowSum = sA + sB;
            hash = (hash ^ (ulong)narrowSum) * 1099511628211UL;
            checksum[gid] = hash;
            }
            }
            """;

        // Floating-point `+` and `*` (`fA + fB`, `fA * fB`, `dA + dB`) must never be swapped because distinct NaN operand payloads can produce different result NaN payload bits; narrow `short + short` (whose result type promotes to `int`) is also excluded.
        // mutant: MetamorphicCommutativeSwapper.Apply(): allow `SpecialType.System_Single` and `SpecialType.System_Double` in `+` and `*` operand swapping
        // input: FloatAndNarrowSource with `float` (`fA + fB`, `fA * fB`), `double` (`dA + dB`), and `short` (`sA + sB`)
        Assert.Equal(
            FloatAndNarrowSource,
            FuzzMetamorphicRewriter.SwapCommutativeOperands(FloatAndNarrowSource, seed: 1UL, targetMethodName: "KernelFloatAndNarrow"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Transform6_Negative_WrapStatementInControlFlow_RejectsDirectNestedAndTransitiveBarrierOrWarpCalls()
    {
        const string BarrierStatementsOnlySource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public static partial class MetaNegativeWrapKernels
            {
            [GpuDeviceFunction]
            public static void HelperSync()
            {
            Gpu.SyncThreads();
            }

            [GpuKernel]
            public static void KernelBarriersOnly(int[] inA, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            Gpu.SyncThreads();
            {
            Gpu.SyncThreads();
            }
            HelperSync();
            checksum[gid] = hash;
            }
            }
            """;

        // Statements that directly call `Gpu.SyncThreads()`, contain a barrier inside a nested block, or call a `[GpuDeviceFunction]` that transitively calls a barrier must never be wrapped in control flow.
        // mutant: MetamorphicControlFlowWrapper.IsWrappableExecutableStatement(): remove `MetamorphicEffectAnalyzer.ContainsBarrierOrWarpCall` check
        // input: BarrierStatementsOnlySource with `Gpu.SyncThreads();`, `{ Gpu.SyncThreads(); }`, and `HelperSync();`
        Assert.Equal(
            BarrierStatementsOnlySource,
            FuzzMetamorphicRewriter.WrapStatementInControlFlow(
                BarrierStatementsOnlySource,
                seed: 1UL,
                targetMethodName: "KernelBarriersOnly",
                wrapKind: MetamorphicWrapKind.IfTrue));
        Assert.Equal(
            BarrierStatementsOnlySource,
            FuzzMetamorphicRewriter.WrapStatementInControlFlow(
                BarrierStatementsOnlySource,
                seed: 1UL,
                targetMethodName: "KernelBarriersOnly",
                wrapKind: MetamorphicWrapKind.SingleIterationLoop));
        Assert.Equal(
            BarrierStatementsOnlySource,
            FuzzMetamorphicRewriter.WrapStatementInControlFlow(
                BarrierStatementsOnlySource,
                seed: 1UL,
                targetMethodName: "KernelBarriersOnly",
                wrapKind: MetamorphicWrapKind.Block));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Transform7_Negative_AddDeadCode_RejectsPrologueOnlyOrBrokenMethodAndNeverEmitsOrWrapsBarriers()
    {
        const string PrologueOnlySource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public static partial class MetaNegativeDeadCodeKernels
            {
            [GpuKernel]
            public static void KernelPrologueOnly(ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            }
            }
            """;

        const string BarrierKernelSource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public static partial class MetaNegativeDeadCodeKernels
            {
            [GpuKernel]
            public static void KernelWithBarrier(int[] inA, int[] inB, int[] inC, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            ulong hash = 14695981039346656037UL;
            int v0 = inA[0] + inB[0];
            Gpu.SyncThreads();
            hash = (hash ^ (ulong)v0) * 1099511628211UL;
            if (gid < checksum.Length)
            {
            checksum[gid] = hash;
            }
            }
            }
            """;

        // A method with no executable body after its prologue must not be rewritten, and when dead code is inserted into a kernel with `Gpu.SyncThreads()`, the unreachable `if (false)` block must never contain or wrap any barrier or warp call.
        // mutant: MetamorphicDeadCodeInserter.Apply(): remove `minInsertIdx >= statements.Count` guard or emit `Gpu.SyncThreads()` inside `if (false)`
        // input: PrologueOnlySource (no statements after `ulong hash = ...`) and BarrierKernelSource (containing `Gpu.SyncThreads()`)
        Assert.Equal(
            PrologueOnlySource,
            FuzzMetamorphicRewriter.AddDeadCode(PrologueOnlySource, seed: 1UL, targetMethodName: "KernelPrologueOnly"));

        VerifyZeroGeneratorAndAnalyzerDiagnostics(BarrierKernelSource, "Cpu");
        string barrierWithDeadCode = FuzzMetamorphicRewriter.AddDeadCode(
            BarrierKernelSource,
            seed: 1UL,
            targetMethodName: "KernelWithBarrier");
        Assert.Contains("if (false)", barrierWithDeadCode, StringComparison.Ordinal);
        VerifyZeroGeneratorAndAnalyzerDiagnostics(barrierWithDeadCode, "Cpu");
        VerifyZeroGeneratorAndAnalyzerDiagnostics(barrierWithDeadCode, "Cuda");
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

        var generator = new ScalarFuzzGenerator(options);
        var batch = generator.GenerateBatch();

        // BatchSize=3 with MetamorphicVariantsPerKernel=2 emits 3 base kernels + 6 metamorphic variants = 9 kernels total.
        // mutant: ScalarFuzzGenerator.GenerateBatch(): skip metamorphic variant emission when `_options.Metamorphic` is true
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

    private static void AssertCompilesAndMatchesRuntime(
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

    private static void VerifyZeroGeneratorAndAnalyzerDiagnostics(string source, string dialect)
    {
        var tree = CSharpSyntaxTree.ParseText(source, FuzzTestCompilationHelper.ParseOptions, path: $"MetaVerify_{dialect}.cu.cs");
        var compilation = CSharpCompilation.Create(
            $"MetaVerify_{dialect}",
            [tree],
            FuzzTestCompilationHelper.BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithAllowUnsafe(true)
                .WithOptimizationLevel(OptimizationLevel.Release));

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
}
