namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_08_LambdaAndLocalFunctionsTests
:  TranspilerTestBase{
    [MemberData(nameof(TranspilerTestBase.Dialects))]
    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-08")]
    public void Test_SYN08_NonCapturingLambdaKernel(ITranspilerDialect dialect)
    {
        // Happy Path: キャプチャを持たない、あるいは単純な値型のみをキャプチャするラムダ式 (() => { }) が、C++側で一意な名前（決定的かつ競合しない名前）を持つ抽出された device 関数（またはC++ラムダ）として正しく展開され実行されること。
        int[] data = [1, 5, 20];
        SYN_08_LambdaAndLocalFunctionsKernelsWrapper.Launch_NonCapturingLambdaKernel(data, blockDimX: 3);
        Assert.Equal(2, data[0]);
        Assert.Equal(10, data[1]);
        Assert.Equal(40, data[2]);
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-08")]
    [InlineData(2, 5, 10)]
    [InlineData(3, 10, 30)]
    public void Test_SYN08_ValueCapturingLambdaKernel(int factor, int input, int expected)
    {
        // Happy Path: 単純な値型のみをキャプチャするラムダ式が、一意かつ決定的な名前で抽出された device 関数として正しく展開・実行されること。
        int[] data = [input];
        SYN_08_LambdaAndLocalFunctionsKernelsWrapper.Launch_ValueCapturingLambdaKernel(data, factor, blockDimX: 1);
        Assert.Equal(expected, data[0]);
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-08")]
    public void Test_SYN08_ImmediatelyEvaluatedLambdaKernel(ITranspilerDialect dialect)
    {
        // Edge Cases: メソッド引数内で即時評価されるラムダや、ネストされたラムダ式におけるキャプチャスコープの解決
        int[] data = [2, 4, 7];
        SYN_08_LambdaAndLocalFunctionsKernelsWrapper.Launch_ImmediatelyEvaluatedLambdaKernel(data, blockDimX: 3);
        Assert.Equal(102, data[0]);
        Assert.Equal(104, data[1]);
        Assert.Equal(107, data[2]);
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-08")]
    public void Test_SYN08_ShadowedVariableCaptureKernel(ITranspilerDialect dialect)
    {
        // Edge Cases: 同名変数のキャプチャ時の名前衝突回避が正しく行われること。
        int[] data = [100];
        SYN_08_LambdaAndLocalFunctionsKernelsWrapper.Launch_ShadowedVariableCaptureKernel(data, blockDimX: 1);
        Assert.Equal(250, data[0]);
    }
}
