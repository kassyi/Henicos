using System.Runtime.CompilerServices;
using VerifyXunit;

namespace Kassyi.GSharp.Stdlib.Math.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        Verifier.UseProjectRelativeDirectory("Snapshots");
    }
}
