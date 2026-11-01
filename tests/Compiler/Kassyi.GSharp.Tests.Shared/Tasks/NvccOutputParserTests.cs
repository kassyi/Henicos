using System.Reflection;
using Microsoft.Build.Locator;
using Xunit;
using Kassyi.GSharp.Compiler.Tasks;

namespace Kassyi.GSharp.Tests.Tasks;

[Category(TestCategory.Compiler)]
public class NvccOutputParserTests
{
    static NvccOutputParserTests()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            try
            {
                MSBuildLocator.RegisterDefaults();
            }
            catch (InvalidOperationException)
            {
                // ignore
            }
        }
    }

    private static bool DetectRegisterSpill(string output, string error, out string errorMessage)
    {
        var parserType =
            typeof(CompileCudaToFatbinTask).Assembly.GetType("Kassyi.GSharp.Compiler.Tasks.NvccOutputParser");
        var detectMethod = parserType.GetMethod("DetectRegisterSpill", BindingFlags.Public | BindingFlags.Static);

        var args = new object[] { output, error, null, null };
        var result = (bool)detectMethod.Invoke(null, args);
        errorMessage = (string)args[2];
        return result;
    }

    [Fact]
    public void Test_DetectRegisterSpill_NoSpill()
    {
        var output = "some random output\ncompilation success";
        var error = "some ptxas info : 0 bytes spill";

        var hasSpill = DetectRegisterSpill(output, error, out var errorMessage);

        Assert.False(hasSpill);
        Assert.Empty(errorMessage);
    }

    [Fact]
    public void Test_DetectRegisterSpill_HasSpillInError()
    {
        var output = "";
        var error = "ptxas warning : 10 bytes spill\nFunction properties for _Z3foo";

        var hasSpill = DetectRegisterSpill(output, error, out var errorMessage);

        Assert.True(hasSpill);
        Assert.Contains("Register spill detected", errorMessage);
        Assert.Contains("10 bytes spill", errorMessage);
    }

    [Fact]
    public void Test_DetectRegisterSpill_HasSpillInOutput()
    {
        var output = "Function properties for _Z3bar\nptxas info : 4 bytes spill";
        var error = "";

        var hasSpill = DetectRegisterSpill(output, error, out var errorMessage);

        Assert.True(hasSpill);
        Assert.Contains("Register spill detected", errorMessage);
        Assert.Contains("4 bytes spill", errorMessage);
    }

    [Fact]
    public void Test_ExtractWarnings_EdgUnusedVariableAndLabel_ReturnsStructuredWarnings()
    {
        var parserType =
            typeof(CompileCudaToFatbinTask).Assembly.GetType("Kassyi.GSharp.Compiler.Tasks.NvccOutputParser");
        Assert.NotNull(parserType);
        var extractMethod = parserType.GetMethod("ExtractWarnings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(extractMethod);

        var output = """
            C:\temp\kernel.g.cu(102): warning #550-D: variable "dummy" was set but never used
            C:\temp\kernel.g.cu(527): warning #177-D: label "__gsharp_iter_end_1" was declared but never referenced
            """;
        var warnings = ((System.Collections.IEnumerable)extractMethod.Invoke(null, [output, "", null])).Cast<object>().ToList();

        Assert.Equal(2, warnings.Count);
    }

    [Fact]
    public void Test_DetectRegisterSpill_ScopedSuppression_OnlySuppressesMarkedFunction()
    {
        var parserType =
            typeof(CompileCudaToFatbinTask).Assembly.GetType("Kassyi.GSharp.Compiler.Tasks.NvccOutputParser");
        Assert.NotNull(parserType);
        var getSuppressedMethod = parserType.GetMethod("GetSuppressedSpillFunctions", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(getSuppressedMethod);
        var detectMethods = parserType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name == "DetectRegisterSpill" && m.GetParameters().Length == 4)
            .ToArray();
        var detectMethod = Assert.Single(detectMethods);

        var cuContent = """
            // gsharp:suppress GS0001
            extern "C" __global__ void __gsharp_k_SuppressedKernel(int* a) {}
            extern "C" __global__ void __gsharp_k_UnsuppressedKernel(int* b) {}
            """;
        var suppressed = getSuppressedMethod.Invoke(null, [cuContent]);

        var suppressedOnlyOutput = """
            ptxas warning : Registers are spilled to local memory in function '__gsharp_k_SuppressedKernel', 12 bytes spill stores, 12 bytes spill loads
            ptxas info    : Function properties for __gsharp_k_SuppressedKernel
                16 bytes stack frame, 12 bytes spill stores, 12 bytes spill loads
            """;
        var args1 = new object[] { suppressedOnlyOutput, "", null, suppressed };
        Assert.False((bool)detectMethod.Invoke(null, args1));

        var mixedOutput = suppressedOnlyOutput + "\n" + """
            ptxas warning : Registers are spilled to local memory in function '__gsharp_k_UnsuppressedKernel', 8 bytes spill stores, 8 bytes spill loads
            ptxas info    : Function properties for __gsharp_k_UnsuppressedKernel
                8 bytes stack frame, 8 bytes spill stores, 8 bytes spill loads
            """;
        var args2 = new object[] { mixedOutput, "", null, suppressed };
        Assert.True((bool)detectMethod.Invoke(null, args2));
        Assert.Contains("__gsharp_k_UnsuppressedKernel", (string)args2[2]);
        Assert.DoesNotContain("__gsharp_k_SuppressedKernel", (string)args2[2]);
    }
}
