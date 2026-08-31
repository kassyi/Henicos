using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers;

public class MEM_04_SharedMemoryDisposableSadPathTests
{
    [Fact]
    [Trait("FeatureId", "MEM-04")]
    [Trait("Category", "SadPath")]
    public void Validate_MEM04_ReportsDiagnostic_OnFieldWithoutDispose()
    {
        var diagnostics = MegaCompilationCache.GetAnalyzerDiagnosticsForFile("MEM-04_SharedMemoryDisposableSadPath.cs");
        diagnostics.AssertContainsError(GSharpDiagnostics.SharedMemoryDisposableNotSupported.Id);
    }

    [Fact]
    [Trait("FeatureId", "MEM-04")]
    [Trait("Category", "SadPath")]
    public void Validate_MEM04_ReportsDiagnostic_OnPropertyWithoutDispose()
    {
        var diagnostics = MegaCompilationCache.GetAnalyzerDiagnosticsForFile("MEM-04_SharedMemoryDisposableSadPath.cs");
        diagnostics.AssertContainsError(GSharpDiagnostics.SharedMemoryDisposableNotSupported.Id);
    }
}

