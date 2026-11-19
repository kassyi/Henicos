#nullable enable
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Verifies that C++ emission for <c>out SharedMemory&lt;T&gt;</c> declarations and discard arguments emits a pointer
/// storage type instead of the CPU state-machine wrapper type in both CPU and CUDA dialects.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class SharedMemoryOutTranspilerTests
{
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("OutVarKernel", "Cpu")]
    [InlineData("OutVarKernel", "Cuda")]
    [InlineData("OutTypedKernel", "Cpu")]
    [InlineData("OutTypedKernel", "Cuda")]
    public void OutDeclaration_CpuAndCudaDialects_DeclaresPointerInsteadOfWrapper(
        string kernelName,
        string dialectName)
    {
        var (source, failures) = TranspileKernel(kernelName, dialectName);

        Assert.Empty(failures);
        Assert.True(
            source.Contains("\nint* __gsharp_id_3_o;\r\n", StringComparison.Ordinal) ||
            source.Contains("\nint* __gsharp_id_3_o;\n", StringComparison.Ordinal),
            $"Expected pointer declaration line 'int* __gsharp_id_3_o;' in {dialectName} output:\n{source}");
        Assert.DoesNotContain("Kassyi_GSharp_Core_SharedMemory", source, StringComparison.Ordinal);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void OutDiscard_CpuAndCudaDialects_DeclaresPointerStorageInsteadOfWrapper(string dialectName)
    {
        var (source, failures) = TranspileKernel("OutDiscardKernel", dialectName);

        Assert.Empty(failures);
        Assert.True(
            source.Contains("\nint* __gsharp_discard_0{};\r\n", StringComparison.Ordinal) ||
            source.Contains("\nint* __gsharp_discard_0{};\n", StringComparison.Ordinal),
            $"Expected 'int* __gsharp_discard_0{{}};' in {dialectName} output:\n{source}");
        Assert.DoesNotContain("Kassyi_GSharp_Core_SharedMemory", source, StringComparison.Ordinal);
    }

    private static (string Source, List<TranspileFailure> Failures) TranspileKernel(
        string kernelName,
        string dialectName)
    {
        var (model, methodSymbol) = RoslynTestUtility.GetMethodContext("SharedMemoryOutKernels", kernelName);
        var methodDecl = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();
        ITranspilerDialect dialect = dialectName == "Cuda" ? new GSharpCudaDialect() : new StandardCppDialect();
        var failures = new List<TranspileFailure>();
        var source = MethodTranspiler.GenerateCudaSource(
            methodDecl, model, failures.Add, isDevice: false, dialect, out _);
        return (source, failures);
    }
}
