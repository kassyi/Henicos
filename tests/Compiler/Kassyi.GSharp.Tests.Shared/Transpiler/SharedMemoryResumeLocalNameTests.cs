#nullable enable
using System.Linq;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// The resume local of a hoisted <c>SharedMemory&lt;T&gt;</c> is declared at the top of the managed state function, so
/// it gets a name of its own when a nested scope of the body declares a local of the same name.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class SharedMemoryResumeLocalNameTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SiblingScopes_LocalDeclarations_ManagedCSharpCompilesWithoutErrors()
    {
        var source = RoslynTestUtility.ReadTestCase("Transpiler/SharedMemorySiblingScopeHoistKernels.cu.cs");

        var (_, outputCompilation, _) = GeneratorTestRunner.RunGenerator(source, "Cpu");
        var errors = outputCompilation.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.Empty(errors);
    }
}
