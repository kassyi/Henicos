using Xunit;
using Kassyi.GSharp.Tests.Infrastructure;

// The class-level Category covers every test method, [SkippableFact] and [Theory] included
[Category(TestCategory.Compiler)]
public class ClassCategoryOnlyTests
{
    [SkippableFact]
    public void SkippableTestMethod()
    {
    }

    [Theory]
    [InlineData(1)]
    public void TheoryTestMethod(int value)
    {
    }
}
