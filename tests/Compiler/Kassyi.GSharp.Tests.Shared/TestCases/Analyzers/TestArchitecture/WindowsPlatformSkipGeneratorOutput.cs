using System;
using Xunit;
using Kassyi.GSharp.Tests.Infrastructure;

// Stands for source generator output: the test compiles it at a *.g.cs path
public static class GeneratedPlatformHelpers
{
    public static void RequireWindows() => Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
}

[Category(TestCategory.Compiler)]
public class GeneratedSkipTests
{
    [SkippableFact]
    public void GeneratedWindowsSkip()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
    }
}
