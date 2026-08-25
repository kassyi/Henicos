using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class Syn17_92_TypesAndStructsTests
{
    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-92")]
    public void TestStructKernel()
    {
        Vector2[] dataArray = new Vector2[1];
        dataArray[0] = new Vector2 { X = 1.5f, Y = 2.0f };

        Syn17_92_TypesAndStructsKernelsWrapper.Launch_StructKernel(dataArray, 1);

        Assert.Equal(3.0f, dataArray[0].X);
        Assert.Equal(3.0f, dataArray[0].Y);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-92")]
    public void TestStruct_MixedPaddingAndAlignment()
    {
        MixedPaddingStruct[] items = new MixedPaddingStruct[2];
        items[0] = new MixedPaddingStruct
        {
            Flag1 = 10,
            ValueD = 4.0,
            ValueI = 50,
            Flag2 = 20
        };
        items[1] = new MixedPaddingStruct
        {
            Flag1 = 250,
            ValueD = 10.0,
            ValueI = 200,
            Flag2 = 100
        };

        Syn17_92_TypesAndStructsKernelsWrapper.Launch_PaddedStructKernel(items, 2, blockDimX: 2);

        // Item 0
        Assert.Equal((byte)11, items[0].Flag1);
        Assert.Equal(6.0, items[0].ValueD);
        Assert.Equal(150, items[0].ValueI);
        Assert.Equal((short)15, items[0].Flag2);

        // Item 1
        Assert.Equal((byte)251, items[1].Flag1);
        Assert.Equal(15.0, items[1].ValueD);
        Assert.Equal(300, items[1].ValueI);
        Assert.Equal((short)95, items[1].Flag2);
    }

    [Fact(Skip = "未実装: SYN-17 (Target-typed new() のネスト完全対応)")]
    [Trait("FeatureId", "SYN-17")]
    [Trait("Category", "Syntax")]
    public void Test_SYN17_TargetTypedNew_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-18 (構造化束縛・タプル宣言分解)")]
    [Trait("FeatureId", "SYN-18")]
    [Trait("Category", "Syntax")]
    public void Test_SYN18_TupleDeconstruction_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-19 (構造体のプライマリコンストラクタ)")]
    [Trait("FeatureId", "SYN-19")]
    [Trait("Category", "Syntax")]
    public void Test_SYN19_PrimaryConstructor_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-20 (record struct と with 式)")]
    [Trait("FeatureId", "SYN-20")]
    [Trait("Category", "Syntax")]
    public void Test_SYN20_RecordStructWithExpression_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-21 (構造体内インデクサ this[int index])")]
    [Trait("FeatureId", "SYN-21")]
    [Trait("Category", "Syntax")]
    public void Test_SYN21_StructIndexer_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-22 (構造体内演算子オーバーロード)")]
    [Trait("FeatureId", "SYN-22")]
    [Trait("Category", "Syntax")]
    public void Test_SYN22_StructOperatorOverload_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-23 (readonly struct / readonly メソッド)")]
    [Trait("FeatureId", "SYN-23")]
    [Trait("Category", "Syntax")]
    public void Test_SYN23_ReadonlyStruct_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-24 (enum の基底型指定とビットフラグ [Flags])")]
    [Trait("FeatureId", "SYN-24")]
    [Trait("Category", "Syntax")]
    public void Test_SYN24_EnumBaseTypeAndFlags_Transpilation()
    {
    }
}

