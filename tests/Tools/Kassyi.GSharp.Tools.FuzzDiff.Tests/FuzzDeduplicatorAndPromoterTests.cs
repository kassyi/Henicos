#nullable enable

namespace Kassyi.GSharp.Tools.FuzzDiff.Tests;

/// <summary>
/// Property and soundness tests for <see cref="FuzzDeduplicator"/>, <see cref="FuzzAstNormalizer"/>,
/// <see cref="FuzzPromoter"/>, and the <see cref="IFuzzCommand"/> CLI subcommands.
/// </summary>
public class FuzzDeduplicatorAndPromoterTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void FuzzDeduplicator_AlphaRenamedAndDeadCodePaddedKernels_ProduceIdenticalFingerprint()
    {
        const string KernelVariantA =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public enum CustomModeA : byte { Zero = 0, One = 1, Mask = 0x55 }

            public static partial class FuzzDedupA
            {
                [GpuDeviceFunction]
                public static int UnusedHelperA(int z) => z + 123;

                [GpuKernel]
                public static void KernelAlpha_01(int[] inA, int[] inB, int[] inC, ulong[] checksum)
                {
                    int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
                    if (gid >= checksum.Length)
                    {
                        return;
                    }

                    ulong hash = 14695981039346656037UL;
                    int deadBefore = default(int);
                    deadBefore = inB[gid] + 777;
                    int v0 = inA[gid];
                    int v1 = inC[gid];
                    CustomModeA modeA = (CustomModeA)(byte)inB[gid];
                    int v2 = ((v0 + 11) >>> v1) ^ (int)modeA;
                    short deadAfter = default(short);
                    deadAfter = (short)42;
                    hash = (hash ^ (((ulong)v2) + 1UL)) * 1099511628211UL;
                    checksum[gid] = hash;
                }
            }
            """;

        const string KernelVariantB =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public enum RenamedModeB : byte { Zero = 0, One = 1, Mask = 0x55 }

            public static partial class FuzzDedupB
            {
                [GpuKernel]
                public static void DifferentKernelName_99(int[] arrX, int[] arrY, int[] arrZ, ulong[] checksum)
                {
                    int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
                    if (gid >= checksum.Length)
                    {
                        return;
                    }

                    ulong hash = 14695981039346656037UL;
                    long unusedTemp = default(long);
                    int x9 = arrX[gid];
                    int y8 = arrZ[gid];
                    RenamedModeB flagB = (RenamedModeB)(byte)arrY[gid];
                    int w7 = (((x9 + 9999) >>> y8) ^ (int)flagB);
                    ulong deadTail = default(ulong);
                    deadTail = 123456UL;
                    hash = (hash ^ (((ulong)w7) + 99UL)) * 1099511628211UL;
                    checksum[gid] = hash;
                }
            }
            """;

        const string StructurallyDifferentKernel =
            """
            #nullable enable
            using System;
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Tests.Shared.Differential;

            public enum CustomModeA : byte { Zero = 0, One = 1, Mask = 0x55 }

            public static partial class FuzzDedupC
            {
                [GpuKernel]
                public static void KernelGamma(int[] inA, int[] inB, int[] inC, ulong[] checksum)
                {
                    int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
                    if (gid >= checksum.Length)
                    {
                        return;
                    }

                    ulong hash = 14695981039346656037UL;
                    int v0 = inA[gid];
                    int v1 = inC[gid];
                    CustomModeA modeA = (CustomModeA)(byte)inB[gid];
                    int v2 = ((v0 * 11) << v1) ^ (int)modeA;
                    hash = (hash ^ (((ulong)v2) + 1UL)) * 1099511628211UL;
                    checksum[gid] = hash;
                }
            }
            """;

        var fpA = FuzzDeduplicator.ComputeFingerprint(KernelVariantA, "Native,Cuda", "CellMismatch");
        var fpB = FuzzDeduplicator.ComputeFingerprint(KernelVariantB, "Cuda, Native", "CellMismatch");
        var fpDiffAst = FuzzDeduplicator.ComputeFingerprint(StructurallyDifferentKernel, "Native,Cuda", "CellMismatch");
        var fpDiffPaths = FuzzDeduplicator.ComputeFingerprint(KernelVariantA, "Cuda", "CellMismatch");
        var fpDiffKind = FuzzDeduplicator.ComputeFingerprint(KernelVariantA, "Native,Cuda", "RuntimeException:DivideByZeroException");

        // Two kernels with alpha-renamed identifiers, different numeric literal constants, and dead local declarations/stores must produce the exact same fingerprint.
        // mutant: FuzzAstNormalizer.BuildVariableNormalizationMap(): change `varMap[text] = $"v{varMap.Count}";` to `varMap[text] = text;`
        // input: KernelVariantA (locals v0, v1, v2; enum CustomModeA; literal 11) vs KernelVariantB (locals x9, y8, w7; enum RenamedModeB; literal 9999)
        Assert.Equal(fpA.NormalizedAstSkeleton, fpB.NormalizedAstSkeleton);
        Assert.Equal(fpA.Fingerprint, fpB.Fingerprint);

        Assert.NotEqual(fpA.Fingerprint, fpDiffAst.Fingerprint);
        Assert.NotEqual(fpA.Fingerprint, fpDiffPaths.Fingerprint);
        Assert.NotEqual(fpA.Fingerprint, fpDiffKind.Fingerprint);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void FuzzPromoter_MultipleKernelsFromSameBatch_EmitDistinctNamesAndKnownDivergenceEntries()
    {
        var batch = new ScalarFuzzGenerator(new FuzzOptions
        {
            FuzzSeed = 0xCAFEUL,
            BatchSize = 2,
            Size = 2,
        }).GenerateBatch();

        var promoted0 = FuzzPromoter.BuildPromotionArtifacts(
            batch.KernelsSource,
            fuzzSeed: batch.FuzzSeed,
            inputSeed: 3,
            failingPaths: "Native,Cuda",
            failureKind: "CellMismatch",
            targetKernelName: "Kernel_00",
            column: "hash");

        var promoted1 = FuzzPromoter.BuildPromotionArtifacts(
            batch.KernelsSource,
            fuzzSeed: batch.FuzzSeed,
            inputSeed: 3,
            failingPaths: "Cuda",
            failureKind: "CellMismatch",
            targetKernelName: "Kernel_01",
            column: "hash");

        // Promoting two distinct kernels from the same batch must produce distinct class, method, and file stem names and include a KnownDivergence entry.
        // mutant: FuzzPromoter.TryExtractKernelSeed(): return false unconditionally and set methodSuffix = string.Empty
        // input: batch with FuzzSeed = 0xCAFEUL, Kernel_00 and Kernel_01
        Assert.NotEqual(promoted0.ReproKernelClassName, promoted1.ReproKernelClassName);
        Assert.NotEqual(promoted0.ReproKernelMethodName, promoted1.ReproKernelMethodName);
        Assert.NotEqual(promoted0.ReproFileStem, promoted1.ReproFileStem);

        Assert.Equal(
            $"new(\"{promoted0.ReproKernelClassName}.{promoted0.ReproKernelMethodName}\", \"hash\", DiffPath.Native | DiffPath.Cuda, Issue: 0),",
            promoted0.KnownDivergenceEntry);
        Assert.Contains(promoted0.KnownDivergenceEntry, promoted0.ReproCaseSnippetContent, StringComparison.Ordinal);

        Assert.Equal(
            $"new(\"{promoted1.ReproKernelClassName}.{promoted1.ReproKernelMethodName}\", \"hash\", DiffPath.Cuda, Issue: 0),",
            promoted1.KnownDivergenceEntry);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void FuzzCommands_CommandTableAndOptionParsing_SeparatesOptionsFromExecution()
    {
        Assert.Equal(5, Program.Commands.Count);
        Assert.Contains(Program.Commands, static c => c.Name == "compare");

        bool parsedGen = GenCommand.TryParseOptions(
            ["--fuzz-seed", "0x2A", "--batch-size", "8", "--profile", "G", "--allow-issue", "1101", "--swarm-focus", "coalesce-assign-value"],
            out var genOptions,
            out bool showHelp,
            out string? error);

        // GenCommand.TryParseOptions must parse CLI arguments into a GenCommandOptions record without executing I/O.
        // mutant: GenCommand.TryParseOptions(): return false on "--swarm-focus"
        // input: ["--fuzz-seed", "0x2A", "--batch-size", "8", "--profile", "G", "--allow-issue", "1101", "--swarm-focus", "coalesce-assign-value"]
        Assert.True(parsedGen);
        Assert.False(showHelp);
        Assert.Null(error);
        Assert.NotNull(genOptions);
        Assert.Equal(0x2AUL, genOptions.FuzzOptions.FuzzSeed);
        Assert.Equal(8, genOptions.FuzzOptions.BatchSize);
        Assert.Equal("G", genOptions.FuzzOptions.Profile);
        Assert.Contains(1101, genOptions.FuzzOptions.AllowedIssues);
        Assert.Equal(FuzzSwarmFocus.CoalesceAssignValue, genOptions.FuzzOptions.SwarmFocus);
    }
}
