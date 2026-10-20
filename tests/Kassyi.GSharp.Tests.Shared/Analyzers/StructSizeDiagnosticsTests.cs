using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class StructSizeDiagnosticsTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_StructSizeDiagnostics_SadPathAndBoundaryCases()
    {
        var (compilation, tree, semanticModel) =
            RoslynTestUtility.GetTestCaseContext("Analyzers/StructSizeDiagnostics.cs");

        var diagnostics = await RoslynTestUtility.TestGenerator("Analyzers/StructSizeDiagnostics.cs")
            .GetDiagnosticsAsync();

        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();

        // Helper to find diagnostics targeting a specific method span
        List<Diagnostic> GetDiagnosticsForMethod(string methodName)
        {
            var method = methods.First(m => m.Identifier.ValueText == methodName);
            return diagnostics.Where(d => method.Span.Contains(d.Location.SourceSpan)).ToList();
        }

        // Case 1: AtLimitKernel - total 4096 bytes -> no GSH0066
        var case1Diags = GetDiagnosticsForMethod("AtLimitKernel");
        Assert.DoesNotContain(case1Diags, d => d.Id == "GSH0066");

        // Case 2: OverLimitKernel - 16 + 4096 = 4112 bytes -> GSH0066, message contains 4112
        var case2Diags = GetDiagnosticsForMethod("OverLimitKernel");
        var case2Diag = Assert.Single(case2Diags, d => d.Id == "GSH0066");
        Assert.Contains("4112", case2Diag.GetMessage());

        // Case 3: PaddingKernel - 1 byte padded to 8 + 4096 = 4104 bytes -> GSH0066, message contains 4104
        var case3Diags = GetDiagnosticsForMethod("PaddingKernel");
        var case3Diag = Assert.Single(case3Diags, d => d.Id == "GSH0066");
        Assert.Contains("4104", case3Diag.GetMessage());

        // Case 4: ResultKernel - 4096 + hidden result pointer 8 = 4104 bytes -> GSH0066, message contains 4104
        var case4Diags = GetDiagnosticsForMethod("ResultKernel");
        var case4Diag = Assert.Single(case4Diags, d => d.Id == "GSH0066");
        Assert.Contains("4104", case4Diag.GetMessage());

        // Case 5: SmallStructKernel - 48 bytes by value -> no GSH0066 (and GSH0014 retired)
        var case5Diags = GetDiagnosticsForMethod("SmallStructKernel");
        Assert.DoesNotContain(case5Diags, d => d.Id == "GSH0066");
        Assert.DoesNotContain(case5Diags, d => d.Id == "GSH0014");

        // Case 6: DeviceCallKernel / ReadBlock - device function not checked -> no GSH0066
        var case6DiagsReadBlock = GetDiagnosticsForMethod("ReadBlock");
        Assert.DoesNotContain(case6DiagsReadBlock, d => d.Id == "GSH0066");
        var case6DiagsDeviceCall = GetDiagnosticsForMethod("DeviceCallKernel");
        Assert.DoesNotContain(case6DiagsDeviceCall, d => d.Id == "GSH0066");

        // Case 7: DoubleSharedKernel - SharedMemory<double>(5000) (40000 bytes) -> GSH0013, message contains 40000
        var case7Diags = GetDiagnosticsForMethod("DoubleSharedKernel");
        var case7Diag = Assert.Single(case7Diags, d => d.Id == "GSH0013");
        Assert.Contains("40000", case7Diag.GetMessage());

        // Case 8: DoubleAtLimitKernel - SharedMemory<double>(4096) (32768 bytes) -> no GSH0013
        var case8Diags = GetDiagnosticsForMethod("DoubleAtLimitKernel");
        Assert.DoesNotContain(case8Diags, d => d.Id == "GSH0013");

        // Case 9: ByteSharedKernel - SharedMemory<byte>(10000) (10000 bytes) -> no GSH0013
        var case9Diags = GetDiagnosticsForMethod("ByteSharedKernel");
        Assert.DoesNotContain(case9Diags, d => d.Id == "GSH0013");
    }
}
