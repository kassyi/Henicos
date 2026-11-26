using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
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

// The forms that lose their meaning on a Linux worker, beyond Skip.IfNot(OperatingSystem.IsWindows())
[Category(TestCategory.Compiler)]
public class WidenedWindowsSkipTests
{
    private static bool IsWindowsHost => OperatingSystem.IsWindows();

    private static bool IsWindowsIndirect => IsWindowsHost;

    [SkippableFact]
    public void SkipIfOperatingSystemIsLinux()
    {
        Skip.If(OperatingSystem.IsLinux(), "Not on Linux.");
    }

    [SkippableFact]
    public void SkipIfNotIsOSPlatformWindowsString()
    {
        Skip.IfNot(OperatingSystem.IsOSPlatform("WINDOWS"), "Windows only.");
    }

    [SkippableFact]
    public void SkipIfNotRuntimeLinuxIsFalse()
    {
        Skip.If(!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) == false, "Not on Linux.");
    }

    [SkippableFact]
    public void SkipIfNotOSVersionWin32NT()
    {
        Skip.IfNot(Environment.OSVersion.Platform == PlatformID.Win32NT, "Windows only.");
    }

    [SkippableFact]
    public void SkipIfOSVersionNotWin32NT()
    {
        Skip.If(PlatformID.Win32NT != Environment.OSVersion.Platform, "Windows only.");
    }

    [SkippableFact]
    public void SkipIfNotWindowsLocal()
    {
        var isWindows = OperatingSystem.IsWindows();
        Skip.IfNot(isWindows, "Windows only.");
    }

    [SkippableFact]
    public void SkipIfNotWindowsProperty()
    {
        Skip.IfNot(IsWindowsHost, "Windows only.");
    }

    [SkippableFact]
    public void SkipIfNotWindowsMethodHelper()
    {
        Skip.IfNot(IsWindowsMachine(), "Windows only.");
    }

    [SkippableFact]
    public void SkipIfNotWindowsHelperOnX64()
    {
        Skip.IfNot(IsWindowsHost && RuntimeInformation.ProcessArchitecture == Architecture.X64, "Windows x64 only.");
    }

    [SkippableFact]
    public void SkipIfNeitherWindowsNorMacOS()
    {
        Skip.If(!(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()), "Windows or macOS only.");
    }

    [Fact]
    public void ReturnIfNotWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        _ = 1;
    }

    [Fact]
    public void ReturnIfLinuxInBlock()
    {
        if (OperatingSystem.IsLinux())
        {
            return;
        }

        _ = 1;
    }

    [Fact]
    public void ReturnInElseOfWindowsHelper()
    {
        if (IsWindowsHost)
        {
            _ = 1;
        }
        else
        {
            return;
        }
    }

    [Fact]
    public void ReturnThroughHelper()
    {
        RunOnWindowsOnly();
    }

    [Fact]
    public async Task ReturnIfNotWindowsAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await Task.Yield();
    }

    [SkippableFact]
    public void SkipThroughOverride()
    {
        PlatformProbe probe = new WindowsProbe();
        probe.Require();
    }

    [SkippableFact]
    public void SkipThroughInterface()
    {
        IHostCheck check = new WindowsHostCheck();
        check.Require();
    }

    [SkippableFact]
    public void SkipThroughExplicitInterface()
    {
        IExplicitHostCheck check = new ExplicitWindowsHostCheck();
        check.Require();
    }

    [SkippableFact]
    public void SkipIfTrueLiteral()
    {
        Skip.If(true, "Always skipped.");
    }

    [SkippableFact]
    public void SkipIfNotReassignedLocal()
    {
        var isWindows = OperatingSystem.IsWindows();
        isWindows = true;
        Skip.IfNot(isWindows, "Windows only.");
    }

    [SkippableFact]
    public void SkipIfNotTwoLevelHelper()
    {
        Skip.IfNot(IsWindowsIndirect, "Windows only.");
    }

    [SkippableFact]
    public void SkipIfNotWindowsOnArm()
    {
        Skip.If(!OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64, "Not on Arm.");
    }

    [SkippableFact]
    public void SkipIfOSVersionWin32NT()
    {
        Skip.If(Environment.OSVersion.Platform == PlatformID.Win32NT, "Unix only.");
    }

    [SkippableFact]
    public void SkipIfNotIsOSPlatformLinuxString()
    {
        Skip.IfNot(OperatingSystem.IsOSPlatform("Linux"), "Linux only.");
    }

    [Fact]
    public void ReturnIfWindows()
    {
        if (OperatingSystem.IsWindows()) return;
        _ = 1;
    }

    [Fact]
    public void ReturnAfterWorkIfNotWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            _ = 2;
            return;
        }

        _ = 1;
    }

    [Fact]
    public void ReturnInsideLambda()
    {
        Action action = () =>
        {
            if (!OperatingSystem.IsWindows()) return;
            _ = 1;
        };
        action();
    }

    [Fact]
    public void BoolHelperReturningFalse()
    {
        if (HasWindowsTool())
        {
            _ = 1;
        }
    }

    private static bool IsWindowsMachine() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static void RunOnWindowsOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _ = 1;
    }

    private static bool HasWindowsTool()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return true;
    }
}

public abstract class PlatformProbe
{
    public abstract void Require();
}

public sealed class WindowsProbe : PlatformProbe
{
    public override void Require() => Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
}

public interface IHostCheck
{
    void Require();
}

public sealed class WindowsHostCheck : IHostCheck
{
    public void Require() => Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
}

public interface IExplicitHostCheck
{
    void Require();
}

public sealed class ExplicitWindowsHostCheck : IExplicitHostCheck
{
    void IExplicitHostCheck.Require() => Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
}

// A skip in a constructor skips every test of the class
[Category(TestCategory.Compiler)]
public class ConstructorSkipTests
{
    public ConstructorSkipTests()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
    }

    [SkippableFact]
    public void FirstTestOfSkippingClass()
    {
    }

    [SkippableFact]
    public void SecondTestOfSkippingClass()
    {
    }
}

[Category(TestCategory.Compiler)]
public class StaticConstructorReturnTests
{
    private static readonly int s_value;

    static StaticConstructorReturnTests()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        s_value = 1;
    }

    [Fact]
    public void TestOfStaticConstructorClass()
    {
        _ = s_value;
    }
}

[Category(TestCategory.Compiler)]
[Platform(TestPlatform.Windows)]
public class AnnotatedConstructorSkipTests
{
    public AnnotatedConstructorSkipTests()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
    }

    [SkippableFact]
    public void TestOfAnnotatedSkippingClass()
    {
    }
}

// xUnit runs an inherited test with the traits of the concrete class, never with those of the base
[Category(TestCategory.Compiler)]
public abstract class InheritedSkipTestsBase
{
    [SkippableFact]
    public void InheritedWindowsSkip()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
    }

    [Fact]
    public void InheritedPlainTest()
    {
    }
}

[Category(TestCategory.Compiler)]
[Platform(TestPlatform.Windows)]
public class AnnotatedDerivedTests : InheritedSkipTestsBase
{
}

[Category(TestCategory.Compiler)]
public class UnannotatedDerivedTests : InheritedSkipTestsBase
{
}

[Category(TestCategory.Compiler)]
public class ConstructorSkipDerivedTests : InheritedSkipTestsBase
{
    public ConstructorSkipDerivedTests()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
    }
}

[Category(TestCategory.Compiler)]
public abstract class AllDerivedAnnotatedBase
{
    [SkippableFact]
    public void SkipCoveredByEveryDerivedClass()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
    }
}

[Category(TestCategory.Compiler)]
[Platform(TestPlatform.Windows)]
public class OnlyAnnotatedDerivedTests : AllDerivedAnnotatedBase
{
}

[Category(TestCategory.Compiler)]
[Platform(TestPlatform.Windows)]
public abstract class BaseOnlyAnnotatedTests
{
    [SkippableFact]
    public void SkipAnnotatedOnlyOnBase()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
    }
}

[Category(TestCategory.Compiler)]
public class BaseOnlyAnnotatedDerivedTests : BaseOnlyAnnotatedTests
{
}

[Category(TestCategory.Compiler)]
public abstract class GenericSkipTestsBase<T>
{
    [SkippableFact]
    public void GenericInheritedSkip()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
    }
}

[Category(TestCategory.Compiler)]
public class GenericSkipDerivedTests : GenericSkipTestsBase<int>
{
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
