#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Compiler.Transpiler.Managed;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

[TestTarget(ExecutionTarget.GpuExecution)]
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

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void TrailingPragmaNoBarrier_ManagedCopy_RetainsTrailingPragmaWarningRestore()
    {
        var managed = GenerateManagedSource(nameof(TrailingDirectiveKernels.TrailingPragmaNoBarrierKernel));
        Assert.Contains("#pragma warning restore CS0219", managed);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void TrailingPragmaWithBarrier_ManagedCopy_RetainsTrailingPragmaWarningRestore()
    {
        var managed = GenerateManagedSource(nameof(TrailingDirectiveKernels.TrailingPragmaWithBarrierKernel));
        Assert.Contains("#pragma warning restore CS0219", managed);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void TrailingIfEndIf_ManagedCopy_StripsConditionalDirectivesCleanly()
    {
        var managed = GenerateManagedSource(nameof(TrailingDirectiveKernels.TrailingIfEndIfKernel));
        Assert.DoesNotContain("#if", managed);
        Assert.DoesNotContain("#endif", managed);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void TrailingRegion_ManagedCopy_StripsRegionDirectivesCleanly()
    {
        var managed = GenerateManagedSource(nameof(TrailingDirectiveKernels.TrailingRegionKernel));
        Assert.DoesNotContain("#region", managed);
        Assert.DoesNotContain("#endregion", managed);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void TrailingPragmaNoBarrier_ExecutesCorrectly(bool forceManaged)
    {
        int[] data = [0, 0];
        Run(forceManaged, () => TrailingDirectiveKernels.LaunchTrailingPragmaNoBarrierKernel(data, blockDimX: 2));
        Assert.Equal([0, 1], data);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void TrailingPragmaWithBarrier_ExecutesCorrectly(bool forceManaged)
    {
        int[] data = [0, 0];
        Run(forceManaged, () => TrailingDirectiveKernels.LaunchTrailingPragmaWithBarrierKernel(data, blockDimX: 2));
        Assert.Equal([1, 2], data);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void TrailingIfEndIf_ExecutesCorrectly(bool forceManaged)
    {
        int[] data = [0, 0];
        Run(forceManaged, () => TrailingDirectiveKernels.LaunchTrailingIfEndIfKernel(data, blockDimX: 2));
        Assert.Equal([100, 101], data);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void TrailingRegion_ExecutesCorrectly(bool forceManaged)
    {
        int[] data = [0, 0];
        Run(forceManaged, () => TrailingDirectiveKernels.LaunchTrailingRegionKernel(data, blockDimX: 2));
        Assert.Equal([200, 201], data);
    }

    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");
        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            launch();
        }
    }
}
