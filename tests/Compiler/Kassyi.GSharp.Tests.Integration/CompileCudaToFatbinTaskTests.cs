using Kassyi.GSharp.Compiler.Tasks;

namespace Kassyi.GSharp.Tests.Integration;

[Category(TestCategory.Compiler)]
public class CompileCudaToFatbinTaskTests
{
    [Theory]
    [InlineData("ptxas info : 0 bytes gmem\nptxas info : Compiling entry function\nptxas info : 0 bytes spill stores, 0 bytes spill loads\nptxas info : Used 4 registers", "", false)]
    [InlineData("ptxas info : 12 bytes spill stores, 12 bytes spill loads\n", "", true)]
    [InlineData("", "ptxas warning : ... spills ...", true)]
    [InlineData("ptxas info : 0 bytes spill stores, 0 bytes spill loads", "ptxas warning : ... spills ...", true)]
    [InlineData("ptxas info : 4 bytes spill stores, 0 bytes spill loads", "", true)]
    [InlineData("ptxas info : 0 bytes spill stores, 8 bytes spill loads", "", true)]
    [InlineData("ptxas info : 1 bytes spill stores, 0 bytes spill loads", "", true)]
    [InlineData("ptxas info : 0 bytes spill stores, 1 bytes spill loads", "", true)]
    [InlineData("Some random output that doesn't mention spills", "Some random error", false)]
    public void Test_DetectRegisterSpill_ShouldCorrectlyDetectSpills(string output, string error, bool expectedHasSpill)
    {
        // Act
        var result = NvccOutputParser.DetectRegisterSpill(output, error, out var errorMessage);

        // Assert
        Assert.Equal(expectedHasSpill, result);

        if (expectedHasSpill)
        {
            Assert.Contains("Register spill detected", errorMessage);
        }
        else
        {
            Assert.Empty(errorMessage);
        }
    }
}
