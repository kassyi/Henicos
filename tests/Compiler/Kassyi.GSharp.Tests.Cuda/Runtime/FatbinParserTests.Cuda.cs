#nullable enable
using System;
using System.IO;
using Kassyi.GSharp.Runtime.Fatbin;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public partial class FatbinParserTests
{
    [SkippableTheory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Kassyi.GSharp.Tests.Cuda")]
    [InlineData("Kassyi.GSharp.Stdlib.GpuMath")]
    public void BuiltFatbin_DefaultArchitectures_HoldsSass86AndPtx86(string moduleName)
    {
        var fileName = Kassyi.GSharp.Compiler.Marshaling.AotModuleNames.GetArtifactFileStem(moduleName) + ".gfatbin";
        // The default GSharpTargetArchitectures keeps the sm_86 SASS and adds compute_86 PTX, so that GPUs newer than
        // the 8.x family JIT-compile the module instead of failing to load it.
        // The build skips the fatbins only without nvcc or with GSHARP_TEST_NO_CUDA=1; otherwise a missing file is a failure
        Skip.If(Environment.GetEnvironmentVariable("GSHARP_TEST_NO_CUDA") == "1", "GSHARP_TEST_NO_CUDA=1 skips the fatbins.");
        Skip.IfNot(IsNvccOnPath(), "nvcc is not on PATH, so the build skipped the fatbins.");
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        Assert.True(File.Exists(path), $"'{path}' was not built.");

        bool parsed = FatbinParser.TryParse(File.ReadAllBytes(path), out FatbinImage? image);

        Assert.True(parsed);
        Assert.NotNull(image);
        Assert.Contains(image.Items, i => i.Type == FatbinItemInfo.TypeCubin && i.ComputeVersion == 86);
        Assert.Contains(image.Items, i => i.Type == FatbinItemInfo.TypePtx && i.ComputeVersion == 86);
        Assert.True(image.IsCompatibleWith(12, 0));
    }

    private static bool IsNvccOnPath()
    {
        var fileName = OperatingSystem.IsWindows() ? "nvcc.exe" : "nvcc";
        var path = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).AsSpan();
        foreach (var range in path.Split(Path.PathSeparator))
        {
            var directory = path[range].Trim('"');
            if (!directory.IsEmpty && File.Exists(Path.Combine(directory.ToString(), fileName)))
            {
                return true;
            }
        }

        return false;
    }
}
