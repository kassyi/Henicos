using System;
using System.IO;
using System.Linq;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.Generator;

/// <summary>
/// The managed code the generator copies out of device code that Stryker.NET mutated (state machines of kernels and of
/// suspending device functions, managed versions, inlined iterators) is the code it emits for the source before the
/// mutation (#1191).
/// </summary>
[Category(TestCategory.Compiler)]
public class MutantSwitchGeneratorTests
{
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("MutantSwitchGeneratorCase__Kernels.DeviceStateMachines.g.cs")]
    [InlineData("MutantSwitchGeneratorCase__Kernels.g.cs")]
    public void Generate_InstrumentedDeviceCode_EmitsTheManagedCodeOfTheOriginal(string hintName)
    {
        var original = Generate("MutantSwitches_Original.cs", hintName);
        var instrumented = Generate("MutantSwitches_Instrumented.cs", hintName);

        Assert.DoesNotContain("MutantControl", instrumented, StringComparison.Ordinal);
        Assert.Equal(original, instrumented);
    }

    private static string Generate(string testCase, string hintName)
    {
        var source = RoslynTestUtility.ReadTestCase(Path.Combine("Generator", testCase));
        var (driver, output, diagnostics) = RoslynTestUtility.RunGenerator(source, "Cpu");

        Assert.Empty(diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
        var result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        return Assert.Single(result.GeneratedSources, s => s.HintName == hintName).SourceText.ToString();
    }
}
