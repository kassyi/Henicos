#nullable enable
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;
using System.Collections.Generic;
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
        Assert.Contains(generatedTrees, t => t.FilePath.Contains("MyKernel.g.cs"));

        var wrapperSource = generatedTrees.First(t => t.FilePath.Contains("MyKernel.g.cs")).ToString();

        // We unified naming, so it should expect GetFunction with mangled name
        Assert.Contains("GetFunction", wrapperSource);
        Assert.Contains("MyKernel_Run", wrapperSource);

        // Check that it's using the file-based loading approach via ModuleContext
        Assert.Contains("GetOrLoadModule()", wrapperSource);
    }

    [Fact]
    [Trait("FeatureId", "GEN-35")]
    [Trait("Category", "Transpiler")]
    public void Test_GEN35_AotConfiguration_ProducesPlaintextArtifactAndNoKey()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        try
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

            var additionalOptions = new Dictionary<string, string>
            {
                { "build_property.IntermediateOutputPath", "." },
                { "build_property.MSBuildProjectDirectory", tempDir }
            };

            var (_, _, _) = RoslynTestUtility.RunGenerator(sourceCode, "Cuda", additionalOptions);

            var expectedArtifact = System.IO.Path.Combine(tempDir, "Kassyi.GSharp.Kernels_MegaTestCompilationBase.g.cu");
            Assert.True(System.IO.File.Exists(expectedArtifact),
                $"Expected plaintext artifact {expectedArtifact} was not generated.");

            var keyArtifact = expectedArtifact + ".key";
            Assert.False(System.IO.File.Exists(keyArtifact),
                $"Key sidecar file {keyArtifact} should not be generated.");
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, true);
            }
        }
    }
}
