
namespace Kassyi.GSharp.Tests.Transpiler;

public class GSH_01_05_TranspilerIssuesTests
{
    [Fact(Skip = "未実装: GSH-01 (三項演算子の複雑式展開)")]
    [Trait("FeatureId", "GSH-01")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH01_ComplexConditionalExpression()
    {
    }

    [Fact(Skip = "未実装: GSH-02 (タプル構文の一般化)")]
    [Trait("FeatureId", "GSH-02")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH02_GeneralTupleSupport()
    {
    }

    [Fact]
    [Trait("FeatureId", "GSH-03")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH03_LocalFunctionsSupport()
    {
        var testInstance = new Syntax.SYN_07_08_LocalFunctionsAndLambdasTests();
        testInstance.Test_GSH03_SYN07_LocalFunction_Transpilation_HappyPath();
    }

    [Fact(Skip = "未実装: GSH-04 (スタック固定長配列・stackallocの限定性)")]
    [Trait("FeatureId", "GSH-04")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH04_StackAllocFixedArray()
    {
    }

    [Fact(Skip = "未実装: GSH-05 (ジェネリクスの制約解決)")]
    [Trait("FeatureId", "GSH-05")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH05_GenericsConstraintResolution()
    {
    }
}
