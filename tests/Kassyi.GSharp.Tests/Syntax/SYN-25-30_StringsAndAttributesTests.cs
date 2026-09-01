
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_25_30_StringsAndAttributesTests
:  TranspilerTestBase{
    [MemberData(nameof(TranspilerTestBase.Dialects))]
    [Theory]
    [Trait("FeatureId", "SYN-25")]
    [Trait("Category", "Syntax")]
    public void Test_SYN25_StringInterpolationPrintf_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-26")]
    [Trait("Category", "Syntax")]
    public void Test_SYN26_Utf8StringLiteral_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-27")]
    [Trait("Category", "Syntax")]
    public void Test_SYN27_NameOfExpression_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-29")]
    [Trait("Category", "Syntax")]
    public void Test_SYN29_LoopUnrollAttribute_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-30")]
    [Trait("Category", "Syntax")]
    public void Test_SYN30_GenericStruct_Transpilation(ITranspilerDialect dialect)
    {
    }
}
