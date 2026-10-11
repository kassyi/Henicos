using System.Reflection;
using Microsoft.Build.Locator;
using Xunit;
using Kassyi.GSharp.Compiler.Tasks;

namespace Kassyi.GSharp.Tests.Tasks;

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
            catch
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

        var args = new object[] { output, error, null };
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
}
