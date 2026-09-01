using System.Text.RegularExpressions;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Syntax;

public class TestClass_SYN91_92
{
    public static void CheckCondition(int a, int b)
    {
        if (a < b)
        {
        }
    }

    public static void ComplexOperators(int a, int b, int c)
    {
        bool r1 = (a & b) == c;
        int r2 = a * (b + c);
    }
}

public partial class SYN_91_92_ParenthesesSuppressionTests
{
    [GeneratedRegex(@"if\s*\(\s*a\s*<\s*b\s*\)")]
    private static partial Regex IfConditionRegex();

    [GeneratedRegex(@"bool\s+r1\s*=\s*\(a\s*&\s*b\)\s*==\s*c;")]
    private static partial Regex BoolAssignmentRegex();

    [GeneratedRegex(@"int\s+r2\s*=\s*a\s*\*\s*\(b\s*\+\s*c\);")]
    private static partial Regex IntAssignmentRegex();

    [Fact]
    [Trait("FeatureId", "SYN-91")]
    [Trait("Category", "Syntax")]
    public void Test_SYN91_RedundantParenthesesSuppression_HappyPath()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<TestClass_SYN91_92>(nameof(TestClass_SYN91_92.CheckCondition));

        Assert.Empty(diagnostics);
        Assert.Matches(IfConditionRegex(), cudaSource);
    }

    [Fact]
    [Trait("FeatureId", "SYN-92")]
    [Trait("Category", "Syntax")]
    public void Test_SYN92_ParenthesesPreservation_EdgeCases_PrecedenceAndMixedOperators()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<TestClass_SYN91_92>(nameof(TestClass_SYN91_92.ComplexOperators));

        Assert.Empty(diagnostics);
        Assert.Matches(BoolAssignmentRegex(), cudaSource);
        Assert.Matches(IntAssignmentRegex(), cudaSource);
    }
}

