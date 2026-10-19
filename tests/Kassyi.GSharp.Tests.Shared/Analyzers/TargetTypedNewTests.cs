using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public class TargetTypedNewTests : TranspilerTestBase
{
    private static readonly AllocationAnalyzer s_allocationAnalyzer = new();
    private static readonly SideEffectAnalyzer s_sideEffectAnalyzer = new();

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task StaticDisposableAllocation_TargetTypedNew_Detected()
    {
        const string source = """
            using System;

            public class DummyDisposable : IDisposable
            {
                public void Dispose() {}
            }

            public static class TargetTypedStaticContainer
            {
                public static readonly DummyDisposable GlobalCtx = new();
            }
            """;

        var diagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, s_allocationAnalyzer);
        var diag = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.StaticDisposableAllocation.Id, diag.Id);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task UnguardedMultiAllocation_TargetTypedNew_Detected()
    {
        const string source = """
            using System;

            public class DummyDisposable : IDisposable
            {
                public void Dispose() {}
            }

            public class MultiAllocContainer
            {
                private DummyDisposable _c1;
                private DummyDisposable _c2;

                public MultiAllocContainer()
                {
                    _c1 = new();
                    _c2 = new();
                }
            }
            """;

        var diagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, s_allocationAnalyzer);
        var diag = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.UnguardedMultiAllocation.Id, diag.Id);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task PropertyGetterSideEffect_TargetTypedNew_Detected()
    {
        const string source = """
            using System;

            public class DummyDisposable : IDisposable
            {
                public void Dispose() {}
            }

            public class PropertyGetterContainer
            {
                public DummyDisposable Context => new();
            }
            """;

        var diagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, s_sideEffectAnalyzer);
        var diag = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.PropertyGetterSideEffect.Id, diag.Id);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task CastOperatorSideEffect_TargetTypedNew_Detected()
    {
        const string source = """
            using System;

            public class DummyDisposable : IDisposable
            {
                public void Dispose() {}
            }

            public class MyWrapper
            {
                public static implicit operator DummyDisposable(MyWrapper w) => new();
            }
            """;

        var diagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, s_sideEffectAnalyzer);
        var diag = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.CastOperatorSideEffect.Id, diag.Id);
    }
}
