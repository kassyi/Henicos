#nullable enable
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

    protected GeneratorTestConfig TestGenerator(string relativePathToTestCases, [System.Runtime.CompilerServices.CallerFilePath] string callerFilePath = "")
    {
        return RoslynTestUtility.TestGenerator(relativePathToTestCases, callerFilePath)
                                .WithDialect(DialectName);
    }

    protected AnalyzerTestConfig TestAnalyzer(string relativePathToTestCases, Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer analyzer, [System.Runtime.CompilerServices.CallerFilePath] string callerFilePath = "")
    {
        return RoslynTestUtility.TestAnalyzer(relativePathToTestCases, analyzer, callerFilePath)
                                .WithDialect(DialectName);
    }

    protected VerifyTests.SettingsTask Verify(object? target, [System.Runtime.CompilerServices.CallerMemberName] string methodName = "")
    {
        var prefixParts = methodName.Split('_');
        var prefix = prefixParts.Length > 1 ? prefixParts[1] : "Test";
        var hash10 = HashHelper.ComputeShortHash(methodName);
        var snapshotName = $"Snap.{prefix}_{hash10}_{DialectName}";
        return VerifyXunit.Verifier.Verify(target).UseFileName(snapshotName);
    }
}

