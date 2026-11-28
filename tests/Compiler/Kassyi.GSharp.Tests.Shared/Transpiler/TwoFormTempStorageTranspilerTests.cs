#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Two-form CUDA kernels emit both a by-value and a by-address form. Per-form temporary tables
/// (<c>DiscardStorages</c>, <c>SharedMemoryLifts</c>) must be cleared between forms so the second
/// form does not reuse the first form's count and redeclare the same temporary name (Issue #2334).
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class TwoFormTempStorageTranspilerTests
{
    private static string GenerateCuda(string kernelName)
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext("Transpiler/Emission/TwoFormTempStorageKernels.cu.cs");
        var methodDecl = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.Text == kernelName);
        var failures = new List<TranspileFailure>();

        var source = MethodTranspiler.GenerateCudaSource(
            methodDecl, model, failures.Add, isDevice: false, new GSharpCudaDialect(), out _);

        Assert.Empty(failures);
        return source;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void DiscardTwiceKernel_TwoForms_NumbersDiscardStoragesPerForm()
    {
        var source = GenerateCuda("DiscardTwiceKernel");

        Assert.Contains("DiscardTwiceKernel__gsharp_in_by_address", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__gsharp_discard_2", source, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(source, "__gsharp_discard_0{}"));
        Assert.Equal(2, CountOccurrences(source, "__gsharp_discard_1{}"));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SharedLiftTwiceKernel_TwoForms_NumbersSharedMemoryLiftsPerForm()
    {
        var source = GenerateCuda("SharedLiftTwiceKernel");

        Assert.Contains("SharedLiftTwiceKernel__gsharp_in_by_address", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__gsharp_smem_2", source, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(source, "__shared__ int __gsharp_smem_0[4]"));
        Assert.Equal(2, CountOccurrences(source, "__shared__ int __gsharp_smem_1[4]"));
    }
}
