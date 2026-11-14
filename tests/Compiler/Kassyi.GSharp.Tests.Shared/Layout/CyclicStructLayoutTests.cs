#nullable enable
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Layout;
using Kassyi.GSharp.Tests.Infrastructure;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Layout.TypeLayoutTestHelper;

namespace Kassyi.GSharp.Tests.Shared.Layout;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class CyclicStructLayoutTests
{
    private const string CyclicStructCase = "Analyzers/CyclicStructParameter.cu.cs";

    private const string MutualCyclicSource = """
        public struct MutualA
        {
            public MutualB B;
            public int X;
        }

        public struct MutualB
        {
            public MutualA A;
            public int Y;
        }
        """;

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void TypeLayoutCalculator_DirectCyclicStruct_ReturnsFallbackWithoutStackOverflow(string dialectName)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(CyclicStructCase));
        var type = compilation.GetTypeByMetadataName("Kassyi.GSharp.Tests.TestCases.Analyzers.CyclicStructParameter+Cyc");
        Assert.NotNull(type);
        var cyc = (INamedTypeSymbol)type;
        var dialect = dialectName == "Cuda" ? s_cudaDialect : s_cpuDialect;

        var deviceLayout = TypeLayoutCalculator.GetDeviceLayout(cyc, dialect);
        Assert.True(deviceLayout.Size > 0);

        var structLayout = TypeLayoutCalculator.GetStructLayout(cyc, dialect);
        Assert.NotNull(structLayout);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void HostLayoutCalculator_DirectCyclicStruct_ReturnsFalseWithoutStackOverflow(string dialectName)
    {
        _ = dialectName;
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(CyclicStructCase));
        var type = compilation.GetTypeByMetadataName("Kassyi.GSharp.Tests.TestCases.Analyzers.CyclicStructParameter+Cyc");
        Assert.NotNull(type);
        var cyc = (INamedTypeSymbol)type;

        var hasHost = HostLayoutCalculator.TryGetHostLayout(cyc, out var hostLayout);
        Assert.False(hasHost);
        Assert.Equal(0, hostLayout.Size);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void TransferClassifier_DirectCyclicStruct_TerminatesWithoutStackOverflow(string dialectName)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(CyclicStructCase));
        var type = compilation.GetTypeByMetadataName("Kassyi.GSharp.Tests.TestCases.Analyzers.CyclicStructParameter+Cyc");
        Assert.NotNull(type);
        var cyc = (INamedTypeSymbol)type;
        var dialect = dialectName == "Cuda" ? s_cudaDialect : s_cpuDialect;

        var hasArray = TransferClassifier.TryFindArrayField(cyc, out var path);
        Assert.False(hasArray);
        Assert.Empty(path);

        var difference = TransferClassifier.DescribeLayoutDifference(cyc, dialect);
        Assert.NotEmpty(difference);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void TypeLayoutCalculator_MutualCyclicStruct_ReturnsFallbackWithoutStackOverflow(string dialectName)
    {
        var compilation = RoslynTestUtility.CreateCompilation(MutualCyclicSource);
        var type = compilation.GetTypeByMetadataName("MutualA");
        Assert.NotNull(type);
        var mutualA = (INamedTypeSymbol)type;
        var dialect = dialectName == "Cuda" ? s_cudaDialect : s_cpuDialect;

        var deviceLayout = TypeLayoutCalculator.GetDeviceLayout(mutualA, dialect);
        Assert.True(deviceLayout.Size > 0);

        var structLayout = TypeLayoutCalculator.GetStructLayout(mutualA, dialect);
        Assert.NotNull(structLayout);

        var hasHost = HostLayoutCalculator.TryGetHostLayout(mutualA, out _);
        Assert.False(hasHost);

        var hasArray = TransferClassifier.TryFindArrayField(mutualA, out _);
        Assert.False(hasArray);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public async Task GpuKernelAnalyzer_CyclicStructParameter_CompletesWithoutStackOverflow(string dialect)
    {
        var (_, analyzer, generator) = await KernelDiagnostics.GetDiagnosticsAsync(
            RoslynTestUtility.ReadTestCase(CyclicStructCase),
            dialect);

        // Analysis and generation complete without StackOverflowException.
        Assert.NotNull(analyzer);
        Assert.NotNull(generator);
    }
}
