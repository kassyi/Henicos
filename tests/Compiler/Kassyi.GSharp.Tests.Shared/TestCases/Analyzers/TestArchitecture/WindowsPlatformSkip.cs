using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Xunit;
using Kassyi.GSharp.Tests.Infrastructure;

[Category(TestCategory.Compiler)]
public class UnannotatedWindowsSkipTests
{
    [SkippableFact]
    public void SkipIfNotOperatingSystemIsWindows()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
    }

    [SkippableFact]
    public void SkipIfNotOperatingSystemIsWindowsCompoundAnd()
    {
        Skip.IfNot(
            OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "Windows x64 only.");
    }

    [SkippableFact]
    public void SkipIfNegatedOperatingSystemIsWindows()
    {
        Skip.If(!OperatingSystem.IsWindows(), "Windows only.");
    }

    [SkippableFact]
    public void SkipIfRuntimeInformationIsOSPlatformWindows()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows only.");
    }

    [SkippableFact]
    public void IfNotWindowsThrowSkipException()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new SkipException("Windows only.");
        }
    }

    [SkippableFact]
    public void IfWindowsElseSkip()
    {
        if (OperatingSystem.IsWindows())
        {
            _ = 1;
        }
        else
        {
            throw new SkipException("Windows only.");
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void SupportedOSPlatformWindowsMethod()
    {
    }

    [SkippableFact]
    public void HelperCallingWindowsSkip()
    {
        RequireWindowsHost();
    }

    [SkippableFact]
    [FakePlatform.Platform(FakePlatform.TestPlatform.Windows)]
    public void UserDefinedPlatformAttributeDoesNotSatisfy()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
    }

    [SkippableFact]
    [Platform(TestPlatform.Windows)]
    public void AnnotatedOnMethod()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
    }

    [SkippableFact]
    public void SkipIfOperatingSystemIsWindows()
    {
        Skip.If(OperatingSystem.IsWindows(), "Unix only.");
    }

    [SkippableFact]
    public void SkipIfNotOperatingSystemIsLinux()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only.");
    }

    [Fact]
    public void NonSkipConditionalBranch()
    {
        if (OperatingSystem.IsWindows())
        {
            _ = 1;
        }
        else
        {
            _ = 2;
        }
    }

    private static void RequireWindowsHost()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Helper requires Windows.");
    }
}

[Category(TestCategory.Compiler)]
[Platform(TestPlatform.Windows)]
public class ClassAnnotatedWindowsSkipTests
{
    [SkippableFact]
    public void AnnotatedOnClass()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
    }
}

[Category(TestCategory.Compiler)]
[Platform(TestPlatform.Windows)]
public class OuterAnnotated
{
    [Category(TestCategory.Compiler)]
    public class InnerUnannotated
    {
        [SkippableFact]
        public void NestedMethod()
        {
            Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
        }
    }
}

public class PlatformOnlyWithoutCategoryTests
{
    [SkippableFact]
    [Platform(TestPlatform.Windows)]
    public void PlatformOnlyStillNeedsCategory()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
    }
}

namespace FakePlatform
{
    public enum TestPlatform
    {
        Windows,
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class PlatformAttribute(TestPlatform platform) : Attribute
    {
        public TestPlatform Platform { get; } = platform;
    }
}
