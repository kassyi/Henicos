using System.Runtime.CompilerServices;
using VerifyTests;
using VerifyXunit;

namespace Kassyi.GSharp.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifySourceGenerators.Initialize();
        Verifier.UseProjectRelativeDirectory("Snapshots");
    }
}
