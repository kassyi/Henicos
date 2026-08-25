using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class Syn57_93_ModernPatternsTests
{
    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-93")]
    public void TestDeviceMethodKernel()
    {
        int[] dataArray = [5, 10];
        Syn57_93_ModernPatternsKernelsWrapper.Launch_DeviceMethodKernel(dataArray, blockDimX: 2);

        Assert.Equal(105, dataArray[0]);
        Assert.Equal(110, dataArray[1]);
    }

    [Fact(Skip = "未実装: SYN-57 (リストパターン [1, .., var last])")]
    [Trait("FeatureId", "SYN-57")]
    [Trait("Category", "Syntax")]
    public void Test_SYN57_ListPatterns_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-58 (プロパティパターン { Length: > 0 })")]
    [Trait("FeatureId", "SYN-58")]
    [Trait("Category", "Syntax")]
    public void Test_SYN58_PropertyPatterns_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-59 (位置パターン (0, 0) / (var x, > 10))")]
    [Trait("FeatureId", "SYN-59")]
    [Trait("Category", "Syntax")]
    public void Test_SYN59_PositionalPatterns_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-60 (ラムダ式のデフォルト引数 C# 12)")]
    [Trait("FeatureId", "SYN-60")]
    [Trait("Category", "Syntax")]
    public void Test_SYN60_LambdaDefaultParameters_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-61 (ラムダ式の明示的戻り値型指定 C# 10)")]
    [Trait("FeatureId", "SYN-61")]
    [Trait("Category", "Syntax")]
    public void Test_SYN61_LambdaExplicitReturnType_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-62 (scoped ref / scoped Span<T> C# 11)")]
    [Trait("FeatureId", "SYN-62")]
    [Trait("Category", "Syntax")]
    public void Test_SYN62_ScopedRef_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-63 (Unsafe.NullRef<T>() / IsNullRef)")]
    [Trait("FeatureId", "SYN-63")]
    [Trait("Category", "Syntax")]
    public void Test_SYN63_UnsafeNullRef_Transpilation()
    {
    }
}

