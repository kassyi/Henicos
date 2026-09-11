
namespace Kassyi.GSharp.Tests.Syntax;

public struct DummyStruct_GEN17
{
    public int Field;
}

public class TestClass_GEN17
{
    public static void ModifyRef(ref bool successFlag)
    {
        successFlag = false;
    }

    public static void MemberAccessRef(ref DummyStruct_GEN17 refArg)
    {
        refArg.Field = 5;
    }

    public static void ChainedRefAssignment(ref int refArg, out int a, out int b)
    {
        a = b = refArg = 10;
    }
}

public partial class GEN_17_RefOutDereferenceTests
    : TranspilerTestBase
{
    [GeneratedRegex(@"\(\*successFlag\)\s*=\s*false;")]
    private static partial Regex SuccessFlagRegex();

    [GeneratedRegex(@"\(\*refArg\)\.Field\s*=\s*5;")]
    private static partial Regex RefArgFieldRegex();

    [GeneratedRegex(@"\(\*a\)\s*=\s*\(\*b\)\s*=\s*\(\*refArg\)\s*=\s*10;")]
    private static partial Regex ChainedRefRegex();

    [SkippableFact]
    [Trait("FeatureId", "GEN-17")]
    [Trait("Category", "Syntax")]
    public void Test_GEN17_RefOutDereferencing_HappyPath_ModifyingRefArgument()
    {
        var (cudaSource, diagnostics) =
            RoslynTestUtility.TranspileMethod<TestClass_GEN17>(nameof(TestClass_GEN17.ModifyRef));

        Assert.Empty(diagnostics);
        Assert.Matches(SuccessFlagRegex(), cudaSource);
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-17")]
    [Trait("Category", "Syntax")]
    public void Test_GEN17_RefOutDereferencing_EdgeCase_MemberAccessAndChainedAssignments()
    {
        var (cudaSourceMember, diagnosticsMember) =
            RoslynTestUtility.TranspileMethod<TestClass_GEN17>(nameof(TestClass_GEN17.MemberAccessRef));
        Assert.Empty(diagnosticsMember);
        Assert.Matches(RefArgFieldRegex(), cudaSourceMember);

        var (cudaSourceChained, diagnosticsChained) =
            RoslynTestUtility.TranspileMethod<TestClass_GEN17>(nameof(TestClass_GEN17.ChainedRefAssignment));
        Assert.Empty(diagnosticsChained);
        Assert.Matches(ChainedRefRegex(), cudaSourceChained);
    }
}
