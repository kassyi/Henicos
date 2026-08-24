using Xunit;

namespace Kassyi.GSharp.Stdlib.Math.Tests;

/// <summary>
/// GpuBigInt (GBI) 課題 &amp; 演算子進捗管理表 (docs/roadmaps/gbi_roadmap.md) に基づくテストスタブ。
/// 未実装の演算子・課題はすべて [Fact(Skip = "...")] で定義され、実装完了時にスキップを解除してテストを完成させます。
/// </summary>
public class GpuBigIntRoadmapStubTests
{
    #region 1. GBI アーキテクチャ・最適化課題

    [Fact(Skip = "未実装・課題: GBI-01 (GSharpJitProvider のメモリプール不在 / スラブアロケータ)")]
    [Trait("RoadmapId", "GBI-01")]
    [Trait("Category", "Memory")]
    public void Test_GBI01_MemoryPoolAllocation()
    {
    }

    [Fact(Skip = "未実装・課題: GBI-03 (CPU-Fallback メソッドの GPU 並列リダクション化)")]
    [Trait("RoadmapId", "GBI-03")]
    [Trait("Category", "Performance")]
    public void Test_GBI03_GpuReductionCompareAndFlags()
    {
    }

    [Fact(Skip = "未実装・課題: GBI-04 (Square 専用 JIT Pointwise カーネルの直接ディスパッチ)")]
    [Trait("RoadmapId", "GBI-04")]
    [Trait("Category", "Optimization")]
    public void Test_GBI04_DirectSquarePointwiseKernel()
    {
    }

    [Fact(Skip = "未実装・課題: GBI-05 (Lehmer's GCD の大桁並列化 / Half-GCD)")]
    [Trait("RoadmapId", "GBI-05")]
    [Trait("Category", "Algorithm")]
    public void Test_GBI05_ParallelGcdLargeDigits()
    {
    }

    [Fact(Skip = "未実装・課題: GBI-06 (式木 GpuBigIntExpr の動的 Kernel Fusion)")]
    [Trait("RoadmapId", "GBI-06")]
    [Trait("Category", "Runtime")]
    public void Test_GBI06_ExpressionKernelFusion()
    {
    }

    #endregion

    #region 2. GBI 未実装演算子 & 関数

    [Fact(Skip = "未実装: GBI-OP-INC (インクリメント/デクリメント演算子 ++, --)")]
    [Trait("RoadmapId", "GBI-OP-INC")]
    [Trait("Category", "Operator")]
    public void Test_GBI_Operator_IncrementDecrement()
    {
    }

    [Fact(Skip = "未実装: GBI-FN-MODINV (GpuBigInteger.ModInverse モジュラ逆数)")]
    [Trait("RoadmapId", "GBI-FN-MODINV")]
    [Trait("Category", "Function")]
    public void Test_GBI_Function_ModInverse()
    {
    }

    #endregion
}
