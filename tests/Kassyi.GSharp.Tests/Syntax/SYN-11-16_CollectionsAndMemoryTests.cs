
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_11_16_CollectionsAndMemoryTests
:  TranspilerTestBase{
    [MemberData(nameof(TranspilerTestBase.Dialects))]
    [Theory]
    [Trait("FeatureId", "SYN-11")]
    [Trait("Category", "Syntax")]
    public void Test_SYN11_IndexAndRange_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-12")]
    [Trait("Category", "Syntax")]
    public void Test_SYN12_CollectionExpression_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-13")]
    [Trait("Category", "Syntax")]
    public void Test_SYN13_Stackalloc_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-15")]
    [Trait("Category", "Syntax")]
    public void Test_SYN15_SpanUsage_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-16")]
    [Trait("Category", "Syntax")]
    public void Test_SYN16_MultidimensionalIndexer_Transpilation(ITranspilerDialect dialect)
    {
    }
}
