#nullable enable
using Microsoft.CodeAnalysis.Diagnostics;
using VerifyTests;
using VerifyXunit;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

public abstract class TranspilerTestBase
{
    protected ITranspilerDialect CurrentDialect { get; } =
#if GSHARP_CUDA
        new GSharpCudaDialect();
#elif GSHARP_CPU
        new StandardCppDialect();
#else
        new StandardCppDialect();
#endif

    protected bool IsCudaDialect => CurrentDialect.IsCuda;
    protected bool IsCpuDialect => !CurrentDialect.IsCuda;

    protected void SkipIfCpuDialect(string reason = "This test requires GPU hardware features and cannot run on CPU.")
    {
        Xunit.Skip.If(IsCpuDialect, reason);
    }

    protected void EnsureCapability(DialectCapabilities required)
    {
        Skip.IfNot(CurrentDialect.Capabilities.HasFlag(required), $"Current dialect does not support {required}");
    }

    protected string DialectName => IsCudaDialect ? "Cuda" : "Cpu";

    protected GeneratorTestConfig TestGenerator(string relativePathToTestCases = "",
        [CallerFilePath] string callerFilePath = "")
    {
        if (string.IsNullOrEmpty(relativePathToTestCases))
        {
            var resolvedFile = RoslynTestUtility.ResolveKernelFilePath(callerFilePath);
            // By-pass GetTestCasesDirectory since we already have absolute path
            return new GeneratorTestConfig(resolvedFile).WithDialect(DialectName);
        }

        return RoslynTestUtility.TestGenerator(relativePathToTestCases, callerFilePath)
            .WithDialect(DialectName);
    }

    protected AnalyzerTestConfig TestAnalyzer(string relativePathToTestCases,
        DiagnosticAnalyzer analyzer, [CallerFilePath] string callerFilePath = "")
    {
        return RoslynTestUtility.TestAnalyzer(relativePathToTestCases, analyzer, callerFilePath)
            .WithDialect(DialectName);
    }

    protected SettingsTask Verify(object? target,
        [CallerMemberName] string methodName = "")
    {
        return Verifier.Verify(target).UseFastHashSnapshotName(methodName, DialectName);
    }

    protected SettingsTask Verify(string target,
        [CallerMemberName] string methodName = "")
    {
        return Verifier.Verify(target, "cs").UseFastHashSnapshotName(methodName, DialectName);
    }

    protected SettingsTask Verify(SyntaxNode target,
        [CallerMemberName] string methodName = "")
    {
        return Verifier.Verify(target.ToFullString(), "cs").UseFastHashSnapshotName(methodName, DialectName);
    }

    /// <summary>
    /// A convenient one-liner to generate and verify a source generator's output.
    /// Combines TestGenerator(path).GetDriverAsync() and Verify(driver).
    /// </summary>
    /// <remarks>
    /// Verify compares the snapshot only when the <see cref="SettingsTask"/> itself is awaited, so it is awaited here
    /// rather than returned (#505).
    /// </remarks>
    protected async Task VerifyGeneratorAsync(
        string relativePathToTestCases = "",
        [CallerFilePath] string callerFilePath = "",
        [CallerMemberName] string methodName = "")
    {
        var config = TestGenerator(relativePathToTestCases, callerFilePath);
        var driver = await config.GetDriverAsync();
        await Verify(driver, methodName);
    }
}

