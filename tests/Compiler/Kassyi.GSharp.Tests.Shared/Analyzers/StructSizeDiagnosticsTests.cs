using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
[Collection(TestCollections.CorpusScan)]
public class StructSizeDiagnosticsTests : TranspilerTestBase
{
    private const string TestCasePath = "Analyzers/StructSizeDiagnostics.cs";

    // The analyzers and the generator run once for the whole class; every case reads the diagnostics inside its own method.
    private static readonly Lazy<Task<(SyntaxTree Tree, ImmutableArray<Diagnostic> Diagnostics)>> s_result =
        new(RunGeneratorAsync);

    [Theory]
    [Scenario(TestScenario.SadPath)]
    // 32761, aligned to 4 for the launch epoch, + 4 = 32768 bytes: within the limit only when the epoch is not counted.
    [InlineData("OverLimitKernel", "GSH0064", "32768 bytes")]
    // 16 (array header) + 32760 + 4 (epoch) = 32780 bytes.
    [InlineData("ArrayAndBlockKernel", "GSH0064", "32780 bytes")]
    // 1 byte padded to 8, 32752, 1 byte, padded to 4, + 4 (epoch) = 32768 bytes (the unaligned sum would be 32758).
    [InlineData("PaddingKernel", "GSH0064", "32768 bytes")]
    // 16 + 40960 + 4 = 40980 bytes: nvcc rejects it ("max 32764 bytes allowed").
    [InlineData("LargeKernel", "GSH0064", "40980 bytes")]
    // An `in` struct is passed by value, the same 40980 bytes (not an 8-byte pointer).
    [InlineData("InLargeKernel", "GSH0064", "40980 bytes")]
    // The by-value entry point of an `in` struct beside a `ref` of its type: 32760 + 8 + 4 = 32772 bytes (counting the
    // by-address one alone gives 20).
    [InlineData("InBesideRefLargeKernel", "GSH0064", "32772 bytes")]
    // 32760 + 8 (grid aggregate scratch) + 4 (epoch) = 32772 bytes; the mutant that leaves the scratch out gives 32764.
    [InlineData("GridScratchKernel", "GSH0064", "32772 bytes")]
    // SharedMemory<double>(5000) = 40000 bytes.
    [InlineData("DoubleSharedKernel", "GSH0013", "40000 bytes")]
    // The byte count is computed in long: 2,400,000,000 and 2^32 bytes would wrap in int to a negative number and to 0
    [InlineData("HugeSharedKernel", "GSH0013", "2400000000 bytes")]
    [InlineData("WrapToZeroSharedKernel", "GSH0013", "4294967296 bytes")]
    public async Task StructSizeDiagnostics_StructOverLimit_ReportsOverLimitCase(string methodName, string diagnosticId,
        string expectedSize)
    {
        var diagnostics = await GetDiagnosticsForMethodAsync(methodName);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == diagnosticId);
        Assert.Contains(expectedSize, diagnostic.GetMessage());
        if (diagnosticId == "GSH0064")
        {
            // nvcc rejects every such kernel, so the build stops here
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Contains("32764-byte", diagnostic.GetMessage());
        }
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    // 32760 + 4 (epoch) = exactly 32764 bytes of kernel parameters.
    [InlineData("AtLimitKernel", "GSH0064")]
    // 32752 + 8 (result pointer) + 4 (epoch) = exactly 32764 bytes.
    [InlineData("ResultKernel", "GSH0064")]
    // 16 + 8192 + 4 = 8212 bytes: above the old 4,096-byte check, accepted by nvcc and run on the GPU (#1168).
    [InlineData("MediumKernel", "GSH0064")]
    // A `ref` struct is an 8-byte pointer, whatever its size.
    [InlineData("RefLargeKernel", "GSH0064")]
    // The parameters of GridScratchKernel without the aggregate: exactly 32764 bytes.
    [InlineData("NoGridScratchKernel", "GSH0064")]
    // A 48-byte struct by value: the retired GSH0014 warned, GSH0064 does not.
    [InlineData("SmallStructKernel", "GSH0064")]
    [InlineData("SmallStructKernel", "GSH0014")]
    // Device functions are not checked; the kernel passes the struct through an array.
    [InlineData("ReadBlock", "GSH0064")]
    [InlineData("DeviceCallKernel", "GSH0064")]
    // SharedMemory<double>(4096) = exactly 32768 bytes.
    [InlineData("DoubleAtLimitKernel", "GSH0013")]
    // SharedMemory<byte>(10000) = 10000 bytes (the old 4-byte guess computed 40000).
    [InlineData("ByteSharedKernel", "GSH0013")]
    public async Task StructSizeDiagnostics_StructWithinLimit_DoesNotReportWithinLimitCase(
        string methodName,
        string diagnosticId)
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
        var (tree, analyzer, generator) =
            await KernelDiagnostics.GetDiagnosticsAsync(RoslynTestUtility.ReadTestCase(TestCasePath), "Cpu");
        return (tree, analyzer.AddRange(generator));
    }
}
