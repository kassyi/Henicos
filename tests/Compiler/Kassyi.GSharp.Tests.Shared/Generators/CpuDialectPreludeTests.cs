namespace Kassyi.GSharp.Tests.Shared.Generators;

/// <summary>
/// The Cpu dialect compiles its C++ against <c>Prelude.cpp</c> only. Library calls the transpiler emits for a kernel
/// have to be declared by that prelude, or by an include the kernel lists.
/// </summary>
[Category(TestCategory.Compiler)]
public class CpuDialectPreludeTests
{
    private static string GeneratedText(string testCase, string dialect)
    {
        var source = RoslynTestUtility.ReadTestCase(testCase);
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, dialect);
        return string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(static t => t.GetText().ToString()));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_CpuDialect_StackLinqCall_IsDeclaredByCpuPrelude()
    {
        var text = GeneratedText("Generators/GpuKernel_StackLinqUnfused.cu.cs", "Cpu");

        Assert.Contains("Kassyi::ZeroAlloc::Linq::StackLinq::", text);
        var cpuPrelude = File.ReadAllText(RepositoryPaths.Combine(
            "src", "Compiler", "Kassyi.GSharp.Compiler", "Resources", "Prelude.cpp"));
        var declared = cpuPrelude.Contains("StackLinq.cuh") || text.Contains("StackLinq.cuh");
        Assert.True(declared, "Cpu output calls Kassyi::ZeroAlloc::Linq::StackLinq::* but nothing includes StackLinq.cuh.");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_CpuDialect_MemoryExtensionsSort_DoesNotEmitThrustDevicePolicy()
    {
        var text = GeneratedText("Generators/GpuKernel_StackLinqUnfused.cu.cs", "Cpu");

        Assert.DoesNotContain("thrust::device", text);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CudaDialect_StackLinqCall_Control()
    {
        var text = GeneratedText("Generators/GpuKernel_StackLinqUnfused.cu.cs", "Cuda");

        Assert.Contains("Kassyi::ZeroAlloc::Linq::StackLinq::", text);
    }
}
