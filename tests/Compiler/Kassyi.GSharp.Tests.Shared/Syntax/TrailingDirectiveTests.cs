#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Compiler.Transpiler.Managed;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Infrastructure;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class TrailingDirectiveTests : TranspilerTestBase
{
    private static string GenerateManagedSource(string methodName)
    {
        var (_, tree, model) = RoslynTestUtility.GetKernelContext();
        var methodDecl = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == methodName);
        var plan = MethodEmissionPlan.Create(methodDecl, model, isKernel: true);
        return ManagedCSharpTranspiler.Generate(in plan);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(nameof(TrailingDirectiveKernels.TrailingPragmaNoBarrierKernel))]
    [InlineData(nameof(TrailingDirectiveKernels.TrailingPragmaWithBarrierKernel))]
    [InlineData(nameof(TrailingDirectiveKernels.TrailingPragmaLoopWithBarrierKernel))]
    public void TrailingPragma_ManagedCopy_RestoresWarningExactlyOnceAfterDisable(string kernelName)
    {
        var managed = GenerateManagedSource(kernelName);
        const string disableDirective = "#pragma warning disable CS0219";
        const string restoreDirective = "#pragma warning restore CS0219";

        int disableCount = CountOccurrences(managed, disableDirective);
        int restoreCount = CountOccurrences(managed, restoreDirective);

        Assert.Equal(1, disableCount);
        Assert.Equal(1, restoreCount);

        int disableIndex = managed.IndexOf(disableDirective, StringComparison.Ordinal);
        int restoreIndex = managed.IndexOf(restoreDirective, StringComparison.Ordinal);
        Assert.True(
            restoreIndex > disableIndex,
            $"Expected '{restoreDirective}' (at {restoreIndex}) to appear after '{disableDirective}' (at {disableIndex}).");

        // Guard against #if / #region directives leaking into managed source
        Assert.DoesNotContain("#if", managed);
        Assert.DoesNotContain("#endif", managed);
        Assert.DoesNotContain("#region", managed);
        Assert.DoesNotContain("#endregion", managed);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }
}
