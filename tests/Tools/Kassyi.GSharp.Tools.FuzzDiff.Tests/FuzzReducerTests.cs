#nullable enable
using System.Globalization;
using System.Runtime.Loader;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Tests.Shared.Differential;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff.Tests;

/// <summary>
/// Property and soundness tests for <see cref="FuzzReducer"/>, <see cref="FuzzFailureSignature"/>,
/// <see cref="FuzzSpeculativeBatchBuilder"/>, and <c>#1103</c> hoisting guards.
/// </summary>
public class FuzzReducerTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void FuzzReducer_Synthetic100LineFailingKernel_ShrinksBelow15StatementsPreservingFailure()
    {
        string synthetic100LineSource = BuildSynthetic100StatementMultiFunctionKernel();
        int initialStatements = FuzzReducer.CountKernelExecutableStatements(synthetic100LineSource);
        int initialHelpers = FuzzReducer.CountDeviceFunctionMethods(synthetic100LineSource);
        Assert.True(initialStatements >= 95, $"Expected synthetic kernel to start with >= 95 statements, got {initialStatements}.");
        Assert.Equal(3, initialHelpers);

        bool IsInteresting(string candidateSource) =>
            CompilesWithZeroDiagnosticsAndSimulateShiftMismatch(candidateSource, out bool mismatchReproduced, out _) &&
            mismatchReproduced;

        Assert.True(IsInteresting(synthetic100LineSource), "Expected initial 100-statement kernel to reproduce the planted shift mismatch.");

        var reduction = FuzzReducer.Reduce(synthetic100LineSource, IsInteresting);

        CompilesWithZeroDiagnosticsAndSimulateShiftMismatch(
            reduction.ReducedSource,
            out bool reducedStillFails,
            out var reducedGshDiags);

        // The reduced kernel must preserve the failure, shrink to <= 15 statements and 0 helper [GpuDeviceFunction] methods, and emit 0 GSH* diagnostics.
        // mutant: FuzzReducer.CoarseRemoveStatementsInMethod(): change `for (int chunkIndex = granularity - 1; chunkIndex >= 0; chunkIndex--)` to `for (int chunkIndex = granularity - 1; chunkIndex >= granularity; chunkIndex--)`
        // input: Synthetic100StatementKernel (102 statements, 3 [GpuDeviceFunction] helpers HelperStep1 -> HelperStep2 -> HelperLeafShiftBug with planted `a >>> b` bug)
        Assert.True(reducedStillFails, "Reduced kernel must still reproduce the planted mismatch.");
        Assert.True(
            reduction.ReducedStatementCount <= 15,
            $"Expected reduced kernel to have <= 15 statements, but got {reduction.ReducedStatementCount}.{Environment.NewLine}{reduction.ReducedSource}");
        Assert.Equal(0, reduction.ReducedDeviceFunctionCount);
        Assert.Empty(reducedGshDiags);

        var specBatch = FuzzReducer.GenerateSpeculativeBatchCandidates(
            synthetic100LineSource,
            fuzzSeed: 0xBEEFUL,
            inputSeed: 0,
            maxCandidates: 16);

        Assert.Equal(16, specBatch.Candidates.Count);
        var batchAssembly = FuzzTestCompilationHelper.CompileToInMemoryAssembly("SpeculativeBatch16Assembly", specBatch.KernelsSource);
        Assert.NotNull(batchAssembly.GetType("Kassyi.GSharp.Tests.Shared.Differential.FuzzBatchKernels"));

        var bestCandidate = FuzzReducer.SelectBestSpeculativeCandidate(
            specBatch,
            cand =>
            {
                string singleUnit = ReplaceKernelInSyntheticUnit(synthetic100LineSource, cand.CandidateKernelMethodSource, "SyntheticBugKernel");
                return IsInteresting(singleUnit);
            });

        Assert.NotNull(bestCandidate);
        Assert.True(bestCandidate.StatementCount < initialStatements);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void FuzzReducer_FailureSignaturePreservation_RejectsCandidateThatSwitchesBug()
    {
        const string DualBugSource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public static partial class DualBugKernels
            {
            [GpuKernel]
            public static void DualBugKernel(int[] inA, int[] inB, int[] inC, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            int bugATrigger = inA[gid] >>> inC[gid];
            int bugBTrigger = (-inB[gid]) / 3;
            int noise1 = inA[gid] + 11;
            int noise2 = inB[gid] ^ 22;
            hash = (hash ^ (((ulong)(bugATrigger ^ bugBTrigger)) + 1UL)) * 1099511628211UL;
            checksum[gid] = hash;
            }
            }
            """;

        var expectedBugA = FuzzFailureSignature.Create("Native,Cuda", "CellMismatch", "hash");
        var bugBSignaturePaths = FuzzFailureSignature.Create("Native", "CellMismatch", "hash");
        var bugBSignatureKind = FuzzFailureSignature.Create("Native,Cuda", "RuntimeException:DivideByZeroException", "$exception");

        // FuzzFailureSignature.Matches must reject candidates that switch failing paths, failure kind, or column.
        // mutant: FuzzFailureSignature.Matches(): return true unconditionally
        // input: expectedBugA ("Native,Cuda", "CellMismatch", "hash") vs bugBSignaturePaths ("Native", ...) and bugBSignatureKind (..., "RuntimeException:DivideByZeroException", "$exception")
        Assert.False(expectedBugA.Matches(bugBSignaturePaths));
        Assert.False(expectedBugA.Matches(bugBSignatureKind));
        Assert.False(expectedBugA.Matches("Native,Cuda", "CellMismatch", "other_col"));
        Assert.True(expectedBugA.Matches("Cuda, Native", "CellMismatch", "hash"));

        var reduced = FuzzReducer.Reduce(
            DualBugSource,
            expectedBugA,
            candidate =>
            {
                bool hasBugA = candidate.Contains(">>>", StringComparison.Ordinal);
                bool hasBugB = candidate.Contains("/ 3", StringComparison.Ordinal);
                if (hasBugA)
                {
                    return expectedBugA;
                }

                if (hasBugB)
                {
                    return bugBSignaturePaths;
                }

                return null;
            });

        // Reduction must keep the `>>>` statement that triggers Bug A (`Native,Cuda`) and remove `bugBTrigger` (`Native`-only) and noise statements.
        // mutant: FuzzReducer.Reduce(source, expectedFailure, evaluateCandidate): change `expectedFailure.Matches(actual)` to `actual is not null`
        // input: DualBugSource with Bug A (`>>>`) and Bug B (`/ 3`)
        Assert.Contains(">>>", reduced.ReducedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("noise1", reduced.ReducedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("noise2", reduced.ReducedSource, StringComparison.Ordinal);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void FuzzReducer_HoistLocalsToDefault_SkipsStructsWithConversionOperatorsWhile1103Open()
    {
        const string StructKernelSource =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public struct FuzzWrapInt
            {
            public int Value;
            public static implicit operator FuzzWrapInt(int v) => new FuzzWrapInt { Value = v };
            }

            public struct FuzzStructL1
            {
            public int A;
            public short B;
            }

            public struct FuzzStructL3
            {
            public FuzzWrapInt Wrap;
            public ushort Flags;
            }

            public static partial class HoistGuardKernels
            {
            [GpuKernel]
            public static void KernelHoistCheck(int[] inA, int[] inB, int[] inC, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            FuzzStructL1 sL1 = new FuzzStructL1 { A = inA[gid], B = 1 };
            FuzzWrapInt sWrap = (FuzzWrapInt)inA[gid];
            FuzzStructL3 sL3 = new FuzzStructL3 { Wrap = sWrap, Flags = 3 };
            hash = (hash ^ (ulong)(sL1.A + sWrap.Value + sL3.Flags)) * 1099511628211UL;
            checksum[gid] = hash;
            }
            }
            """;

        string hoisted = FuzzReducer.HoistLocalsToDefault(StructKernelSource, avoidStructConversionDefault: true);

        // Plain structs without conversion operators (`FuzzStructL1`) are hoisted to `default(FuzzStructL1)`, whereas structs with conversion operators (`FuzzWrapInt`, `FuzzStructL3`) must NOT be hoisted to `default(T)` while #1103 is open.
        // mutant: FuzzSyntaxHelpers.IsHoistableLocalType(): remove `if (avoidStructConversionDefault && HasConversionOperatorInStructHierarchy(root, text)) return false;`
        // input: StructKernelSource declaring FuzzStructL1, FuzzWrapInt, and FuzzStructL3
        Assert.Contains("default(FuzzStructL1)", hoisted, StringComparison.Ordinal);
        Assert.DoesNotContain("default(FuzzWrapInt)", hoisted, StringComparison.Ordinal);
        Assert.DoesNotContain("default(FuzzStructL3)", hoisted, StringComparison.Ordinal);
    }

    private static string BuildSynthetic100StatementMultiFunctionKernel()
    {
        using var writer = new SourceWriter();
        writer.AppendLine(
            """
            #nullable enable
            #pragma warning disable CS0162, CS0219, CS1717, CS1718
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public static partial class SyntheticReduceKernels
            {
            [GpuDeviceFunction]
            public static int HelperLeafShiftBug(int a, int shiftCount)
            {
            return (a >>> shiftCount);
            }

            [GpuDeviceFunction]
            public static int HelperStep2(int x, int y, int s)
            {
            int t = x ^ y;
            return HelperLeafShiftBug(t, s);
            }

            [GpuDeviceFunction]
            public static int HelperStep1(int a, int b, int c)
            {
            int p = a + 17;
            return HelperStep2(p, b, c);
            }

            [GpuKernel]
            public static void SyntheticBugKernel(int[] inA, int[] inB, int[] inC, ulong[] checksum)
            {
            int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (gid >= checksum.Length)
            {
            return;
            }

            ulong hash = 14695981039346656037UL;
            int acc = inA[gid];
            """);

        for (int i = 0; i < 45; i++)
        {
            writer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"int noisePre_{i} = (inA[gid] + {i * 3 + 1}) ^ inB[gid];"));
        }

        writer.AppendLine("int bugVal = HelperStep1(inA[gid], inB[gid], inC[gid]);");

        for (int i = 0; i < 48; i++)
        {
            writer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"int noisePost_{i} = (acc ^ {i * 7 + 5}) + inB[gid];"));
        }

        writer.AppendLine(
            """
            hash = (hash ^ (((ulong)bugVal) + 1UL)) * 1099511628211UL;
            checksum[gid] = hash;
            }
            }
            """);
        return writer.ToString();
    }

    private static string ReplaceKernelInSyntheticUnit(string baseUnit, string candidateMethodSource, string targetKernelName)
    {
        var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(baseUnit, FuzzTestCompilationHelper.ParseOptions).GetRoot();
        MethodDeclarationSyntax? oldKernel = null;
        foreach (var node in root.DescendantNodes())
        {
            if (node is MethodDeclarationSyntax m && string.Equals(m.Identifier.ValueText, targetKernelName, StringComparison.Ordinal))
            {
                oldKernel = m;
                break;
            }
        }

        if (oldKernel is null)
        {
            return baseUnit;
        }

        MethodDeclarationSyntax? newKernel = null;
        foreach (var node in CSharpSyntaxTree.ParseText($"class __W {{ {candidateMethodSource} }}", FuzzTestCompilationHelper.ParseOptions).GetRoot().DescendantNodes())
        {
            if (node is MethodDeclarationSyntax m)
            {
                newKernel = m.WithIdentifier(SyntaxFactory.Identifier(targetKernelName));
                break;
            }
        }

        if (newKernel is null)
        {
            return baseUnit;
        }

        return FuzzTestCompilationHelper.RenderNode(root.ReplaceNode(oldKernel, newKernel));
    }

    private static bool CompilesWithZeroDiagnosticsAndSimulateShiftMismatch(
        string candidateSource,
        out bool mismatchReproduced,
        out Diagnostic[] gshDiagnostics)
    {
        mismatchReproduced = false;
        gshDiagnostics = [];

        var oracleTree = CSharpSyntaxTree.ParseText(candidateSource, FuzzTestCompilationHelper.ParseOptions, path: "ReduceCheck.cu.cs");
        var compilation = CSharpCompilation.Create(
            "ReduceCheckAssembly",
            [oracleTree],
            FuzzTestCompilationHelper.BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithAllowUnsafe(true)
                .WithOptimizationLevel(OptimizationLevel.Debug));

        var configProvider = new FuzzTestCompilationHelper.FuzzTestAnalyzerConfigOptionsProvider("Cpu");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new GpuKernelGenerator().AsSourceGenerator()],
            additionalTexts: null,
            parseOptions: FuzzTestCompilationHelper.ParseOptions,
            optionsProvider: configProvider);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiags);

        var gshList = new List<Diagnostic>();
        foreach (var d in outputCompilation.GetDiagnostics())
        {
            if (d.Severity == DiagnosticSeverity.Error)
            {
                return false;
            }

            if (d.Id.StartsWith("GSH", StringComparison.Ordinal) &&
                d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            {
                gshList.Add(d);
            }
        }

        foreach (var d in generatorDiags)
        {
            if (d.Severity == DiagnosticSeverity.Error)
            {
                return false;
            }

            if (d.Id.StartsWith("GSH", StringComparison.Ordinal) &&
                d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            {
                gshList.Add(d);
            }
        }

        gshDiagnostics = gshList.ToArray();
        if (gshDiagnostics.Length > 0)
        {
            return false;
        }

        var oracleRoot = (CompilationUnitSyntax)oracleTree.GetRoot();
        ClassDeclarationSyntax? oracleClass = null;
        foreach (var node in oracleRoot.DescendantNodes())
        {
            if (node is ClassDeclarationSyntax c && string.Equals(c.Identifier.ValueText, "SyntheticReduceKernels", StringComparison.Ordinal))
            {
                oracleClass = c;
                break;
            }
        }

        if (oracleClass is null)
        {
            return false;
        }

        var brokenClass = (ClassDeclarationSyntax)new BrokenKernelCloneRewriter().Visit(oracleClass)!;
        var combinedRoot = oracleRoot.AddMembers(brokenClass);
        string combinedSource = FuzzTestCompilationHelper.RenderNode(combinedRoot);

        var execTree = CSharpSyntaxTree.ParseText(combinedSource, FuzzTestCompilationHelper.ParseOptions, path: "ReduceExec.cu.cs");
        var execCompilation = CSharpCompilation.Create(
            "ReduceExecAssembly",
            [execTree],
            FuzzTestCompilationHelper.BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithAllowUnsafe(true)
                .WithOptimizationLevel(OptimizationLevel.Debug));

        using var peStream = new MemoryStream();
        var emitRes = execCompilation.Emit(peStream);
        if (!emitRes.Success)
        {
            return false;
        }

        peStream.Position = 0;
        var alc = new AssemblyLoadContext("ReduceTempAlc", isCollectible: true);
        try
        {
            var asm = alc.LoadFromStream(peStream);
            var oracleType = asm.GetType("Kassyi.GSharp.Tests.Shared.Differential.SyntheticReduceKernels");
            var brokenType = asm.GetType("Kassyi.GSharp.Tests.Shared.Differential.SyntheticReduceKernels_Broken");
            if (oracleType is null || brokenType is null)
            {
                return false;
            }

            var oracleDel = FuzzTestCompilationHelper.CreateKernelDelegate(oracleType, "SyntheticBugKernel");
            var brokenDel = FuzzTestCompilationHelper.CreateKernelDelegate(brokenType, "SyntheticBugKernel");

            var shape = LaunchShapes.PartialWarp(rows: 16, blockX: 16);
            int[] inA = new int[16];
            int[] inB = new int[16];
            int[] inC = new int[16];
            for (int i = 0; i < 16; i++)
            {
                inA[i] = unchecked((int)0x87654321U) + i;
                inB[i] = 0x11112222 + i;
                inC[i] = 35;
            }

            ulong[] oracleOut = new ulong[16];
            ulong[] brokenOut = new ulong[16];
            SequentialOracle.Run(shape, ThreadOrder.Forward, () => oracleDel(inA, inB, inC, oracleOut));
            SequentialOracle.Run(shape, ThreadOrder.Forward, () => brokenDel(inA, inB, inC, brokenOut));

            for (int i = 0; i < 16; i++)
            {
                if (oracleOut[i] != brokenOut[i])
                {
                    mismatchReproduced = true;
                    break;
                }
            }

            return true;
        }
        finally
        {
            alc.Unload();
        }
    }

    private sealed class BrokenKernelCloneRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;
            if (string.Equals(visited.Identifier.ValueText, "SyntheticReduceKernels", StringComparison.Ordinal))
            {
                return visited.WithIdentifier(SyntaxFactory.Identifier("SyntheticReduceKernels_Broken"));
            }

            return visited;
        }

        public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            var visited = (BinaryExpressionSyntax)base.VisitBinaryExpression(node)!;
            if (visited.IsKind(SyntaxKind.UnsignedRightShiftExpression))
            {
                string lhs = FuzzTestCompilationHelper.RenderNode(visited.Left.WithoutTrivia());
                string rhs = FuzzTestCompilationHelper.RenderNode(visited.Right.WithoutTrivia());
                return SyntaxFactory.ParseExpression($"((uint)({rhs}) >= 32U ? 0 : (({lhs}) >>> ({rhs})))").WithTriviaFrom(visited);
            }

            return visited;
        }
    }
}
