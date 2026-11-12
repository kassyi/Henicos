#nullable enable
#pragma warning disable CS0649
using System.Collections.Immutable;
using System.Threading;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// The decision what a call in GPU code calls, and the text of its diagnostic (<c>CallTargetClassifier.Classify</c>,
/// GSH0107 and GSH0109), without running the transpiler.
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class CallTargetClassifierTests
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
            .First(i => i.Expression switch
            {
                MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name } => name == methodName,
                IdentifierNameSyntax { Identifier.ValueText: var name } => name == methodName,
                _ => false
            });
        var called = model.GetSymbolInfo(call).Symbol as IMethodSymbol ??
                     throw new InvalidOperationException($"{methodName} does not bind");
        return (call, called, model);
    }

    private static CallTargetPlan Classify(string caller, string method, ITranspilerDialect? dialect = null)
    {
        var (call, called, model) = FindCall(caller, method);
        return CallTargetClassifier.Classify(called, call, model, dialect ?? new GSharpCudaDialect());
    }

    [Theory]
    [InlineData(nameof(CallTargetCases.CallsMarked), "Marked", nameof(CallTargetKind.DeviceFunction))]
    [InlineData(nameof(CallTargetCases.CallsLibraryDevice), "GoldilocksPow", nameof(CallTargetKind.DeviceFunction))]
    [InlineData(nameof(CallTargetCases.CallsMath), "Abs", nameof(CallTargetKind.DialectIntrinsic))]
    [InlineData(nameof(CallTargetCases.CallsIntrinsic), "SyncThreads", nameof(CallTargetKind.DialectIntrinsic))]
    [InlineData(nameof(CallTargetCases.CallsInterlocked), "Add", nameof(CallTargetKind.BclIntrinsicMap))]
    [InlineData(nameof(CallTargetCases.CallsRemovedConditional), "NeverTraced", nameof(CallTargetKind.Removed))]
    [InlineData(nameof(CallTargetCases.CallsDelegate), "Invoke", nameof(CallTargetKind.NotOrdinary))]
    [InlineData(nameof(CallTargetCases.CallsDelegate), "f", nameof(CallTargetKind.NotOrdinary))]
    [InlineData(nameof(CallTargetCases.CallsLocalFunction), "Local", nameof(CallTargetKind.NotOrdinary))]
    [InlineData(nameof(CallTargetCases.CallsAsSpan), "AsSpan", nameof(CallTargetKind.ClaimedByRule))]
    [InlineData(nameof(CallTargetCases.CallsImmutableAsSpan), "AsSpan", nameof(CallTargetKind.ClaimedByRule))]
    [InlineData(nameof(CallTargetCases.CallsStackLinq), "Sum", nameof(CallTargetKind.ClaimedByRule))]
    [InlineData(nameof(CallTargetCases.CallsGetValueOrDefault), "GetValueOrDefault",
        nameof(CallTargetKind.ClaimedByRule))]
    [InlineData(nameof(CallTargetCases.CallsSingleThreadLambda), "SingleThread", nameof(CallTargetKind.ClaimedByRule))]
    [InlineData(nameof(CallTargetCases.CallsMemorySlice), "Slice", nameof(CallTargetKind.ArrayViewMember))]
    [InlineData(nameof(CallTargetCases.CallsSegmentCopyTo), "CopyTo", nameof(CallTargetKind.ArrayViewMember))]
    [Scenario(TestScenario.HappyPath)]
    public void Classify_EmittedMappedOrWrittenTarget_ReturnsItsKind(string caller, string method,
        string expected)
    {
        var plan = Classify(caller, method);

        Assert.Equal(expected, plan.Kind.ToString());
        Assert.False(plan.IsFailure);
    }

    [Theory]
    [InlineData(nameof(CallTargetCases.CallsUnmarked), "Unmarked", "add [GpuDeviceFunction]")]
    [InlineData(nameof(CallTargetCases.CallsInstance), "Set", "instance methods of structs")]
    [InlineData(nameof(CallTargetCases.CallsThroughInterface), "Apply", "through an interface")]
    [Scenario(TestScenario.SadPath)]
    public void Classify_ProjectMethodWithoutDeviceDefinition_ReturnsUnemittedWithReason(string caller, string method,
        string reason)
    {
        var plan = Classify(caller, method);

        Assert.Equal(CallTargetKind.Unemitted, plan.Kind);
        Assert.Equal("GSH0107", plan.Descriptor.Id);
        Assert.Contains(reason, plan.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(CallTargetCases.CallsIntMax), "Max", "int.Max(int, int)", "; use Math.Max(int, int) instead")]
    [InlineData(nameof(CallTargetCases.CallsFloatClamp), "Clamp", "float.Clamp(float, float, float)",
        "; use Math.Clamp(float, float, float) instead")]
    [InlineData(nameof(CallTargetCases.CallsLongMax), "Max", "Math.Max(long, long)",
        "; the dialect implements Math.Max(double, double), Math.Max(float, float), Math.Max(int, int)")]
    [InlineData(nameof(CallTargetCases.CallsCbrt), "Cbrt", "MathF.Cbrt(float)", "")]
    [InlineData(nameof(CallTargetCases.CallsImmutableClear), "Clear", "ImmutableArray<T>.Clear()", "")]
    [Scenario(TestScenario.SadPath)]
    public void Classify_LibraryMethodWithoutMapping_ReturnsUnmappedWithAlternative(string caller, string method,
        string display, string detail)
    {
        var plan = Classify(caller, method, new StandardCppDialect());

        Assert.Equal(CallTargetKind.Unmapped, plan.Kind);
        Assert.Equal("GSH0109", plan.Descriptor.Id);
        Assert.Equal(new object[] { display, "StandardCppDialect", detail }, plan.MessageArgs);
    }

    [Theory]
    [InlineData(nameof(CallTargetCases.CallsSingleThreadMethodGroup), "CallTargetHelpers.Body",
        "Gpu.SingleThread with a body that is not a lambda (a Gpu macro body must be a lambda)")]
    [InlineData(nameof(CallTargetCases.CallsSingleThreadInExpression),
        "Gpu.SingleThread(() => CallTargetHelpers.Body())",
        "Gpu.SingleThread outside a statement (call it as a statement of its own)")]
    [Scenario(TestScenario.SadPath)]
    public void Classify_MacroWhereNoRuleWritesIt_ReturnsMisusedMacroAtTheForm(string caller, string reportedAt,
        string syntax)
    {
        var plan = Classify(caller, nameof(Gpu.SingleThread));

        Assert.Equal(CallTargetKind.MisusedMacro, plan.Kind);
        Assert.Equal("GSH0003", plan.Descriptor.Id);
        Assert.Equal(new object[] { syntax }, plan.MessageArgs);
        Assert.Equal(reportedAt, plan.ReportAt?.ToString());
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Descriptor_NotAFailure_Throws()
    {
        var plan = Classify(nameof(CallTargetCases.CallsMarked), nameof(CallTargetHelpers.Marked));

        Assert.Throws<InvalidOperationException>(() => plan.Descriptor);
    }
}

public interface ICallTargetOp
{
    int Apply(int x);
}

public struct CallTargetBox
{
    public int Value;

    public void Set(int v) => Value = v;
}

public static class CallTargetHelpers
{
    public static int Unmarked(int v) => v * 2;

    [GpuDeviceFunction]
    public static int Marked(int v) => v + 1;

    [System.Diagnostics.Conditional("CALL_TARGET_NEVER_DEFINED")]
    public static void NeverTraced(int[] r) => r[0] = 1;

    [GpuDeviceFunction]
    public static void Body()
    {
    }
}

public static class CallTargetCases
{
    public static int CallsUnmarked(int v) => CallTargetHelpers.Unmarked(v);

    public static int CallsMarked(int v) => CallTargetHelpers.Marked(v);

    public static ulong CallsLibraryDevice(ulong v) =>
        Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels.GoldilocksArithmetic.GoldilocksPow(v, 2UL);

    public static int CallsMath(int v) => Math.Abs(v);

    public static void CallsIntrinsic() => Gpu.SyncThreads();

    public static int CallsInterlocked(int[] counter) => Interlocked.Add(ref counter[0], 1);

    public static void CallsRemovedConditional(int[] r) => CallTargetHelpers.NeverTraced(r);

    public static int CallsDelegate(Func<int, int> f, int v) => f.Invoke(v) + f(v);

    public static int CallsLocalFunction(int v)
    {
        return Local(v);

        static int Local(int x) => x + 1;
    }

    public static int CallsAsSpan(int[] a) => a.AsSpan().Length;

    public static int CallsImmutableAsSpan(ImmutableArray<int> a) => a.AsSpan().Length;

    public static int CallsStackLinq(int[] a) => a.AsStackEnumerable().Sum();

    public static int CallsGetValueOrDefault(int? v) => v.GetValueOrDefault();

    public static void CallsSingleThreadLambda()
    {
        Gpu.SingleThread(() => CallTargetHelpers.Body());
    }

    public static void CallsSingleThreadMethodGroup()
    {
        Gpu.SingleThread(CallTargetHelpers.Body);
    }

    public static void CallsSingleThreadInExpression() => Gpu.SingleThread(() => CallTargetHelpers.Body());

    public static int CallsMemorySlice(Memory<int> m) => m.Slice(1, 2).Length + m.Slice(1).Length;

    public static int CallsSegmentCopyTo(ArraySegment<int> s, int[] a)
    {
        s.CopyTo(a);
        return a.Length;
    }

    public static ImmutableArray<int> CallsImmutableClear(ImmutableArray<int> a) => a.Clear();

    public static void CallsInstance(ref CallTargetBox box) => box.Set(1);

    public static int CallsThroughInterface<T>(T op)
        where T : struct, ICallTargetOp => op.Apply(1);

    public static int CallsIntMax(int a) => int.Max(a, 3);

    public static float CallsFloatClamp(float a) => float.Clamp(a, 0f, 1f);

    public static long CallsLongMax(long a) => Math.Max(a, 3L);

    public static float CallsCbrt(float a) => MathF.Cbrt(a);
}
