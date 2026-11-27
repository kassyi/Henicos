#nullable enable
using Kassyi.GSharp.Compiler.Backends;
using Kassyi.GSharp.Tests.Cpu.Backends.Cpp;
using Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

namespace Kassyi.GSharp.Tests.Cpu.Backends;

/// <summary>
/// The switch of the IR routes (<see cref="IrRouteSwitch"/>): off by default, on inside a request of a test, and on for
/// a project that asks for it through the features of its parse options.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrRouteSwitchTests
{
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RequestedRoutes_NoFeatureNoScope_IsNone()
    {
        Assert.Equal(IrRouteRequest.None, IrRouteSwitch.RequestedRoutes(DefaultTree()));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void RequestedRoutes_WithTheScope_AreRequestedOnlyInside()
    {
        var tree = DefaultTree();
        using (IrRouteSwitch.Request())
        {
            Assert.Equal(IrRouteRequest.Native, IrRouteSwitch.RequestedRoutes(tree));
        }

        Assert.Equal(IrRouteRequest.None, IrRouteSwitch.RequestedRoutes(tree));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void RequestedRoutes_ScopeAndNesting_FollowTheRequest()
    {
        var tree = DefaultTree();
        const IrRouteRequest Both = IrRouteRequest.Native | IrRouteRequest.Cuda;

        Assert.Equal(IrRouteRequest.None, IrRouteSwitch.RequestedRoutes(tree));
        using (IrRouteSwitch.Request())
        {
            Assert.Equal(IrRouteRequest.Native, IrRouteSwitch.RequestedRoutes(tree));
            using (IrRouteSwitch.Request(Both))
            {
                Assert.Equal(Both, IrRouteSwitch.RequestedRoutes(tree));
            }

            Assert.Equal(IrRouteRequest.Native, IrRouteSwitch.RequestedRoutes(tree));
        }

        Assert.Equal(IrRouteRequest.None, IrRouteSwitch.RequestedRoutes(tree));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Includes_NativeAndCuda_MapToTheirRoutes()
    {
        Assert.True(IrRouteSwitch.Includes(IrRouteRequest.Native, IrBodyRoute.NativePlain));
        Assert.True(IrRouteSwitch.Includes(IrRouteRequest.Native, IrBodyRoute.NativeStack));
        Assert.False(IrRouteSwitch.Includes(IrRouteRequest.Native, IrBodyRoute.Cuda));

        Assert.False(IrRouteSwitch.Includes(IrRouteRequest.Cuda, IrBodyRoute.NativePlain));
        Assert.False(IrRouteSwitch.Includes(IrRouteRequest.Cuda, IrBodyRoute.NativeStack));
        Assert.True(IrRouteSwitch.Includes(IrRouteRequest.Cuda, IrBodyRoute.Cuda));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void RequestedRoutes_ProjectAsksThroughTheParseOptions_ReadsEachFeature()
    {
        Assert.Equal(IrRouteRequest.Native, IrRouteSwitch.RequestedRoutes(RequestingTree(IrRouteSwitch.NativeFeature)));
        Assert.Equal(IrRouteRequest.Cuda, IrRouteSwitch.RequestedRoutes(RequestingTree(IrRouteSwitch.CudaFeature)));
        Assert.Equal(IrRouteRequest.Native | IrRouteRequest.Cuda,
            IrRouteSwitch.RequestedRoutes(RequestingTree(IrRouteSwitch.NativeFeature, IrRouteSwitch.CudaFeature)));
    }

    private static SyntaxTree DefaultTree() =>
        IrTestCases.Compile(IrBodyLoweringTests.ReasonCases).SyntaxTrees.Last();

    // The tree of the test case with the requested parse-option features
    private static SyntaxTree RequestingTree(params string[] featureNames)
    {
        var tree = DefaultTree();
        var options = (CSharpParseOptions)tree.Options;
        var features = new Dictionary<string, string>(options.Features);
        foreach (var featureName in featureNames)
        {
            features[featureName] = "true";
        }

        return tree.WithRootAndOptions(tree.GetRoot(), options.WithFeatures(features));
    }
}
