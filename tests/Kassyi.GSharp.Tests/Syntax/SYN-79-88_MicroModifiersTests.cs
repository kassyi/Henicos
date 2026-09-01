
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_79_88_MicroModifiersTests
:  TranspilerTestBase{
    [MemberData(nameof(TranspilerTestBase.Dialects))]
    [Theory]
    [Trait("FeatureId", "SYN-79")]
    [Trait("Category", "Syntax")]
    public void Test_SYN79_FileLocalTypes_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-80")]
    [Trait("Category", "Syntax")]
    public void Test_SYN80_ParamsSpan_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-81")]
    [Trait("Category", "Syntax")]
    public void Test_SYN81_RefReadonlyReturns_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-82")]
    [Trait("Category", "Syntax")]
    public void Test_SYN82_VolatileModifier_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-83")]
    [Trait("Category", "Syntax")]
    public void Test_SYN83_SkipLocalsInitAttribute_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-84")]
    [Trait("Category", "Syntax")]
    public void Test_SYN84_ForeachTupleDeconstruction_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-86")]
    [Trait("Category", "Syntax")]
    public void Test_SYN86_FunctionPointerCallingConventions_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-87")]
    [Trait("Category", "Syntax")]
    public void Test_SYN87_LineDirectives_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-88")]
    [Trait("Category", "Syntax")]
    public void Test_SYN88_ImplicitStackallocArray_Transpilation(ITranspilerDialect dialect)
    {
    }
}
