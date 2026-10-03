#nullable enable
using Microsoft.CodeAnalysis.Diagnostics;

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

    protected GeneratorTestConfig TestGenerator(string relativePathToTestCases,
        [CallerFilePath] string callerFilePath = "")
    {
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
}

