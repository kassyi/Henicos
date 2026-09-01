
namespace Kassyi.GSharp.Tests.Syntax;

public class GSH04_StackAllocFixedArrayTests:  TranspilerTestBase{
    [MemberData(nameof(TranspilerTestBase.Dialects))]
    [Theory]
    [Trait("FeatureId", "SYN-13")]
    [Trait("FeatureId", "GSH-04")]
    [Trait("Category", "Execution")]
    public void Test_SYN13_GSH04_StackAlloc_HappyPath(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-13")]
    [Trait("FeatureId", "GSH-04")]
    [Trait("Category", "Execution")]
    public void Test_SYN13_GSH04_StackAlloc_InTightLoop(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-13")]
    [Trait("FeatureId", "GSH-04")]
    [Trait("Category", "Execution")]
    public void Test_SYN13_GSH04_StackAlloc_AsMethodArgument(ITranspilerDialect dialect)
    {
    }
}
