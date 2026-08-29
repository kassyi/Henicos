
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_39_45_OperatorsAndInterfacesTests
{
    [Fact(Skip = "未実装: SYN-39 (明示的 / 暗黙的型変換演算子 implicit/explicit)")]
    [Trait("FeatureId", "SYN-39")]
    [Trait("Category", "Syntax")]
    public void Test_SYN39_ConversionOperators_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-40 (自動実装プロパティ { get; set; })")]
    [Trait("FeatureId", "SYN-40")]
    [Trait("Category", "Syntax")]
    public void Test_SYN40_AutoProperties_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-41 (計算式プロパティ =>)")]
    [Trait("FeatureId", "SYN-41")]
    [Trait("Category", "Syntax")]
    public void Test_SYN41_ExpressionBodiedProperties_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-42 (静的抽象メンバ static abstract in interfaces)")]
    [Trait("FeatureId", "SYN-42")]
    [Trait("Category", "Syntax")]
    public void Test_SYN42_StaticAbstractMembers_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-43 (checked / unchecked ブロック・式)")]
    [Trait("FeatureId", "SYN-43")]
    [Trait("Category", "Syntax")]
    public void Test_SYN43_CheckedUnchecked_Transpilation()
    {
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-44")]
    public void Test_SYN44_ExtensionMethods_Transpilation()
    {
        int[] valIn = [2, 4];
        int[] multOut = [0, 0];
        int[] genericOut = [0, 0];

        SYN_39_45_OperatorsAndInterfacesKernelsWrapper.Launch_ExtensionMethodKernel(valIn, multOut, genericOut, blockDimX: 2);

        // tid 0: valIn=2. Modify(5) -> Value=7. MultiplyAndAdd(10, 20) -> 7 * 3 + 30 = 51. GenericIdentity(200) = 200.
        Assert.Equal(51, multOut[0]);
        Assert.Equal(200, genericOut[0]);

        // tid 1: valIn=4. Modify(5) -> Value=9. MultiplyAndAdd(10, 20) -> 9 * 3 + 30 = 57. GenericIdentity(400) = 400.
        Assert.Equal(57, multOut[1]);
        Assert.Equal(400, genericOut[1]);
    }

    [Fact(Skip = "未実装: SYN-45 (Nullable 値型 uint?, Nullable<T>)")]
    [Trait("FeatureId", "SYN-45")]
    [Trait("Category", "Syntax")]
    public void Test_SYN45_NullableValueTypes_Transpilation()
    {
    }
}
