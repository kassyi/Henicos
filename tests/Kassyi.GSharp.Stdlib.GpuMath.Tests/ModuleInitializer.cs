using System.Runtime.CompilerServices;
using VerifyXunit;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        Environment.SetEnvironmentVariable("DiffEngine_Disabled", "true");
        DiffEngine.DiffRunner.Disabled = true;
        Verifier.UseProjectRelativeDirectory("Snapshots");
    }
}
