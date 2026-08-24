using Xunit;

namespace Kassyi.GSharp.Tests;

public class TranspilerRuntimeIssuesTests
{
    [Fact(Skip = "未実装: GSH-01 (三項演算子の複雑式展開)")]
    [Trait("RoadmapId", "GSH-01")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH01_ComplexConditionalExpression()
    {
    }

    [Fact(Skip = "未実装: GSH-02 (タプル構文の一般化)")]
    [Trait("RoadmapId", "GSH-02")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH02_GeneralTupleSupport()
    {
    }

    [Fact(Skip = "未実装: GSH-03 (ローカル関数の非サポート)")]
    [Trait("RoadmapId", "GSH-03")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH03_LocalFunctionsSupport()
    {
    }

    [Fact(Skip = "未実装: GSH-04 (スタック固定長配列・stackallocの限定性)")]
    [Trait("RoadmapId", "GSH-04")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH04_StackAllocFixedArray()
    {
    }

    [Fact(Skip = "未実装: GSH-05 (ジェネリクスの制約解決)")]
    [Trait("RoadmapId", "GSH-05")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH05_GenericsConstraintResolution()
    {
    }

    [Fact(Skip = "未実装: GSH-06 (PTX / C++ コンパイルエラーの行番号マッピング)")]
    [Trait("RoadmapId", "GSH-06")]
    [Trait("Category", "Runtime")]
    public void Test_GSH06_LineNumberMappingInDiagnostics()
    {
    }

    [Fact(Skip = "未実装: GSH-07 (JIT ディスクキャッシュ機構)")]
    [Trait("RoadmapId", "GSH-07")]
    [Trait("Category", "Runtime")]
    public void Test_GSH07_JitDiskCache()
    {
    }
}
