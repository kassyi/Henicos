#nullable enable
#pragma warning disable CS0649
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// The decision whether a call names a method of the compilation without a device definition, and its reason
/// (<c>UnemittedMethodCall.GetReason</c>, GSH0107), without running the transpiler.
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class UnemittedMethodCallTests
{
    // The first invocation of methodName in the body of the named method of this file
    private static (InvocationExpressionSyntax Call, IMethodSymbol Called, SemanticModel Model) FindCall(
        string callerName,
        string methodName,
        [CallerFilePath] string callerFilePath = "")
    {
        var (compilation, tree) = MegaCompilationCache.GetSourceContext(callerFilePath);
        var model = compilation.GetSemanticModel(tree);
        var call = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.ValueText == callerName)
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First(i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name } &&
                        name == methodName);
        var called = model.GetSymbolInfo(call).Symbol as IMethodSymbol ??
                     throw new InvalidOperationException($"{methodName} does not bind");
        return (call, called, model);
    }

    [Theory]
    [InlineData(nameof(UnemittedCallCases.CallsUnmarked), "Unmarked", "add [GpuDeviceFunction]")]
    [InlineData(nameof(UnemittedCallCases.CallsInstance), "Set", "instance methods of structs")]
    [InlineData(nameof(UnemittedCallCases.CallsThroughInterface), "Apply", "through an interface")]
    [Scenario(TestScenario.SadPath)]
    public void GetReason_UnemittedCallee_ReturnsDiagnosticReason(string caller, string method, string reason)
    {
        var (call, called, model) = FindCall(caller, method);

        var actual = UnemittedMethodCall.GetReason(called, call, model, new GSharpCudaDialect());

        Assert.NotNull(actual);
        Assert.Contains(reason, actual, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(UnemittedCallCases.CallsMarked), nameof(UnemittedCallHelpers.Marked))]
    [InlineData(nameof(UnemittedCallCases.CallsRemovedConditional), nameof(UnemittedCallHelpers.NeverTraced))]
    [InlineData(nameof(UnemittedCallCases.CallsMath), nameof(Math.Abs))]
    [Scenario(TestScenario.HappyPath)]
    public void GetReason_EmittedMappedOrRemovedCallee_ReturnsNull(string caller, string method)
    {
        var (call, called, model) = FindCall(caller, method);

        Assert.Null(UnemittedMethodCall.GetReason(called, call, model, new GSharpCudaDialect()));
    }
}

public interface IUnemittedCallOp
{
    int Apply(int x);
}

public struct UnemittedCallBox
{
    public int Value;

    public void Set(int v) => Value = v;
}

public static class UnemittedCallHelpers
{
    public static int Unmarked(int v) => v * 2;

    [GpuDeviceFunction]
    public static int Marked(int v) => v + 1;

    [System.Diagnostics.Conditional("UNEMITTED_CALL_NEVER_DEFINED")]
    public static void NeverTraced(int[] r) => r[0] = 1;
}

public static class UnemittedCallCases
{
    public static int CallsUnmarked(int v) => UnemittedCallHelpers.Unmarked(v);

    public static int CallsMarked(int v) => UnemittedCallHelpers.Marked(v);

    public static int CallsMath(int v) => Math.Abs(v);

    public static void CallsRemovedConditional(int[] r) => UnemittedCallHelpers.NeverTraced(r);

    public static void CallsInstance(ref UnemittedCallBox box) => box.Set(1);

    public static int CallsThroughInterface<T>(T op)
        where T : struct, IUnemittedCallOp => op.Apply(1);
}
