using System.Text.RegularExpressions;

namespace Kassyi.GSharp.Tests.Shared.Generators;

/// <summary>A <c>[GpuOperatorMacro]</c> call has to name the macro target and the caller's context as the C++ declares them.</summary>
[Category(TestCategory.Compiler)]
public class MacroContextNameGeneratorTests
{
    private static string Generate(string dialect)
    {
        var source = RoslynTestUtility.ReadTestCase("Generators/GpuKernel_MacroContextName.cu.cs");
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, dialect);
        return string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(static t => t.GetText().ToString()));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public void Test_MacroContextArgument_IsNotTheLiteralIdentifierCtx(string dialect)
    {
        var text = Generate(dialect);

        Assert.Contains("MacroCtxAdd(", text);
        // The kernel's context parameter is 'bigCtx'; the call must not refer to an undeclared 'ctx'.
        Assert.DoesNotContain("MacroCtxAdd(&ctx", text);
        Assert.Contains("__MacroCtxAdd(&__gsharp_id_0_bigCtx,", text);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public void Test_MacroCall_UsesTheEmittedFunctionName(string dialect)
    {
        var text = Generate(dialect);

        // The target is declared as __gsharp_fn_<namespace>__<type>__MacroCtxAdd; a bare MacroCtxAdd( call has no declaration.
        Assert.DoesNotMatch(new Regex(@"(?<![A-Za-z0-9_])MacroCtxAdd\(&"), text);
    }
}
