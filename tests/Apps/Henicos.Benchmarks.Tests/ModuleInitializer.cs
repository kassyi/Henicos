using System.Runtime.CompilerServices;

namespace Henicos.Benchmarks.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize() => TestConsole.Silence();
}
