using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_95_StructInstanceMethodsSadPathTests
{
    [Fact]
    [Trait("FeatureId", "SYN-95")]
    [Trait("Category", "SadPath")]
    public void Test_SYN95_UnsupportedClass_ReportsGSH0001()
    {
        var diagnostics = MegaCompilationCache.GetGeneratorDiagnosticsForFile("SYN-95_StructInstanceMethodsSadPathKernels.cu.cs");
        diagnostics.AssertContainsError(GSharpDiagnostics.ObjectCreationNotSupported.Id);
    }

    [Fact]
    [Trait("FeatureId", "SYN-95")]
    [Trait("Category", "SadPath")]
    public void Test_SYN95_UnsupportedDynamicDispatch_ReportsGSH0001()
    {
        var diagnostics = MegaCompilationCache.GetGeneratorDiagnosticsForFile("SYN-95_StructInstanceMethodsSadPathKernels.cu.cs");
        diagnostics.AssertContainsError(GSharpDiagnostics.ObjectCreationNotSupported.Id);
    }

    [Fact]
    [Trait("FeatureId", "SYN-95")]
    [Trait("Category", "SadPath")]
    public void Test_SYN95_UnsupportedStringAndOverride_ReportsGSH0002()
    {
        var diagnostics = MegaCompilationCache.GetGeneratorDiagnosticsForFile("SYN-95_StructInstanceMethodsSadPathKernels.cu.cs");
        diagnostics.AssertContainsError(GSharpDiagnostics.StringNotSupported.Id);
    }
}

