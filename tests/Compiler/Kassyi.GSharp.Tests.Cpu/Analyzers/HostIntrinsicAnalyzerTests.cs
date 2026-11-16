using System.Collections.Immutable;
using Kassyi.GSharp.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class HostIntrinsicAnalyzerTests
{
    private const string CasesDirectory = "Analyzers/HostIntrinsic/";

    private static readonly HostIntrinsicAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task GpuOnlyMembers_ReadCalledOrNamedInHostCode_ReportGSH0018AtEachReference()
    {
        var actual = await AnalyzeAsync(("Host.cs", ReadCase("GpuOnlyHostReferences_SadPath.cs")));

        Assert.Equal(
            [
                // A field initializer
                "(9,49)-(9,62) ThreadIndex.X",
                // The four coordinate types read as properties (#1731)
                "(11,40)-(11,53) ThreadIndex.X",
                "(11,56)-(11,68) BlockIndex.Y",
                "(11,71)-(11,87) BlockDimension.X",
                "(11,90)-(11,105) GridDimension.Z",
                // Calls: a [CudaIntrinsic] method, a generated stub and two Gpu macros
                "(15,9)-(15,26) Gpu.SyncThreads",
                "(16,20)-(16,32) Gpu.LaneId",
                "(17,24)-(17,41) Gpu.Constant",
                "(18,9)-(18,45) Gpu.ForUnroll",
                // Method groups: two delegates and a function pointer
                "(23,23)-(23,38) Gpu.SyncThreads",
                "(24,26)-(24,36) Gpu.LaneId",
                "(25,36)-(25,51) Gpu.SyncThreads",
                // The indexer of SharedMemory<T> (not its constructor) and a property of Dim3
                "(31,9)-(31,18) SharedMemory.this[]",
                "(31,21)-(31,27) Dim3.X",
                // A lambda and a local function of a host method are host code
                "(36,32)-(36,44) BlockIndex.X",
                "(37,24)-(37,39) GridDimension.X"
            ],
            actual
        );
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task GpuOnlyProperty_ReadInHostCode_ReportsTheMessageWithTheMember()
    {
        var diagnostics = await GetDiagnosticsAsync(("Host.cs", ReadCase("GpuOnlyHostReferences_SadPath.cs")));

        var first = diagnostics[0];
        Assert.Equal(GSharpDiagnostics.HostIntrinsicCallError.Id, first.Id);
        Assert.Equal(DiagnosticSeverity.Error, first.Severity);
        Assert.Equal("The GPU intrinsic 'ThreadIndex.X' cannot be called from a host method", first.GetMessage());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task GpuOnlyMembers_InDeviceCodeOrNotRunOnTheHost_ReportNothing()
    {
        var actual = await AnalyzeAsync(("Host.cs", ReadCase("GpuOnlyHostReferences_HappyPath.cs")));

        Assert.Empty(actual);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Host.g.cs")]
    [InlineData("Other.Generators/Other.Generators.HostGenerator/Host.g.cs")]
    [InlineData("Kassyi.GSharp.Compiler/Host.g.cs")]
    [InlineData("src/Kassyi.GSharp.Compiler/Kassyi.GSharp.Compiler.Helpers/Host.cs")]
    public async Task GeneratedHostRead_OutsideTheOutputOfGSharp_ReportsGSH0018(string path)
    {
        var actual = await AnalyzeAsync((path, ReadCase("GeneratedHostRead.cs")));

        Assert.Equal(["(9,33)-(9,46) ThreadIndex.X"], actual);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Kassyi.GSharp.Compiler/Kassyi.GSharp.Compiler.GpuKernelGenerator/Host.g.cs")]
    [InlineData(@"C:\build\obj\Kassyi.GSharp.Compiler\Kassyi.GSharp.Compiler.GpuKernelGenerator\Host.g.cs")]
    [InlineData("/build/obj/Kassyi.GSharp.Compiler/Kassyi.GSharp.Compiler.NativeApi.NativeApiGenerator/Host.g.cs")]
    public async Task GeneratedHostRead_InTheOutputOfGSharp_ReportsNothing(string path)
    {
        var actual = await AnalyzeAsync((path, ReadCase("GeneratedHostRead.cs")));

        Assert.Empty(actual);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task ManagedKernelBody_GeneratedByGSharp_ReportsNothing()
    {
        var compilation = RoslynTestUtility.CreateCompilationAt(
            ("ManagedKernelBody.cs", ReadCase("ManagedKernelBody.cs"))
        );
        var (_, output, _) = GeneratorTestRunner.RunGenerator(compilation, "Cpu");

        // The managed body of the Cpu dialect reads the GPU-only members, so the test sees the generator's own output
        var generated = output.SyntaxTrees.Where(tree => tree.FilePath != "ManagedKernelBody.cs").ToArray();
        Assert.Contains(generated, static tree => tree.GetText().ToString().Contains("ThreadIndex.X"));
        Assert.Empty(await GetDiagnosticsAsync(output));
    }

    private static string ReadCase(string fileName) => RoslynTestUtility.ReadTestCase(CasesDirectory + fileName);

    private static async Task<string[]> AnalyzeAsync(params (string Path, string Source)[] files)
    {
        var diagnostics = await GetDiagnosticsAsync(files);
        return diagnostics.Select(Format).ToArray();
    }

    private static Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(params (string Path, string Source)[] files) =>
        GetDiagnosticsAsync(RoslynTestUtility.CreateCompilationAt(files));

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(Compilation compilation)
    {
        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(s_analyzer))
            .GetAnalyzerDiagnosticsAsync();
        return [.. diagnostics.OrderBy(static d => d.Location.SourceSpan.Start)];
    }

    // (line,column)-(line,column) of the span, 1-based, and the member the message names
    private static string Format(Diagnostic diagnostic)
    {
        Assert.Equal(GSharpDiagnostics.HostIntrinsicCallError.Id, diagnostic.Id);
        var span = diagnostic.Location.GetLineSpan();
        var start = span.StartLinePosition;
        var end = span.EndLinePosition;
        // The message quotes the member: '<member>' ...
        var message = diagnostic.GetMessage();
        var parts = message.AsSpan().Split('\'');
        Assert.True(parts.MoveNext() && parts.MoveNext(), message);
        var member = message.AsSpan(parts.Current).ToString();
        return $"({start.Line + 1},{start.Character + 1})-({end.Line + 1},{end.Character + 1}) {member}";
    }
}
