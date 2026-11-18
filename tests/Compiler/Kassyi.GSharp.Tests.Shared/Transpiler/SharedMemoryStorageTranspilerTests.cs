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
/// Verifies that a <c>SharedMemory&lt;T&gt;</c> storage declared by an <c>out</c> variable that
/// <c>Gpu.SyncGrid(body)</c> moves in front of the body is a <c>T*</c> and never the CPU frame wrapper type in the
/// CUDA output. The CPU dialect keeps such a variable in the state machine frame, which holds the wrapper type by design.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class SharedMemoryStorageTranspilerTests
{
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SyncGridOutVar_CudaDialect_DeclaresPointerInsteadOfWrapper()
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(
            "Transpiler/Emission/SharedMemoryStorageShapeKernels.cu.cs");
        var methodDecl = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static m => m.Identifier.Text == "SyncGridOutVarKernel");
        var failures = new List<TranspileFailure>();

        var source = MethodTranspiler.GenerateCudaSource(
            methodDecl, model, failures.Add, isDevice: false, new GSharpCudaDialect(), out _);

        Assert.Empty(failures);
        Assert.Contains(
            source.Split('\n'),
            static line => line.StartsWith("int* __gsharp_id_", StringComparison.Ordinal) &&
                           line.TrimEnd().EndsWith("_o;", StringComparison.Ordinal));
        Assert.DoesNotContain("Kassyi_GSharp_Core_SharedMemory", source, StringComparison.Ordinal);
    }
}
