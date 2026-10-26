using System.Text.RegularExpressions;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// The device definition of an explicit layout struct puts the largest member of each union group first: C++ aggregate
/// initialization (<c>S{}</c>) initializes only the first member of an anonymous union.
/// </summary>
[Category(TestCategory.Compiler)]
public class ExplicitUnionEmissionTests
{
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void Test_UnionGroup_EmitsTheLargestMemberFirst(string dialect)
    {
        var source = RoslynTestUtility.ReadTestCase("Layout/ExplicitUnionOrder.cs");

        var (_, output, _) = RoslynTestUtility.RunGenerator(source, dialect);

        var generated = string.Concat(output.SyntaxTrees.Select(static tree => tree.ToString()));
        Assert.Matches(
            new Regex(@"union\{\r?\nlong long __gsharp_mb_L;\r?\nint __gsharp_mb_I;\r?\n\}"),
            generated);
    }
}
