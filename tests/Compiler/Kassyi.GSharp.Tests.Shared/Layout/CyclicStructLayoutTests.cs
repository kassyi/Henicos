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

    // A<T> holds B<A<A<T>>>, whose field is A<A<T>>: the construction grows through the type argument of B
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public Task Run_GenericStructThatGrowsThroughATypeArgument_ReportsOnlyCs0523(string dialect) =>
        AssertOnlyCSharpErrorsAsync(
            CyclicStructs + "TypeArgumentCycle.cu.cs",
            dialect,
            [("CS0523", 16, 27, 16, 32)]);

    // A<T> holds A<A<T>>?: the construction grows through Nullable<T>
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public Task Run_GenericStructThatGrowsThroughANullable_ReportsOnlyCs0523(string dialect) =>
        AssertOnlyCSharpErrorsAsync(
            CyclicStructs + "NullableArgumentCycle.cu.cs",
            dialect,
            [("CS0523", 11, 25, 11, 30)]);

    // Valid C#: N<int> holds arrays of N<int[]>, N<int[]> arrays of N<int[][]>, ...: the searches through array
    // elements stop at the second construction of N, and the array gets what any array of such a struct gets
    [Theory(Timeout = 120_000)]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public async Task Run_GenericStructThatGrowsThroughArrays_WarnsOnlyForTheArray(string dialect)
    {
        var (errors, reported) = await RunAsync(CyclicStructs + "ArrayGrowthGeneric.cu.cs", dialect);

        Assert.Empty(errors);
        Assert.Equal(
            dialect == "Cuda"
                ? [(Analyzer, "GSH0080", 16, 51, 16, 59), (Analyzer, "GSH0081", 16, 51, 16, 59)]
                : [(Analyzer, "GSH0080", 16, 51, 16, 59)],
            reported.Select(static d => (d.Origin, d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn)));
    }

    // Valid C#: a struct first met deep in one field and again near the root in another is searched in full; main
    // reported these before the searches marked structs as searched, and still does
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public async Task Run_MultidimensionalArrayFirstMetDeep_ReportsGsh0003(string dialect)
    {
        var (errors, reported) = await RunAsync(CyclicStructs + "DeepChainMultidimensional.cu.cs", dialect);

        Assert.Empty(errors);
        Assert.Equal(
            [(Analyzer, "GSH0003", 113, 36, 113, 40)],
            reported.Select(static d => (d.Origin, d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn)));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public async Task Run_ReservedNameFirstMetDeep_ReportsGsh0055(string dialect)
    {
        var (errors, reported) = await RunAsync(CyclicStructs + "DeepChainReservedName.cu.cs", dialect);

        Assert.Empty(errors);
        Assert.Equal(
            [(Analyzer, "GSH0055", 71, 41, 71, 45)],
            reported.Select(static d => (d.Origin, d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn)));
        Assert.Contains(".Inner.__bad'", reported[0].Message, StringComparison.Ordinal);
    }

    // Valid C#: another construction of a generic struct that does not grow (G<M> after G<int>) is searched like any
    // struct, so what it holds is reported as on main, with the name collision of the two constructions (GSH0058)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("SecondConstructionMultidimensional.cu.cs", "GSH0003", 21, 49, 21, 55, "Cpu")]
    [InlineData("SecondConstructionMultidimensional.cu.cs", "GSH0003", 21, 49, 21, 55, "Cuda")]
    [InlineData("SecondConstructionReservedName.cu.cs", "GSH0055", 21, 56, 21, 61, "Cpu")]
    [InlineData("SecondConstructionReservedName.cu.cs", "GSH0055", 21, 56, 21, 61, "Cuda")]
    [InlineData("SecondConstructionInaccessible.cu.cs", "GSH0108", 17, 49, 17, 55, "Cpu")]
    [InlineData("SecondConstructionInaccessible.cu.cs", "GSH0108", 17, 49, 17, 55, "Cuda")]
    public async Task Run_SecondConstructionThatDoesNotGrow_ReportsWhatItHolds(string testCase, string id,
        int startLine, int startColumn, int endLine, int endColumn, string dialect)
    {
        var (errors, reported) = await RunAsync(CyclicStructs + testCase, dialect);
        var structLine = testCase == "SecondConstructionInaccessible.cu.cs" ? 10 : 14;

        Assert.Empty(errors);
        Assert.Equal(
            [
                (Analyzer, "GSH0058", structLine, 19, structLine, 20),
                (Analyzer, id, startLine, startColumn, endLine, endColumn)
            ],
            reported.Select(static d => (d.Origin, d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn)));
    }

    // Valid C#: V<T> names V<T> in a property and Tr<T> in an array, not by value, so V<V<int>> and Tr<Tr<int>> are no
    // cycle and keep their full layouts, and an array of V<V<int>> as a kernel parameter gets nothing
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void GetStructLayout_ConstructionInsideItsOwnConstruction_LaysOutEveryField(string dialect)
    {
        var compilation = RoslynTestUtility
            .GetTestCaseContext(CyclicStructs + "NestedGenericConstruction.cu.cs")
            .Compilation;
        var vector = GetType(compilation, "NestedGenericConstruction+V`1");
        var tree = GetType(compilation, "NestedGenericConstruction+Tr`1");
        var int32 = compilation.GetSpecialType(SpecialType.System_Int32);
        var target = dialect == "Cuda" ? s_cudaDialect : s_cpuDialect;

        var vectors = TypeLayoutCalculator.GetStructLayout(vector.Construct(vector.Construct(int32)), target);
        var trees = TypeLayoutCalculator.GetStructLayout(tree.Construct(tree.Construct(int32)), target);

        Assert.Equal(
            (16, 4, TransferClass.Identical),
            (vectors.Device.Size, vectors.Device.Alignment, vectors.Transfer));
        Assert.Equal(
            [("X", 0, 8), ("Y", 8, 8)],
            vectors.Host.Fields.Select(static f => (f.Member.Name, f.Offset, f.Layout.Size)));
        Assert.Equal((40, 8), (trees.Device.Size, trees.Device.Alignment));
        Assert.Equal(
            [("Kids", 0, 16), ("Value", 16, 24)],
            trees.DeviceFields.Select(static f => (f.Member.Name, f.Offset, f.Layout.Size)));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public Task Run_ConstructionInsideItsOwnConstruction_ReportsNothing(string dialect) =>
        AssertOnlyCSharpErrorsAsync(CyclicStructs + "NestedGenericConstruction.cu.cs", dialect, []);

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
