using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// GSH0003 rejects a member of <c>Interlocked</c> or <c>BitOperations</c> exactly when no <c>[BclIntrinsicMap]</c> maps
/// it, the table the transpiler converts by, and a mapped one used as a method group (#570).
/// </summary>
[Category(TestCategory.Compiler)]
public class BclIntrinsicMappedMembersDiagnosticsTests
{
    private const string TestCase = "Diagnostics/BclIntrinsicMappedMembers.cu.cs";

    [Theory]
    [InlineData("Cpu", "MappedInterlockedKernel")]
    [InlineData("Cpu", "MappedBitOperationsKernel")]
    [InlineData("Cuda", "MappedInterlockedKernel")]
    [InlineData("Cuda", "MappedBitOperationsKernel")]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_MappedMember_ReportsNothing(string dialect, string kernel)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Empty(InKernel(diagnostics, kernel));
    }

    // Kernel, the text of the message, then the range of its one GSH0003
    [Theory]
    [InlineData("Cpu", "UnmappedBitOperationsKernel", "(no G# mapping)", 30, 19, 30, 50)]
    [InlineData("Cpu", "UnmappedInterlockedOverloadKernel", "(no G# mapping)", 38, 9, 38, 23)]
    [InlineData("Cpu", "MappedMethodGroupKernel", "(used as a method group)", 46, 36, 46, 58)]
    [InlineData("Cuda", "UnmappedBitOperationsKernel", "(no G# mapping)", 30, 19, 30, 50)]
    [InlineData("Cuda", "UnmappedInterlockedOverloadKernel", "(no G# mapping)", 38, 9, 38, 23)]
    [InlineData("Cuda", "MappedMethodGroupKernel", "(used as a method group)", 46, 36, 46, 58)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UnmappedMemberOrMethodGroup_ReportsGsh0003(string dialect, string kernel, string reason,
        int startLine, int startColumn, int endLine, int endColumn)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal([(Analyzer, startLine, startColumn, endLine, endColumn)], InKernel(diagnostics, kernel, reason));
    }

    // The GSH0003 ranges inside the declaration of kernel, with reason in the message when one is given
    private static List<(DiagnosticOrigin, int, int, int, int)> InKernel(ImmutableArray<ReportedDiagnostic> diagnostics,
        string kernel, string? reason = null)
    {
        var span = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(TestCase)).SyntaxTrees.Last()
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == kernel)
            .GetLocation()
            .GetLineSpan()
            .Span;

        return diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, reason)
            .Where(range => range.StartLine > span.Start.Line && range.StartLine <= span.End.Line + 1)
            .ToList();
    }
}
