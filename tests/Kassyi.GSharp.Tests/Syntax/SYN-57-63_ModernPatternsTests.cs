
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_57_63_ModernPatternsTests
:  TranspilerTestBase{
    [MemberData(nameof(TranspilerTestBase.Dialects))]
    [Theory]
    [Trait("FeatureId", "SYN-57")]
    [Trait("Category", "Syntax")]
    public void Test_SYN57_ListPatterns_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-58")]
    [Trait("Category", "Syntax")]
    public void Test_SYN58_PropertyPatterns_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-59")]
    [Trait("Category", "Syntax")]
    public void Test_SYN59_PositionalPatterns_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-60")]
    [Trait("Category", "Syntax")]
    public void Test_SYN60_LambdaDefaultParameters_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-61")]
    [Trait("Category", "Syntax")]
    public void Test_SYN61_LambdaExplicitReturnType_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-62")]
    [Trait("Category", "Syntax")]
    public void Test_SYN62_ScopedRef_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-63")]
    [Trait("Category", "Syntax")]
    public void Test_SYN63_UnsafeNullRef_Transpilation(ITranspilerDialect dialect)
    {
    }
}
