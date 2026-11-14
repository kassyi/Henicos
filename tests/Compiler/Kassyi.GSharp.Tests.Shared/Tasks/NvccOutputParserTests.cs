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

    private static bool HasRegisterSpill(string output, string error, out string errorMessage) =>
        NvccOutputParser.HasRegisterSpill(output, error, out errorMessage);

    [Fact]
    public void HasRegisterSpill_ZeroBytesSpill_ReturnsFalse()
    {
        var output = "some random output\ncompilation success";
        var error = "some ptxas info : 0 bytes spill";

        var hasSpill = HasRegisterSpill(output, error, out var errorMessage);

        Assert.False(hasSpill);
        Assert.Empty(errorMessage);
    }

    [Fact]
    public void HasRegisterSpill_SpillInErrorStream_ReportsTheSpill()
    {
        var output = "";
        var error = "ptxas warning : 10 bytes spill\nFunction properties for _Z3foo";

        var hasSpill = HasRegisterSpill(output, error, out var errorMessage);

        Assert.True(hasSpill);
        Assert.Contains("Register spill detected", errorMessage);
        Assert.Contains("10 bytes spill", errorMessage);
    }

    [Fact]
    public void HasRegisterSpill_SpillInOutputStream_ReportsTheSpill()
    {
        var output = "Function properties for _Z3bar\nptxas info : 4 bytes spill";
        var error = "";

        var hasSpill = HasRegisterSpill(output, error, out var errorMessage);

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
    [Scenario(TestScenario.HappyPath)]
    public void HasRegisterSpill_ListedKernel_SuppressesOnlyThatKernel()
    {
        var cuContent = """
            // gsharp:suppress GS0001 __gsharp_k_SuppressedKernel
            extern "C" __global__ void __gsharp_k_SuppressedKernel(int* a) {}
            extern "C" __global__ void __gsharp_k_UnsuppressedKernel(int* b) {}
            """;
        var suppressed = SpillSuppression.ReadSuppressedFunctions(cuContent);

        var suppressedOnlyOutput = """
            ptxas warning : Registers are spilled to local memory in function '__gsharp_k_SuppressedKernel', 12 bytes spill stores, 12 bytes spill loads
            ptxas info    : Function properties for __gsharp_k_SuppressedKernel
                16 bytes stack frame, 12 bytes spill stores, 12 bytes spill loads
            """;
        Assert.False(NvccOutputParser.HasRegisterSpill(suppressedOnlyOutput, "", out _, suppressed));

        var mixedOutput = suppressedOnlyOutput + "\n" + """
            ptxas warning : Registers are spilled to local memory in function '__gsharp_k_UnsuppressedKernel', 8 bytes spill stores, 8 bytes spill loads
            ptxas info    : Function properties for __gsharp_k_UnsuppressedKernel
                8 bytes stack frame, 8 bytes spill stores, 8 bytes spill loads
            """;
        Assert.True(NvccOutputParser.HasRegisterSpill(mixedOutput, "", out var errorMessage, suppressed));
        Assert.Contains("__gsharp_k_UnsuppressedKernel", errorMessage);
        Assert.DoesNotContain("__gsharp_k_SuppressedKernel", errorMessage);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    // A kernel whose name starts with a listed name is another kernel
    [InlineData("ptxas info    : Function properties for __gsharp_k_FooBar", false)]
    [InlineData("ptxas info    : Function properties for __gsharp_k_Foo", true)]
    [InlineData("ptxas warning : Registers are spilled to local memory in function '__gsharp_k_FooBar', 8 B", false)]
    // A device function is named in its mangled form: the identifier is the <length> characters after _Z<length>
    [InlineData("ptxas info    : Function properties for _Z18__gsharp_fn_Helperi", true)]
    [InlineData("ptxas info    : Function properties for _Z23__gsharp_fn_HelperOtherii", false)]
    [InlineData("ptxas info    : Function properties for _ZL18__gsharp_fn_Helperv", true)]
    // The lambda of a local function is a local entity of the function that encloses it
    [InlineData("ptxas info    : Function properties for _ZZ14__gsharp_k_Foo11GSharpArrayIiEjENKUliE_clEi", true)]
    [InlineData(
        "ptxas info    : Function properties for _ZZZ14__gsharp_k_Foo11GSharpArrayIiEjENKUliE_clEiENKUliE_clEi",
        true
    )]
    [InlineData("ptxas info    : Function properties for _ZZ17__gsharp_k_FooBar11GSharpArrayIiEjENKUliE_clEi", false)]
    [InlineData("ptxas warning : Stack size for entry function '__gsharp_k_Foo' cannot be determined", true)]
    // A line that names no function
    [InlineData("    8 bytes stack frame, 8 bytes spill stores, 8 bytes spill loads", false)]
    public void IsSuppressed_FunctionOfThePtxasLine_ComparesTheWholeName(string line, bool expected)
    {
        var suppressed = SpillSuppression.ReadSuppressedFunctions(
            "// gsharp:suppress GS0001 __gsharp_k_Foo __gsharp_fn_Helper\n");

        Assert.Equal(expected, SpillSuppression.IsSuppressed(line, suppressed));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    // No line, or a line without names (the marker the generator wrote before it listed names), suppresses nothing
    [InlineData("extern \"C\" __global__ void __gsharp_k_Foo(int* a) {}\n")]
    [InlineData("// gsharp:suppress GS0001\nextern \"C\" __global__ void __gsharp_k_Foo(int* a) {}\n")]
    public void ReadSuppressedFunctions_NoNamesListed_SuppressesNothing(string cuContent)
    {
        var suppressed = SpillSuppression.ReadSuppressedFunctions(cuContent);

        Assert.Empty(suppressed);
        Assert.False(
            SpillSuppression.IsSuppressed("ptxas info    : Function properties for __gsharp_k_Foo", suppressed)
        );
    }
}
