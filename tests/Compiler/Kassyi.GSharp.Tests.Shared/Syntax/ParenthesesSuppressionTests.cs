
namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class TestClass_SYN91_92
{
    public static void CheckCondition(int a, int b)
    {
        if (a < b)
        {
        }
    }

    // uint: int + and * are Prelude calls (#722), unsigned arithmetic stays an operator
    public static void ComplexOperators(int a, int b, int c, uint x, uint y, uint z)
    {
        bool r1 = (a & b) == c;
        uint r2 = x * (y + z);
    }
}

public partial class ParenthesesSuppressionTests
    : TranspilerTestBase
{
    [GeneratedRegex(@"if\s*\(\s*a\s*<\s*b\s*\)")]
    private static partial Regex IfConditionRegex();

    [GeneratedRegex(@"bool\s+r1\s*=\s*\(a\s*&\s*b\)\s*==\s*c;")]
    private static partial Regex BoolAssignmentRegex();

    [GeneratedRegex(@"(uint32_t|unsigned int)\s+r2\s*=\s*x\s*\*\s*\(y\s*\+\s*z\);")]
    private static partial Regex IntAssignmentRegex();

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    public void Test_RedundantParenthesesSuppression_HappyPath()
    {
        var (cudaSource, diagnostics) =
            RoslynTestUtility.TranspileMethod<TestClass_SYN91_92>(nameof(TestClass_SYN91_92.CheckCondition));

        Assert.Empty(diagnostics);
        Assert.Matches(IfConditionRegex(), cudaSource.WithCSharpNames());
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    public void Test_ParenthesesPreservation_EdgeCases_PrecedenceAndMixedOperators()
    {
        var (cudaSource, diagnostics) =
            RoslynTestUtility.TranspileMethod<TestClass_SYN91_92>(nameof(TestClass_SYN91_92.ComplexOperators));

        Assert.Empty(diagnostics);
        Assert.Matches(BoolAssignmentRegex(), cudaSource.WithCSharpNames());
        Assert.Matches(IntAssignmentRegex(), cudaSource.WithCSharpNames());
    }
}

