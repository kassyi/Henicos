#nullable enable
using System.Collections.Immutable;
using Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.ReadOnlyArrays;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// <c>[GpuReadOnly]</c> promises that no thread writes the array while the kernel runs, through any alias. The analyzer
/// reports, at the write (GSH0118), each write it can see: of the array itself, of a span or array converted from it,
/// of a struct field or a <c>ref</c> foreach variable that holds it, and through a method that writes its operand or its
/// parameter. The reads of the same arrays are not reported. Each form has a kernel of its own in the test case, and a
/// range is asserted relative to the line that declares the kernel, so the kernels can move.
/// </summary>
[Category(TestCategory.Compiler)]
public class ReadOnlyArrayWriteDiagnosticsTests
{
    private const string TestCase = "Diagnostics/ReadOnlyArrayWrites.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ElementWrites_ParameterAndField_AreReportedAtTheElement(string dialect)
    {
        // The assignment, the field and the compound assignment; ++ and -- are next; h.Free and the reads are not
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 2, 9, 2, 13), (Analyzer, 3, 9, 3, 19), (Analyzer, 6, 9, 6, 13)],
            InKernel(diagnostics, "DirectKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
        Assert.Equal(
            [(Analyzer, 4, 9, 4, 13), (Analyzer, 5, 11, 5, 15)],
            InKernel(diagnostics, "DirectKernel", ReadOnlyArrayWriteAnalysis.ElementUpdate));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ByReference_ReadOnlyElement_IsReportedAtTheElement(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // Increment(ref o[0]) on the next line is not reported
        Assert.Equal(
            [(Analyzer, 2, 23, 2, 27)],
            InKernel(diagnostics, "ByReferenceKernel", ReadOnlyArrayWriteAnalysis.ByReference));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ConvertedArrayAndSpans_WriteThroughTheAlias_AreReportedAtTheElement(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // x = h.Fixed, view = r.AsSpan() and sliced = Gpu.NotNull(r).AsSpan().Slice(1); own = o is not read-only
        Assert.Equal(
            [(Analyzer, 3, 9, 3, 13), (Analyzer, 5, 9, 5, 16), (Analyzer, 7, 9, 7, 18)],
            InKernel(diagnostics, "ConvertedKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task WritingMethods_ReadOnlyOperand_AreReportedAtTheCall(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // Fill, Array.Fill, Array.Copy and CopyTo into it, and Array.Reverse; CopyTo out of it, Array.Fill(o) are not
        Assert.Equal(
            [
                (Analyzer, 2, 9, 2, 28),
                (Analyzer, 3, 9, 3, 26),
                (Analyzer, 4, 9, 4, 28),
                (Analyzer, 5, 9, 5, 38),
                (Analyzer, 7, 9, 7, 25)
            ],
            InKernel(diagnostics, "MethodKernel", ReadOnlyArrayWriteAnalysis.WritingMethod));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task DeviceFunctionArgument_CalleeWritesTheParameter_IsReportedAtTheArgument(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // WriteFirst(o) and ReadFirst(...) are not reported
        Assert.Equal(
            [(Analyzer, 2, 20, 2, 21), (Analyzer, 3, 20, 3, 27)],
            InKernel(diagnostics, "DeviceFunctionKernel", ReadOnlyArrayWriteAnalysis.PassedToWriter));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task StructField_HoldsTheArray_WriteThroughTheFieldIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // w.Data = r and new S { Data = r }; the struct that holds o is not read-only
        Assert.Equal(
            [(Analyzer, 4, 9, 4, 25), (Analyzer, 6, 9, 6, 28)],
            InKernel(diagnostics, "StructAliasKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task RefForEach_ReadOnlySpan_IsReportedAtTheVariableType(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // The loops over o and the ref readonly loop over r are not reported
        Assert.Equal(
            [(Analyzer, 2, 18, 2, 26)],
            InKernel(diagnostics, "RefForEachKernel", ReadOnlyArrayWriteAnalysis.RefVariable));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task CalleeWritingAnotherReadOnlyParameter_ArgumentIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // WriteSecond(r, o) writes only its second parameter, which is [GpuReadOnly] and reported where it is written
        Assert.Empty(InKernel(diagnostics, "SecondParameterKernel"));
        Assert.Equal(
            [(Analyzer, 2, 9, 2, 18)],
            InKernel(diagnostics, "WriteSecond", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task ReassignedParameter_GivenAnotherArray_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "ReassignedParameterKernel"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task EveryWrite_TestCase_IsReportedOnceAndNothingElse(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(20, diagnostics.Ranges(GSharpDiagnostics.ReadOnlyArrayWritten.Id).Count);
    }

    // The ranges of GSH0118 inside the method, with the lines counted from the line that declares it
    private static IReadOnlyList<(DiagnosticOrigin Origin, int StartLine, int StartColumn, int EndLine, int EndColumn)>
        InKernel(ImmutableArray<ReportedDiagnostic> diagnostics, string method, string? reason = null)
    {
        var declaration = CSharpSyntaxTree.ParseText(RoslynTestUtility.ReadTestCase(TestCase)).GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == method);
        var first = declaration.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var last = declaration.GetLocation().GetLineSpan().EndLinePosition.Line + 1;

        return diagnostics.Ranges(GSharpDiagnostics.ReadOnlyArrayWritten.Id, reason)
            .Where(r => r.StartLine >= first && r.StartLine <= last)
            .Select(r => (r.Origin, r.StartLine - first, r.StartColumn, r.EndLine - first, r.EndColumn))
            .ToList();
    }
}
