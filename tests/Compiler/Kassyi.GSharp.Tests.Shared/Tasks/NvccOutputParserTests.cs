using Xunit;
using Kassyi.GSharp.Compiler.Tasks;
using Kassyi.GSharp.Tests.Shared.Tasks;

namespace Kassyi.GSharp.Tests.Tasks;

[Category(TestCategory.Compiler)]
public class NvccOutputParserTests
{
    static NvccOutputParserTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
    }

    private static bool DetectRegisterSpill(string output, string error, out string errorMessage) =>
        NvccOutputParser.DetectRegisterSpill(output, error, out errorMessage);

    [Fact]
    public void DetectRegisterSpill_ZeroBytesSpill_ReturnsFalse()
    {
        var output = "some random output\ncompilation success";
        var error = "some ptxas info : 0 bytes spill";

        var hasSpill = DetectRegisterSpill(output, error, out var errorMessage);

        Assert.False(hasSpill);
        Assert.Empty(errorMessage);
    }

    [Fact]
    public void DetectRegisterSpill_SpillInErrorStream_ReportsTheSpill()
    {
        var output = "";
        var error = "ptxas warning : 10 bytes spill\nFunction properties for _Z3foo";

        var hasSpill = DetectRegisterSpill(output, error, out var errorMessage);

        Assert.True(hasSpill);
        Assert.Contains("Register spill detected", errorMessage);
        Assert.Contains("10 bytes spill", errorMessage);
    }

    [Fact]
    public void DetectRegisterSpill_SpillInOutputStream_ReportsTheSpill()
    {
        var output = "Function properties for _Z3bar\nptxas info : 4 bytes spill";
        var error = "";

        var hasSpill = DetectRegisterSpill(output, error, out var errorMessage);

        Assert.True(hasSpill);
        Assert.Contains("Register spill detected", errorMessage);
        Assert.Contains("4 bytes spill", errorMessage);
    }

    [Fact]
    public void ExtractWarnings_EdgUnusedVariableAndLabel_ReturnsStructuredWarnings()
    {
        var output = """
            C:\temp\kernel.g.cu(102): warning #550-D: variable "dummy" was set but never used
            C:\temp\kernel.g.cu(527): warning #177-D: label "__gsharp_iter_end_1" was declared but never referenced
            """;
        var warnings = NvccOutputParser.ExtractWarnings(output, "");

        Assert.Equal(2, warnings.Count);
        Assert.Equal(@"C:\temp\kernel.g.cu", warnings[0].File);
        Assert.Equal(102, warnings[0].Line);
        Assert.Equal("550-D", warnings[0].Code);
        Assert.Contains("variable \"dummy\" was set but never used", warnings[0].Message);
        Assert.Equal(@"C:\temp\kernel.g.cu", warnings[1].File);
        Assert.Equal(527, warnings[1].Line);
        Assert.Equal("177-D", warnings[1].Code);
        Assert.Contains("label \"__gsharp_iter_end_1\" was declared but never referenced", warnings[1].Message);
    }

    [Fact]
    public void DetectRegisterSpill_ScopedSuppression_OnlySuppressesMarkedFunction()
    {
        var cuContent = """
            // gsharp:suppress GS0001
            extern "C" __global__ void __gsharp_k_SuppressedKernel(int* a) {}
            extern "C" __global__ void __gsharp_k_UnsuppressedKernel(int* b) {}
            """;
        var suppressed = SpillSuppressionParser.GetSuppressedSpillFunctions(cuContent);

        var suppressedOnlyOutput = """
            ptxas warning : Registers are spilled to local memory in function '__gsharp_k_SuppressedKernel', 12 bytes spill stores, 12 bytes spill loads
            ptxas info    : Function properties for __gsharp_k_SuppressedKernel
                16 bytes stack frame, 12 bytes spill stores, 12 bytes spill loads
            """;
        Assert.False(NvccOutputParser.DetectRegisterSpill(suppressedOnlyOutput, "", out _, suppressed));

        var mixedOutput = suppressedOnlyOutput + "\n" + """
            ptxas warning : Registers are spilled to local memory in function '__gsharp_k_UnsuppressedKernel', 8 bytes spill stores, 8 bytes spill loads
            ptxas info    : Function properties for __gsharp_k_UnsuppressedKernel
                8 bytes stack frame, 8 bytes spill stores, 8 bytes spill loads
            """;
        Assert.True(NvccOutputParser.DetectRegisterSpill(mixedOutput, "", out var errorMessage, suppressed));
        Assert.Contains("__gsharp_k_UnsuppressedKernel", errorMessage);
        Assert.DoesNotContain("__gsharp_k_SuppressedKernel", errorMessage);
    }
}
