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
    // element copies its array field (GSH0081), as for any such struct; nothing else is added to CS0523. The reason
    // names the cycle, without the advice to reorder the fields, which cannot help

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public async Task Run_CyclicStructWithArrayInsideAnotherAndAsArrayElement_WarnsOnlyForTheArray(string dialect)
    {
        const string Reason =
            "field 'Inner' of type '" + NestedCyc + "': '" + NestedCyc + "' holds itself by value (CS0523)";
        var (errors, reported) = await RunAsync(CyclicStructs + "NestedCyclicStruct.cu.cs", dialect);

        Assert.Equal([("CS0523", 11, 20, 11, 25)], errors);
        Assert.Equal(
            dialect == "Cuda"
                ? [(Analyzer, "GSH0080", 22, 50, 22, 55), (Analyzer, "GSH0081", 22, 50, 22, 55)]
                : [(Analyzer, "GSH0080", 22, 50, 22, 55)],
            reported.Select(static d => (d.Origin, d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn)));
        Assert.EndsWith("layouts differ (" + Reason + ").", reported[0].Message, StringComparison.Ordinal);
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

    // Each walk that searches the fields of a struct searches each struct once, so three or four fields of a struct's
    // own type do not multiply the work
    [Theory(Timeout = 120_000)]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public Task Run_StructsThatHoldThemselvesThreeAndFourTimes_ReportsOnlyCs0523(string dialect) =>
        AssertOnlyCSharpErrorsAsync(
            CyclicStructs + "WideCyclicStructs.cu.cs",
            dialect,
            [("CS0523", 11, 22, 11, 24), ("CS0523", 19, 21, 19, 23)]);

    // Valid C#: arrays of a struct's own type are references, not a cycle, and the searches through array elements
    // also look into each struct once
    [Theory(Timeout = 120_000)]
    [Scenario(TestScenario.Boundary)]
    [InlineData("ArrayTree3.cu.cs", "Cpu")]
    [InlineData("ArrayTree3.cu.cs", "Cuda")]
    [InlineData("ArrayTree4.cu.cs", "Cpu")]
    [InlineData("ArrayTree4.cu.cs", "Cuda")]
    public Task Run_StructWithSeveralArraysOfItself_ReportsNothing(string testCase, string dialect) =>
        AssertOnlyCSharpErrorsAsync(CyclicStructs + testCase, dialect, []);

    // G<int> holds G<G<int>>, which holds G<G<G<int>>> ...: no constructed type repeats, and the walks stop at a
    // second construction of a definition that holds itself by value
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public Task Run_GenericStructThatHoldsALargerConstructionOfItself_ReportsOnlyCs0523(string dialect) =>
        AssertOnlyCSharpErrorsAsync(
            CyclicStructs + "GenericCyclicStruct.cu.cs",
            dialect,
            [("CS0523", 11, 24, 11, 29)]);

    // The arm of the union's own type is the cycle, not an arm whose host and device layouts differ (GSH0078): the
    // parameter and the local get nothing, and the array the GSH0080 of any array of a cyclic struct
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public async Task Run_ExplicitUnionWithAnArmOfItsOwnType_WarnsOnlyForTheArray(string dialect)
    {
        const string Union = "Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs.CyclicExplicitUnion.Union";
        var (errors, reported) = await RunAsync(CyclicStructs + "CyclicExplicitUnion.cu.cs", dialect);

        Assert.Equal([("CS0523", 13, 39, 13, 40)], errors);
        Assert.Equal(
            [(Analyzer, "GSH0080", 18, 49, 18, 56)],
            reported.Select(static d => (d.Origin, d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn)));
        Assert.EndsWith(
            "(field 'A' of type '" + Union + "': '" + Union + "' holds itself by value (CS0523)).",
            reported[0].Message,
            StringComparison.Ordinal);
    }

    // A construction of a generic struct inside another construction of it is no cycle when the definition does not
    // hold itself
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void GetStructLayout_GenericStructInsideItsOwnConstruction_LaysOutEveryField(string dialect)
    {
        var (compilation, _, _) = RoslynTestUtility.GetTestCaseContext(CyclicStructs + "RepeatedStructFields.cs");

        var layout = TypeLayoutCalculator.GetStructLayout(
            GetType(compilation, "TwinHolder"),
            dialect == "Cuda" ? s_cudaDialect : s_cpuDialect);

        Assert.Equal(
            [("Nested", 0, 32)],
            layout.DeviceFields.Select(static f => (f.Member.Name, f.Offset, f.Layout.Size)));
        Assert.Equal(
            [("Nested", 0, 32)],
            layout.Host.Fields.Select(static f => (f.Member.Name, f.Offset, f.Layout.Size)));
        Assert.Equal((32, TransferClass.Identical), (layout.Device.Size, layout.Transfer));
    }

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

        // [WHY] On a thread of its own, so that the timeout of a test can end a walk that does not finish
        return (errors, await Task.Run(() => KernelDiagnostics.RunAsync(source, dialect)));
    }
}
