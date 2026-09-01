
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_17_24_TypesAndStructsTests
:  TranspilerTestBase{
    [MemberData(nameof(TranspilerTestBase.Dialects))]
    [Theory]
    [Trait("FeatureId", "SYN-17")]
    [Trait("Category", "Syntax")]
    public void Test_SYN17_TargetTypedNew_Transpilation(ITranspilerDialect dialect)
    {
        // Verified by SYN_17_TargetTypedNewTests
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-18")]
    [Trait("Category", "Syntax")]
    public void Test_SYN18_TupleDeconstruction_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-19")]
    [Trait("Category", "Syntax")]
    public void Test_SYN19_PrimaryConstructor_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-20")]
    [Trait("Category", "Syntax")]
    public void Test_SYN20_RecordStructWithExpression_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-21")]
    [Trait("Category", "Syntax")]
    public void Test_SYN21_StructIndexer_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-22")]
    [Trait("Category", "Syntax")]
    public void Test_SYN22_StructOperatorOverload_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-23")]
    [Trait("Category", "Syntax")]
    public void Test_SYN23_ReadonlyStruct_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-24")]
    [Trait("Category", "Syntax")]
    public void Test_SYN24_EnumBaseTypeAndFlags_Transpilation(ITranspilerDialect dialect)
    {
    }
}
