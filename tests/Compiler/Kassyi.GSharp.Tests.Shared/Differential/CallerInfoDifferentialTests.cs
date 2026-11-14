#nullable enable
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for a caller-info parameter that a call in GPU code omits: it receives the line of the call site,
/// as C# passes it, on every path (#946).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.HappyPath)]
public class CallerInfoDifferentialTests
{
    private const int Rows = 128;

    // The lines of the calls in CallerLineManagedVersionKernel and CallerLineSuspendingCallKernel, read from the kernel
    // file
    private static readonly int s_managedVersionCallLine =
        LineOfCall(nameof(CallerInfoDifferentialKernels.LineWithSum));

    private static readonly int s_suspendingCallLine =
        LineOfCall(nameof(CallerInfoDifferentialKernels.LineAcrossBarrier));

    internal static readonly DifferentialCase s_callerLine = new(
        nameof(CallerInfoDifferentialKernels) + "." + nameof(CallerInfoDifferentialKernels.CallerLineKernel))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, _) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .Out<int>("r", rows, CallerInfoDifferentialKernels.Columns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => CallerInfoDifferentialKernels.LaunchCallerLineKernel(
            b.Get<int>("xs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    // The oracle cannot run a float Sum as C#; the reference is LineWithSum(data) called at s_managedVersionCallLine
    internal static readonly DifferentialCase s_managedVersion = new(
        nameof(CallerInfoDifferentialKernels) + "." +
        nameof(CallerInfoDifferentialKernels.CallerLineManagedVersionKernel))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("data", DiffValues.Int32Pairs(rng, 4).Item1.Select(static v => (float)(v & 0xFF)).ToArray())
            .Out<int>("r", rows, OutputPolicy.Exact),
        Launch = static (b, s) => CallerInfoDifferentialKernels.LaunchCallerLineManagedVersionKernel(
            b.Get<float>("data"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, _) => Array.Fill(b.Get<int>("r"), s_managedVersionCallLine),
    };

    // The oracle cannot run barriers; the reference is LineAcrossBarrier(x) called at s_suspendingCallLine
    internal static readonly DifferentialCase s_suspendingCall = new(
        nameof(CallerInfoDifferentialKernels) + "." +
        nameof(CallerInfoDifferentialKernels.CallerLineSuspendingCallKernel))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, _) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .Out<int>("r", rows, OutputPolicy.Exact);
        },
        Launch = static (b, s) => CallerInfoDifferentialKernels.LaunchCallerLineSuspendingCallKernel(
            b.Get<int>("xs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, _) =>
        {
            var xs = b.Get<int>("xs");
            var r = b.Get<int>("r");
            for (int tid = 0; tid < xs.Length; tid++)
            {
                r[tid] = ((xs[tid] & 0x3FF) * 1000) + s_suspendingCallLine;
            }
        },
    };

    internal static readonly DifferentialCase s_iterator = new(
        nameof(CallerInfoDifferentialKernels) + "." + nameof(CallerInfoDifferentialKernels.CallerLineIteratorKernel))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, _) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .Out<int>("r", rows, OutputPolicy.Exact);
        },
        Launch = static (b, s) => CallerInfoDifferentialKernels.LaunchCallerLineIteratorKernel(
            b.Get<int>("xs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    [SkippableFact]
    public void OmittedCallerLineNumber_CallsInGpuCode_ReceiveTheLineOfTheCallOnEveryPath() =>
        DifferentialRunner.Run(s_callerLine);

    [SkippableFact]
    public void OmittedCallerLineNumber_ManagedVersion_ReceivesTheLineOfTheCallOnEveryPath() =>
        DifferentialRunner.Run(s_managedVersion);

    [SkippableFact]
    public void OmittedCallerLineNumber_SuspendingCall_ReceivesTheLineOfTheCallOnEveryPath() =>
        DifferentialRunner.Run(s_suspendingCall);

    [SkippableFact]
    public void OmittedCallerLineNumber_IteratorCall_ReceivesTheLineOfTheCallOnEveryPath() =>
        DifferentialRunner.Run(s_iterator);

    // The line C# passes for the call of <method> in the kernels file: the line of its opening parenthesis
    private static int LineOfCall(string method)
    {
        var (_, tree, _) = RoslynTestUtility.GetKernelContext();
        var call = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(c => c.Expression is IdentifierNameSyntax name && name.Identifier.ValueText == method);
        return tree.GetMappedLineSpan(call.ArgumentList.OpenParenToken.Span).StartLinePosition.Line + 1;
    }
}
