using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class StructSizeDiagnosticsTests : TranspilerTestBase
{
    private const string TestCasePath = "Analyzers/StructSizeDiagnostics.cs";

    // The generator is run once for the whole class; every case reads the diagnostics inside its own method.
    private static readonly Lazy<Task<(SyntaxTree Tree, ImmutableArray<Diagnostic> Diagnostics)>> s_result =
        new(RunGeneratorAsync);

    [Theory]
    [Scenario(TestScenario.SadPath)]
    // 16 (array header) + 4096 = 4112 bytes.
    [InlineData("OverLimitKernel", "GSH0066", "4112 bytes")]
    // 1 byte padded to 8, then 4096 = 4104 bytes (the unaligned sum would be 4097).
    [InlineData("PaddingKernel", "GSH0066", "4104 bytes")]
    // 4096 + hidden result pointer 8 = 4104 bytes.
    [InlineData("ResultKernel", "GSH0066", "4104 bytes")]
    // SharedMemory<double>(5000) = 40000 bytes.
    [InlineData("DoubleSharedKernel", "GSH0013", "40000 bytes")]
    public async Task Test_StructSizeDiagnostics_ReportsOverLimitCase(string methodName, string diagnosticId,
        string expectedSize)
    {
        var diagnostics = await GetDiagnosticsForMethodAsync(methodName);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == diagnosticId);
        Assert.Contains(expectedSize, diagnostic.GetMessage());
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    // Exactly 4096 bytes of kernel parameters.
    [InlineData("AtLimitKernel", "GSH0066")]
    // A 48-byte struct by value: the retired GSH0014 warned, GSH0066 does not.
    [InlineData("SmallStructKernel", "GSH0066")]
    [InlineData("SmallStructKernel", "GSH0014")]
    // Device functions are not checked; the kernel passes the struct through an array.
    [InlineData("ReadBlock", "GSH0066")]
    [InlineData("DeviceCallKernel", "GSH0066")]
    // SharedMemory<double>(4096) = exactly 32768 bytes.
    [InlineData("DoubleAtLimitKernel", "GSH0013")]
    // SharedMemory<byte>(10000) = 10000 bytes (the old 4-byte guess computed 40000).
    [InlineData("ByteSharedKernel", "GSH0013")]
    public async Task Test_StructSizeDiagnostics_DoesNotReportWithinLimitCase(string methodName, string diagnosticId)
    {
        var diagnostics = await GetDiagnosticsForMethodAsync(methodName);

        Assert.DoesNotContain(diagnostics, d => d.Id == diagnosticId);
    }

    private static async Task<List<Diagnostic>> GetDiagnosticsForMethodAsync(string methodName)
    {
        var (tree, diagnostics) = await s_result.Value;
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                         .Single(m => m.Identifier.ValueText == methodName);
        return diagnostics.Where(d => method.Span.Contains(d.Location.SourceSpan)).ToList();
    }

    private static async Task<(SyntaxTree Tree, ImmutableArray<Diagnostic> Diagnostics)> RunGeneratorAsync()
    {
        var (_, tree, _) = RoslynTestUtility.GetTestCaseContext(TestCasePath);
        var diagnostics = await RoslynTestUtility.TestGenerator(TestCasePath).GetDiagnosticsAsync();
        return (tree, diagnostics);
    }
}
