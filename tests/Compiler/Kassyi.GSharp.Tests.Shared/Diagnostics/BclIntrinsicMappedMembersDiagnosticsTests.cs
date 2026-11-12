#nullable enable
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A member of <c>Interlocked</c> or <c>BitOperations</c> is rejected exactly when no <c>[BclIntrinsicMap]</c> maps
/// it, the table the transpiler converts by (GSH0109); a mapped one that takes a nint or nuint (#983), and a mapped one
/// used as a method group, also named through using static (#570), are reported as GSH0003.
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
    public async Task Gsh0003_MappedInterlockedOrBitOperationsMember_ReportsNothing(string dialect, string kernel)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Empty(InKernel(diagnostics, kernel, GSharpDiagnostics.UnsupportedSyntax.Id));
        Assert.Empty(InKernel(diagnostics, kernel, GSharpDiagnostics.UnmappedMethodCalledFromGpuCode.Id));
    }

    // Kernel, the text of the message, then the range of its one GSH0003
    [Theory]
    [InlineData("Cpu", "MappedMethodGroupKernel", "(used as a method group)", 47, 36, 47, 58)]
    [InlineData("Cpu", "UsingStaticMethodGroupKernel", "(used as a method group)", 56, 36, 56, 44)]
    [InlineData("Cpu", "NuintOverloadKernel", "(nint and nuint are not supported)", 65, 21, 65, 43)]
    [InlineData("Cuda", "MappedMethodGroupKernel", "(used as a method group)", 47, 36, 47, 58)]
    [InlineData("Cuda", "UsingStaticMethodGroupKernel", "(used as a method group)", 56, 36, 56, 44)]
    [InlineData("Cuda", "NuintOverloadKernel", "(nint and nuint are not supported)", 65, 21, 65, 43)]
    [Scenario(TestScenario.SadPath)]
    public async Task Gsh0003_MethodGroupOrNativeInteger_ReportsOneAtTheCall(string dialect, string kernel,
        string reason, int startLine, int startColumn, int endLine, int endColumn)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, startLine, startColumn, endLine, endColumn)],
            InKernel(diagnostics, kernel, GSharpDiagnostics.UnsupportedSyntax.Id, reason));
    }

    // Kernel, then the range of its one GSH0109: the whole call
    [Theory]
    [InlineData("Cpu", "UnmappedBitOperationsKernel", 31, 19, 31, 59)]
    [InlineData("Cpu", "UnmappedInterlockedOverloadKernel", 39, 9, 39, 43)]
    [InlineData("Cuda", "UnmappedBitOperationsKernel", 31, 19, 31, 59)]
    [InlineData("Cuda", "UnmappedInterlockedOverloadKernel", 39, 9, 39, 43)]
    [Scenario(TestScenario.SadPath)]
    public async Task Gsh0109_UnmappedMember_ReportsOneAtTheCall(string dialect, string kernel, int startLine,
        int startColumn, int endLine, int endColumn)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, startLine, startColumn, endLine, endColumn)],
            InKernel(diagnostics, kernel, GSharpDiagnostics.UnmappedMethodCalledFromGpuCode.Id));
        Assert.Empty(InKernel(diagnostics, kernel, GSharpDiagnostics.UnsupportedSyntax.Id));
    }

    // The ranges of id inside the declaration of kernel, with reason in the message when one is given
    private static List<(DiagnosticOrigin, int, int, int, int)> InKernel(ImmutableArray<ReportedDiagnostic> diagnostics,
        string kernel, string id, string? reason = null)
    {
        var span = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(TestCase)).SyntaxTrees.Last()
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == kernel)
            .GetLocation()
            .GetLineSpan()
            .Span;

        return diagnostics.Ranges(id, reason)
            .Where(range => range.StartLine > span.Start.Line && range.StartLine <= span.End.Line + 1)
            .ToList();
    }
}
