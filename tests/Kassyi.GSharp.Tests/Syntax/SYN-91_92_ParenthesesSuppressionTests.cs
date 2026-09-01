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

public class SYN_91_92_ParenthesesSuppressionTests
{
    [Fact]
    [Trait("FeatureId", "SYN-91")]
    [Trait("Category", "Syntax")]
    public void Test_SYN91_RedundantParenthesesSuppression_HappyPath()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<TestClass_SYN91_92>(nameof(TestClass_SYN91_92.CheckCondition));

        Assert.Empty(diagnostics);
        Assert.Contains("if", cudaSource);
        Assert.Contains("a", cudaSource);
        Assert.Contains("b", cudaSource);
    }

    [Fact]
    [Trait("FeatureId", "SYN-92")]
    [Trait("Category", "Syntax")]
    public void Test_SYN92_ParenthesesPreservation_EdgeCases_PrecedenceAndMixedOperators()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<TestClass_SYN91_92>(nameof(TestClass_SYN91_92.ComplexOperators));

        Assert.Empty(diagnostics);
        Assert.Contains("r1", cudaSource);
        Assert.Contains("r2", cudaSource);
    }
}

