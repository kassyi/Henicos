#nullable enable
using System;
using System.Text;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>The C++ that the generator writes for <c>nint</c> and <c>nuint</c> in device code.</summary>
public class NativeIntegerEmissionTests
{
    // The six BitOperations nuint overloads are mapped; nuint locals and casts are written with a declared C++ type
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void BitOperationsNuintOverloads_GeneratorEmission_EmitsIntrinsicCallsWithoutUndeclaredSystemUIntPtr()
    {
        var source = RoslynTestUtility.ReadTestCase("Differential/BitOpsNuintProbeKernels.cs");
        var (driver, _, _) = GeneratorTestRunner.RunGenerator(source, "Cpu");
        var sb = new StringBuilder();
        foreach (var tree in driver.GetRunResult().GeneratedTrees)
        {
            sb.Append(tree.GetText());
        }

        var generated = sb.ToString();

        Assert.DoesNotContain("System_UIntPtr", generated, StringComparison.Ordinal);
        Assert.Contains("__gsharp_lzc_64((unsigned long long int)(", generated, StringComparison.Ordinal);
        Assert.Contains("__gsharp_tzc_64((unsigned long long int)(", generated, StringComparison.Ordinal);
        Assert.Contains("__popcll((unsigned long long int)(", generated, StringComparison.Ordinal);
        Assert.Contains("__gsharp_log2_64((unsigned long long int)(", generated, StringComparison.Ordinal);
        Assert.Contains("__gsharp_rotl64((unsigned long long int)(", generated, StringComparison.Ordinal);
        Assert.Contains("__gsharp_rotr64((unsigned long long int)(", generated, StringComparison.Ordinal);
    }
}
