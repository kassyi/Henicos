#nullable enable
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Generator;

public class GEN_35_AotConfigurationTests
{
    [Fact]
    [Trait("FeatureId", "GEN-35")]
    [Trait("Category", "Transpiler")]
    public void Test_GEN35_AotConfiguration_AlwaysProducesAotWrapper()
    {
        var sourceCode = """
        using Kassyi.GSharp.Core;
        namespace AotTest {
            public partial class MyKernel {
                [GpuKernel]
                public static void Run(float[] data) {}
            }
        }
        """;
        var (_, outputCompilation, _) = RoslynTestUtility.RunGenerator(sourceCode);
        var generatedTrees = outputCompilation.SyntaxTrees.Where(t => t.FilePath.EndsWith(".g.cs")).ToList();
        
        // Ensure wrapper is generated
        Assert.Contains(generatedTrees, t => t.FilePath.Contains("MyKernel_Run_Wrapper.g.cs"));
        
        var wrapperSource = generatedTrees.First(t => t.FilePath.Contains("MyKernel_Run_Wrapper.g.cs")).ToString();
        
        // We unified naming, so it should expect MyKernel_Run.fatbin
        Assert.Contains("MyKernel_Run.fatbin", wrapperSource);

        // Check that it's using the file-based loading approach
        Assert.Contains("System.IO.Path.Combine(System.AppContext.BaseDirectory", wrapperSource);
    }
}
