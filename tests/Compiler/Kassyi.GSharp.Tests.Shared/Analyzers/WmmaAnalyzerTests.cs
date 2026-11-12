using Kassyi.GSharp.Compiler.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class WmmaAnalyzerTests : TranspilerTestBase
{
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Wmma_UnsupportedFragmentsLocationsAndOperands_ReportsGSH0076()
    {
        var diagnostics = (await GeneratorTestRunner
            .TestAnalyzer("Analyzers/Wmma_SadPath.cs", s_analyzer)
            .GetDiagnosticsAsync())
            .Where(static d => d.Id == GSharpDiagnostics.UnsupportedWarpMatrixOperation.Id)
            .OrderBy(static d => d.Location.SourceSpan.Start)
            .ToArray();

        // ValidWmmaKernel produces 0 GSH0076 diagnostics;
        // InvalidFragmentAsKernelParam (1) + InvalidWmmaOperationsKernel (8) = 9 diagnostics
        Assert.Equal(9, diagnostics.Length);
        Assert.All(diagnostics, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));

        // 0: Kernel parameter WmmaAccumulator<WmmaM16N16K16, int> acc (line 38, 0-based 37)
        var span0 = diagnostics[0].Location.GetLineSpan();
        Assert.Equal((37, 52), (span0.StartLinePosition.Line, span0.StartLinePosition.Character));
        Assert.Equal((37, 87), (span0.EndLinePosition.Line, span0.EndLinePosition.Character));

        // 1: WmmaMatrixA<WmmaM8N8K4, sbyte, WmmaRowMajor> badMatA (line 46, 0-based 45)
        var span1 = diagnostics[1].Location.GetLineSpan();
        Assert.Equal((45, 8), (span1.StartLinePosition.Line, span1.StartLinePosition.Character));
        Assert.Equal((45, 52), (span1.EndLinePosition.Line, span1.EndLinePosition.Character));

        // 2: WmmaAccumulator<WmmaM16N16K16, double> badAcc (line 47, 0-based 46)
        var span2 = diagnostics[2].Location.GetLineSpan();
        Assert.Equal((46, 8), (span2.StartLinePosition.Line, span2.StartLinePosition.Character));
        Assert.Equal((46, 46), (span2.EndLinePosition.Line, span2.EndLinePosition.Character));

        // 3: Wmma.MultiplyAccumulate(ref badAcc, in fragA, in fragB, in badAcc) (line 51, 0-based 50)
        var span3 = diagnostics[3].Location.GetLineSpan();
        Assert.Equal((50, 8), (span3.StartLinePosition.Line, span3.StartLinePosition.Character));
        Assert.Equal((50, 74), (span3.EndLinePosition.Line, span3.EndLinePosition.Character));

        // 4: StructWithWmmaFragment badStruct (line 53, 0-based 52)
        var span4 = diagnostics[4].Location.GetLineSpan();
        Assert.Equal((52, 8), (span4.StartLinePosition.Line, span4.StartLinePosition.Character));
        Assert.Equal((52, 30), (span4.EndLinePosition.Line, span4.EndLinePosition.Character));

        // 5: WmmaAccumulator<WmmaM16N16K16, int>[]? badArray (line 54, 0-based 53)
        var span5 = diagnostics[5].Location.GetLineSpan();
        Assert.Equal((53, 8), (span5.StartLinePosition.Line, span5.StartLinePosition.Character));
        Assert.Equal((53, 46), (span5.EndLinePosition.Line, span5.EndLinePosition.Character));

        // 6: WmmaM16N16K16 badMarker (line 55, 0-based 54)
        var span6 = diagnostics[6].Location.GetLineSpan();
        Assert.Equal((54, 8), (span6.StartLinePosition.Line, span6.StartLinePosition.Character));
        Assert.Equal((54, 21), (span6.EndLinePosition.Line, span6.EndLinePosition.Character));

        // 7: Wmma.Load(ref fragA, in localScalar, 16) -> in localScalar (line 58, 0-based 57)
        var span7 = diagnostics[7].Location.GetLineSpan();
        Assert.Equal((57, 32), (span7.StartLinePosition.Line, span7.StartLinePosition.Character));
        Assert.Equal((57, 43), (span7.EndLinePosition.Line, span7.EndLinePosition.Character));

        // 8: Wmma.Load(ref fragA, (sbyte)(a[0] + 1), 16) -> (sbyte)(a[0] + 1) (line 59, 0-based 58)
        var span8 = diagnostics[8].Location.GetLineSpan();
        Assert.Equal((58, 29), (span8.StartLinePosition.Line, span8.StartLinePosition.Character));
        Assert.Equal((58, 46), (span8.EndLinePosition.Line, span8.EndLinePosition.Character));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Wmma_UserAndCoreMarkerLocals_ReportsGSH0076OnlyForTheCoreMarkers()
    {
        var diagnostics = (await GeneratorTestRunner
            .TestAnalyzer("Analyzers/Wmma_MarkerKinds.cs", s_analyzer)
            .GetDiagnosticsAsync())
            .Where(static d => d.Severity >= DiagnosticSeverity.Warning)
            .OrderBy(static d => d.Location.SourceSpan.Start)
            .ToArray();

        // The locals of UserWmmaShape and UserWmmaLayout (lines 22-23) are ordinary structs; only the Core markers are
        // reported
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, static d => Assert.Equal(GSharpDiagnostics.UnsupportedWarpMatrixOperation.Id, d.Id));
        Assert.All(diagnostics, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));

        // 0: WmmaM16N16K16 coreShape (line 28, 0-based 27)
        var span0 = diagnostics[0].Location.GetLineSpan();
        Assert.Equal((27, 8), (span0.StartLinePosition.Line, span0.StartLinePosition.Character));
        Assert.Equal((27, 21), (span0.EndLinePosition.Line, span0.EndLinePosition.Character));

        // 1: WmmaRowMajor coreLayout (line 29, 0-based 28)
        var span1 = diagnostics[1].Location.GetLineSpan();
        Assert.Equal((28, 8), (span1.StartLinePosition.Line, span1.StartLinePosition.Character));
        Assert.Equal((28, 20), (span1.EndLinePosition.Line, span1.EndLinePosition.Character));
    }

    // A struct of the user that implements IWmmaShape or IWmmaLayout compiles as any other struct: no diagnostic, and
    // the struct is emitted. GeneratorSnapshotTests compares the generated code of each case with its snapshots
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("UserMarkerLocal.cu.cs", "LocalShapeTag")]
    [InlineData("UserMarkerExpression.cu.cs", "ExpressionLayoutTag")]
    [InlineData("UserMarkerDefault.cu.cs", "DefaultShapeTag")]
    [InlineData("UserMarkerField.cu.cs", "FieldLayoutTag")]
    [InlineData("UserMarkerParameter.cu.cs", "ParameterMarkerTag")]
    public async Task Wmma_UserStructImplementingAMarkerInterface_CompilesAsAnOrdinaryStruct(
        string fileName,
        string structName)
    {
        var relativePath = "Analyzers/WmmaUserMarker/" + fileName;

        var analyzerDiagnostics = await TestAnalyzer(relativePath, s_analyzer).GetDiagnosticsAsync();
        Assert.DoesNotContain(analyzerDiagnostics, static d => d.Severity >= DiagnosticSeverity.Warning);

        var driver = await TestGenerator(relativePath).GetDriverAsync();
        var runResult = driver.GetRunResult();
        Assert.DoesNotContain(runResult.Diagnostics, static d => d.Severity >= DiagnosticSeverity.Warning);

        // The definition line of the struct: struct __gsharp_ty_<namespace>__<name>
        var lines = runResult.GeneratedTrees
            .SelectMany(static t => t.GetText().Lines)
            .Select(static line => line.ToString());
        Assert.Contains(
            lines,
            line => line.StartsWith("struct __gsharp_ty_", StringComparison.Ordinal) &&
                line.EndsWith("__" + structName, StringComparison.Ordinal)
        );
    }
}
