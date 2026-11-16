#nullable enable
using System.Linq;
using Kassyi.GSharp.Compiler.Marshaling;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

/// <summary>
/// Unit tests for <see cref="PointerLaunchPlan"/> and related pointer launcher parameter classifications.
/// </summary>
[Category(TestCategory.Compiler)]
public class PointerLaunchPlanTests
{
    private static readonly string Source = RoslynTestUtility.ReadTestCase("Marshaling/KernelTwoFormsPlanSource.cs");

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PointerLaunchPlan_MatchingKernelInParameter_ClassifiesAsValueNotReference()
    {
        var compilation = RoslynTestUtility.CreateCompilation(Source);
        var type = compilation.GetTypeByMetadataName("Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.TestKernels");
        Assert.NotNull(type);
        var method = type.GetMembers("MatchingKernel").OfType<IMethodSymbol>().Single();
        var inParam = method.Parameters[0];
        var shape = KernelParameterShape.Classify(inParam);

        Assert.True(shape.IsInReference);
        Assert.Equal(PointerArgumentKind.Value, PointerLaunchPlan.Classify(shape));
        var launcherParam = LauncherSignatures.GetParameter(shape, LauncherKind.Pointer);
        Assert.Equal("global::System.Int32", launcherParam.TypeName);
        Assert.Equal(string.Empty, launcherParam.Modifier);
    }
}
