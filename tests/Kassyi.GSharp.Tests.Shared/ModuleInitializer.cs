using VerifyTests;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Kassyi.GSharp.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        System.Environment.SetEnvironmentVariable("DiffEngine_Disabled", "true");
        DiffEngine.DiffRunner.Disabled = true;
        VerifySourceGenerators.Initialize();
        Verifier.UseProjectRelativeDirectory("Snapshots");
    }
}
