using Kassyi.GSharp.Compiler.Transpiler;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// The parameters G# appends to a kernel's native signature, in the order the signature has them: the result pointer,
/// the grid aggregate scratch and the launch epoch, each aligned to its own size.
/// </summary>
[Category(TestCategory.Compiler)]
public class KernelHiddenParametersTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Transpiler/KernelHiddenParameterCases.cs")
    );

    // The record each kernel gets from its own syntax: a value return takes the result pointer, a grid aggregate of a
    // cooperative kernel the scratch, CUDA the epoch (and the scratch); a CPU dialect none of the last two.
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("VoidKernel", true, false, false, true)]
    [InlineData("ValueKernel", true, true, false, true)]
    [InlineData("GridSumKernel", true, false, true, true)]
    [InlineData("VoidKernel", false, false, false, false)]
    [InlineData("ValueKernel", false, true, false, false)]
    [InlineData("GridSumKernel", false, false, false, false)]
    public void For_Kernel_ListsTheHiddenParametersTheSignatureTakes(
        string name,
        bool isCuda,
        bool resultBuffer,
        bool gridScratch,
        bool launchEpoch)
    {
        var kernel = (s_compilation.GetTypeByMetadataName("KernelHiddenParameterCases.Kernels") ??
                      throw new InvalidOperationException("Type 'Kernels' not found."))
            .GetMembers(name)
            .OfType<IMethodSymbol>()
            .First();
        var syntax = (MethodDeclarationSyntax)kernel.DeclaringSyntaxReferences[0].GetSyntax();
        var model = s_compilation.GetSemanticModel(syntax.SyntaxTree);

        var hidden = KernelHiddenParameters.For(syntax, kernel, model, isCuda);

        Assert.Equal(new KernelHiddenParameters(resultBuffer, gridScratch, launchEpoch), hidden);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    // CUDA, void, no grid aggregate: only the epoch
    [InlineData(false, false, true, 32760, 32764)]
    [InlineData(false, false, true, 32761, 32768)]
    // A CPU dialect appends the result pointer only
    [InlineData(true, false, false, 4, 16)]
    [InlineData(false, false, false, 5, 5)]
    // Result pointer, scratch pointer and epoch: 1 -> 8 + 8 = 16, + 8 = 24, + 4 = 28
    [InlineData(true, true, true, 1, 28)]
    // The scratch alone after an odd size: 3 -> 8 + 8 = 16, + 4 = 20
    [InlineData(false, true, true, 3, 20)]
    public void AppendTo_UserParameterBytes_AddsEachHiddenParameterAligned(
        bool resultBuffer,
        bool gridScratch,
        bool launchEpoch,
        int userBytes,
        int expected)
    {
        var hidden = new KernelHiddenParameters(resultBuffer, gridScratch, launchEpoch);

        Assert.Equal(expected, hidden.AppendTo(userBytes));
    }
}
