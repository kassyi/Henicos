#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Layout;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;
using static Kassyi.GSharp.Tests.Shared.Layout.TypeLayoutTestHelper;

namespace Kassyi.GSharp.Tests.Shared.Layout;

/// <summary>
/// A struct that holds itself by value is C# error CS0523, but the IDE runs the analyzers and the generator on that
/// code while it is being typed. Every walk over struct field types stops at a struct already on its path, so the run
/// ends, and G# adds no error of its own to CS0523.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class CyclicStructLayoutTests
{
    private const string CyclicStructs = "Layout/CyclicStructs/";
    private const string NestedCyc = "Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs.NestedCyclicStruct.Cyc";

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public Task Run_CyclicKernelAndDeviceFunctionParameter_ReportsOnlyCs0523(string dialect) =>
        AssertOnlyCSharpErrorsAsync("Analyzers/CyclicStructParameter.cu.cs", dialect, [("CS0523", 10, 20, 10, 25)]);

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public Task Run_CyclicLocalAndReturnType_ReportsOnlyCs0523(string dialect) =>
        AssertOnlyCSharpErrorsAsync(CyclicStructs + "CyclicStructLocal.cu.cs", dialect, [("CS0523", 11, 20, 11, 25)]);

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public Task Run_CyclicDeviceFunctionParameterOnly_ReportsOnlyCs0523(string dialect) =>
        AssertOnlyCSharpErrorsAsync(
            CyclicStructs + "CyclicStructDeviceFunction.cu.cs",
            dialect,
            [("CS0523", 11, 20, 11, 25)]);

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public Task Run_TwoStructsThatHoldEachOther_ReportsOnlyCs0523(string dialect) =>
        AssertOnlyCSharpErrorsAsync(
            CyclicStructs + "MutualCyclicStructs.cu.cs",
            dialect,
            [("CS0523", 11, 23, 11, 28), ("CS0523", 17, 22, 17, 27)]);

    // An array of structs whose host layout is not known is converted element by element (GSH0080), and on CUDA each
    // element copies its array field (GSH0081), as for any such struct; nothing else is added to CS0523
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public async Task Run_CyclicStructWithArrayInsideAnotherAndAsArrayElement_WarnsOnlyForTheArray(string dialect)
    {
        const string Reason = "field 'Inner' of type '" + NestedCyc + "': '" + NestedCyc + "' holds itself";
        var (errors, reported) = await RunAsync(CyclicStructs + "NestedCyclicStruct.cu.cs", dialect);

        Assert.Equal([("CS0523", 11, 20, 11, 25)], errors);
        Assert.Equal(
            dialect == "Cuda"
                ? [(Analyzer, "GSH0080", 22, 50, 22, 55), (Analyzer, "GSH0081", 22, 50, 22, 55)]
                : [(Analyzer, "GSH0080", 22, 50, 22, 55)],
            reported.Select(static d => (d.Origin, d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn)));
        Assert.Contains("(" + Reason + ")", reported[0].Message, StringComparison.Ordinal);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public Task Run_StructThatHoldsANullableOfItself_ReportsOnlyCs0523(string dialect) =>
        AssertOnlyCSharpErrorsAsync(
            CyclicStructs + "NullableCyclicStruct.cu.cs",
            dialect,
            [("CS0523", 11, 22, 11, 27)]);

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public Task Run_StructThatHoldsItselfTwice_ReportsOnlyCs0523(string dialect) =>
        AssertOnlyCSharpErrorsAsync(
            CyclicStructs + "BranchingCyclicStruct.cu.cs",
            dialect,
            [("CS0523", 11, 21, 11, 25)]);

    // The path holds only the structs being entered: a struct that holds the same struct twice, also inside another
    // struct, is laid out in full on the device and the host
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void GetStructLayout_SameStructInTwoFields_LaysOutBothFields(string dialect)
    {
        var (compilation, _, _) = RoslynTestUtility.GetTestCaseContext(CyclicStructs + "RepeatedStructFields.cs");
        var diamond = GetType(compilation, "Diamond");

        var layout = TypeLayoutCalculator.GetStructLayout(diamond, dialect == "Cuda" ? s_cudaDialect : s_cpuDialect);

        Assert.Equal(
            [("Left", 0, 16), ("Right", 16, 16), ("Wrapped", 32, 24)],
            layout.DeviceFields.Select(static f => (f.Member.Name, f.Offset, f.Layout.Size)));
        Assert.Equal(
            [("Left", 0, 16), ("Right", 16, 16), ("Wrapped", 32, 24)],
            layout.Host.Fields.Select(static f => (f.Member.Name, f.Offset, f.Layout.Size)));
        Assert.Equal((56, TransferClass.Identical), (layout.Device.Size, layout.Transfer));
    }

    private static INamedTypeSymbol GetType(Compilation compilation, string name) =>
        Assert.IsAssignableFrom<INamedTypeSymbol>(
            compilation.GetTypeByMetadataName($"Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs.{name}"));

    // The C# errors of the test case are exactly expectedErrors, and the analyzers and the generator report nothing
    private static async Task AssertOnlyCSharpErrorsAsync(string testCase, string dialect,
        (string Id, int StartLine, int StartColumn, int EndLine, int EndColumn)[] expectedErrors)
    {
        var (errors, reported) = await RunAsync(testCase, dialect);

        Assert.Equal(expectedErrors, errors);
        Assert.Empty(reported);
    }

    // The C# errors of the test case (ID and range), and what the analyzers and the generator report
    private static async Task<(List<(string, int, int, int, int)> Errors, ImmutableArray<ReportedDiagnostic> Reported)>
        RunAsync(string testCase, string dialect)
    {
        var source = RoslynTestUtility.ReadTestCase(testCase);
        var errors = RoslynTestUtility.CreateCompilation(source)
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Generator, d))
            .Select(static d => (d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn))
            .ToList();

        return (errors, await KernelDiagnostics.RunAsync(source, dialect));
    }
}
