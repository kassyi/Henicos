#nullable enable
using System.Linq;
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Cpu.Analysis;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class StackSuspensionAnalysisTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ReachesOnStack_CompilationWithoutImports_MatchesStateMachineQueries()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analysis/ManagedStackEligibilityCases.cs"));

        var gpuMethods = compilation.SyntaxTrees
            .SelectMany(tree =>
            {
                var model = compilation.GetSemanticModel(tree);
                return tree.GetRoot()
                    .DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .Select(declaration => model.GetDeclaredSymbol(declaration))
                    .OfType<IMethodSymbol>()
                    .Where(GpuExecutionSpace.IsGpuFunction);
            })
            .ToArray();

        Assert.NotEmpty(gpuMethods);
        foreach (var method in gpuMethods)
        {
            Assert.Equal(
                SuspensionAnalysis.ReachesWarpShuffle(method, compilation),
                SuspensionAnalysis.ReachesWarpShuffleOnStack(method, compilation));
            Assert.Equal(
                SuspensionAnalysis.ReachesActiveMask(method, compilation),
                SuspensionAnalysis.ReachesActiveMaskOnStack(method, compilation));
            Assert.Equal(
                SuspensionAnalysis.ReachesGridBarrier(method, compilation),
                SuspensionAnalysis.ReachesGridBarrierOnStack(method, compilation));
        }
    }

    [Theory]
    [InlineData("WaitBlock", true, false, false, false, false)]
    [InlineData("WaitShuffle", true, true, false, false, false)]
    [InlineData("WaitGrid", true, false, false, false, true)]
    [InlineData("WaitActiveMask", true, true, true, false, false)]
    [Scenario(TestScenario.HappyPath)]
    public void ReachesOnStack_ImportedLibraryFunction_AnswersFromStackTraits(
        string methodName,
        bool expectedShuffle,
        bool expectedShuffleOnStack,
        bool expectedActiveMaskOnStack,
        bool expectedGrid,
        bool expectedGridOnStack)
    {
        var library = DeviceLibraryBuilder.Build(
            "StackTraitsLib",
            null,
            RoslynTestUtility.ReadTestCase("Generators/DeviceExportStackTraitsLibrary.cs"));
        var consumer = DeviceLibraryBuilder.CreateConsumer(
            RoslynTestUtility.ReadTestCase("Analysis/CrossAssemblyStackConsumer.cs"),
            library);

        var libType = consumer.GetTypeByMetadataName("StackTraitsCase.StackTraitsLib")
            ?? throw new InvalidOperationException("StackTraitsLib not found.");
        var method = libType.GetMembers(methodName).OfType<IMethodSymbol>().Single();

        Assert.Equal(expectedShuffle, SuspensionAnalysis.ReachesWarpShuffle(method, consumer));
        Assert.Equal(expectedShuffleOnStack, SuspensionAnalysis.ReachesWarpShuffleOnStack(method, consumer));
        Assert.Equal(expectedActiveMaskOnStack, SuspensionAnalysis.ReachesActiveMaskOnStack(method, consumer));
        Assert.Equal(expectedGrid, SuspensionAnalysis.ReachesGridBarrier(method, consumer));
        Assert.Equal(expectedGridOnStack, SuspensionAnalysis.ReachesGridBarrierOnStack(method, consumer));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void ReachesOnStack_IncompatibleRecord_FallsBackToSuspends()
    {
        var library = DeviceLibraryBuilder.Build(
            "XasmLib",
            new Dictionary<string, string> { ["build_property.GSharpExportDeviceCode"] = "false" },
            RoslynTestUtility.ReadTestCase("Diagnostics/CrossAssembly/Library.cs"),
            HandWrittenExportRecords.Read("Diagnostics/CrossAssembly/HandWrittenSuspendingRecord.cs",
                formatVersion: 3));
        var consumer = DeviceLibraryBuilder.CreateConsumer(
            RoslynTestUtility.ReadTestCase("Diagnostics/CrossAssembly/Consumer.cs"),
            library);

        var libType = consumer.GetTypeByMetadataName("XasmLib.LibMath")
            ?? throw new InvalidOperationException("LibMath not found.");
        var method = libType.GetMembers("Twice").OfType<IMethodSymbol>().Single();

        Assert.True(SuspensionAnalysis.ReachesWarpShuffleOnStack(method, consumer));
        Assert.True(SuspensionAnalysis.ReachesActiveMaskOnStack(method, consumer));
        Assert.True(SuspensionAnalysis.ReachesGridBarrierOnStack(method, consumer));
    }
}
