using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// A <c>ref</c> local that no state after a split reads stays a local of the state that declares it (#1640): the lowering
/// leaves it out of the frame, and the managed state machine of the CPU dialect runs it as C# does.
/// </summary>
/// <remarks>
/// The kernels run from an in-memory build of the generated managed C#: the C++ of both dialects does not translate a
/// ref local yet (GSH0003 at the <c>ref</c> initializer), so a project with these kernels does not build.
/// </remarks>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.GlobalStateSequential)]
public class RefLocalAcrossSplitTests
{
    private const string TestCase = "Diagnostics/RefLocalAcrossSplit.cu.cs";
    private const string KernelsType = "Kassyi.GSharp.Tests.TestCases.Diagnostics.RefLocalAcrossSplitKernels";
    private const int ThreadCount = 4;

    private static readonly Lazy<Assembly> s_managedAssembly = new(CompileManagedAssembly);

    [Theory]
    [InlineData("UsedBeforeSyncThreads")]
    [InlineData("UsedBeforeSyncThreadsInLoop")]
    [InlineData("HeldAcrossSyncThreads")]
    [InlineData("HeldInDeviceFunction")]
    [InlineData("ReboundAfterSyncThreads")]
    [Scenario(TestScenario.HappyPath)]
    public void Lower_RefLocal_HoistedOnlyWhenAStateAfterTheSplitNamesIt(string method)
    {
        var fields = LowerContextFields(method);

        // mutant: RefLocalLiveness.FindNotLiveAcrossSplit `if (!live[i])` -> `if (live[i])` -> the held locals stay
        // locals and the others are hoisted
        var hoistsAlias = method is "HeldAcrossSyncThreads" or "HeldInDeviceFunction" or "ReboundAfterSyncThreads";
        Assert.Equal(hoistsAlias, fields.Contains("alias"));
        Assert.Contains(method == "ReboundAfterSyncThreads" ? "x" : "w", fields);
    }

    [Theory]
    [InlineData("RunUsedBeforeSyncThreads", new[] { 11, 21, 31, 41 })]
    [InlineData("RunUsedBeforeSyncThreadsInLoop", new[] { 12, 22, 32, 42 })]
    [InlineData("RunUsedInBranchBeforeSyncThreads", new[] { 11, 20, 31, 40 })]
    [InlineData("RunCallsUsedBeforeSyncThreadsInDeviceFunction", new[] { 11, 21, 31, 41 })]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void ManagedStateMachine_RefLocalUsedBeforeTheSplit_WritesThroughTheAlias(string run, int[] expected)
    {
        int[] r = [10, 20, 30, 40];

        Invoke(run, r);

        Assert.Equal(expected, r);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.SadPath)]
    public void ManagedStateMachine_RefLocalHeldAcrossTheSplit_Throws()
    {
        int[] r = [10, 20, 30, 40];

        var thrown = Assert.Throws<TargetInvocationException>(() => Invoke("RunHeldAcrossSyncThreads", r));

        var inner = Assert.IsType<NotSupportedException>(thrown.InnerException);
        Assert.Contains("'alias'", inner.Message, StringComparison.Ordinal);
        Assert.Equal([10, 20, 30, 40], r);
    }

    private static void Invoke(string run, int[] r)
    {
        var kernels = s_managedAssembly.Value.GetType(KernelsType, throwOnError: true);
        Assert.NotNull(kernels);
        var method = kernels.GetMethod(run, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            method.Invoke(null, [r, (uint)ThreadCount]);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
    }

    // The generated managed C# of the test case, built in memory; the generator's GSH0003 for the C++ of the ref
    // locals does not stop the managed build
    private static Assembly CompileManagedAssembly()
    {
        var (_, output, _) = RoslynTestUtility.RunGenerator(RoslynTestUtility.ReadTestCase(TestCase), "Cpu");
        using var stream = new MemoryStream();
        var result = output.Emit(stream);
        Assert.True(
            result.Success,
            string.Join("\n", result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));

        stream.Position = 0;
        return new AssemblyLoadContext($"RefLocalAcrossSplit_{Guid.NewGuid():N}", isCollectible: true)
            .LoadFromStream(stream);
    }

    private static string[] LowerContextFields(string methodName)
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(TestCase);
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == methodName);
        var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method);
        var isDeviceFunction = methodName == "HeldInDeviceFunction";
        var lowered = AstLoweringPipeline.Lower(method, model, symbol, isDeviceFunction);
        return lowered.ContextFields.Select(static f => f.Identifier.ValueText).ToArray();
    }
}
