using Kassyi.GSharp.Compiler.Backends.Cpp;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Emits the warp collectives of the native stack model (<c>GSharpNativeBarrierModel=Stack</c>): the visit counts that
/// a kernel keeps on the stack of each GPU thread, the hidden frame parameter and argument of the device functions that
/// reach a collective, and the calls of the prelude functions that run a collective (<c>StackThreads.hpp</c>).
/// </summary>
/// <remarks>
/// [WHY] The lanes of a warp meet at the same instance of a collective as they do in the state machine: the static call
/// path of its frame, its site, and how often the frame reached the site. The frames are laid out at compile time
/// (<see cref="WarpFrameLayout"/>), so the path is a constant offset that each call passes down, and the counts are one
/// array of the kernel. <see cref="NativeStackEligibility"/> keeps a method whose collectives have no place on the state
/// machine; a place that is missing here is a compiler bug, reported as an internal error rather than written as 0.
/// </remarks>
internal static class StackWarpEmitter
{
    /// <summary>
    /// Whether <paramref name="method"/> is written for <paramref name="dialect"/> with the hidden frame: the native
    /// stack form of a method that reaches a warp collective.
    /// </summary>
    public static bool TakesFrame(IMethodSymbol method, ITranspilerDialect dialect, Compilation compilation) =>
        dialect.RunsThreadsOnOwnStacks() && SuspensionAnalysis.ReachesWarpShuffleOnStack(method, compilation);

    /// <summary>
    /// The frame of the method of <paramref name="plan"/> when its native stack form reaches a warp collective, or
    /// <see langword="null"/> for any other method or dialect.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The method has no layout, or one larger than <see cref="WarpFrameLayout.MaxSize"/>: the eligibility check should
    /// have kept it on the state machine.
    /// </exception>
    public static StackWarpFrame? GetFrame(in MethodEmissionPlan plan, ITranspilerDialect dialect)
    {
        var compilation = plan.SemanticModel.Compilation;
        if (plan.Symbol is not { } method || !TakesFrame(method, dialect, compilation))
        {
            return null;
        }

        if (!WarpFrameLayout.TryGet(method, compilation, out var layout))
        {
            throw InternalError(
                $"{MessageText.Name(method)} reaches a warp collective that has no place in the visit counts");
        }

        if (layout.Size > WarpFrameLayout.MaxSize)
        {
            throw InternalError(
                $"{MessageText.Name(method)} needs {layout.Size} visit counts, more than the " +
                $"{WarpFrameLayout.MaxSize} of the stack model");
        }

        var treeOrdinal = SemanticModelExtensions.GetSyntaxTreeOrdinal(compilation, plan.Declaration.SyntaxTree);
        return new StackWarpFrame(layout, treeOrdinal);
    }

    /// <summary>
    /// The body that the native stack form of the method of <paramref name="plan"/> writes in place of its declaration
    /// when it reaches <c>Warp.ActiveMask()</c>: the stack body, with the reconvergence regions where the state machine
    /// puts them (<see cref="MethodEmissionPlan.StackBody"/>); <see langword="null"/> for any other method or dialect.
    /// </summary>
    /// <remarks>
    /// [WHY] <c>Warp.ActiveMask()</c> returns the lanes that reach it together, so the lanes that took different ways
    /// through a thread-dependent branch must meet where it ends, as the state machine makes them; the regions are
    /// placed on the body after the passes that turn <c>&amp;&amp;</c>, <c>||</c> and <c>?:</c> into statements, which the
    /// declaration does not show.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The method reaches <c>Warp.ActiveMask()</c> but has no stack body (a compiler bug: every kernel with a body and
    /// every device function that reaches a warp collective is lowered).
    /// </exception>
    public static BlockSyntax? GetActiveMaskBody(in MethodEmissionPlan plan, ITranspilerDialect dialect)
    {
        if (plan.Symbol is not { } method || !dialect.RunsThreadsOnOwnStacks() ||
            !SuspensionAnalysis.ReachesActiveMaskOnStack(method, plan.SemanticModel.Compilation))
        {
            return null;
        }

        return plan.StackBody?.Body ?? throw InternalError(
            $"{MessageText.Name(method)} reaches Warp.ActiveMask() and has no body with reconvergence regions");
    }

    /// <summary>What the stack form adds to <paramref name="call"/> of <paramref name="callee"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// The call needs a place that the frame of the method being written does not have (a compiler bug).
    /// </exception>
    public static StackWarpCall Plan(InvocationExpressionSyntax call, IMethodSymbol callee, ref TranspileContext context)
    {
        // [WHY] A struct member (a property, a conversion, Dispose) has no frame: a collective or a call that reaches one
        // there is the user's GSH0062, which StructMemberSuspendingCall has reported, not a missing place
        var dialect = context.Emitter.Dialect;
        if (!dialect.RunsThreadsOnOwnStacks() || context.Emitter.CurrentStructType != null)
        {
            return StackWarpCall.None;
        }

        var compilation = context.Query.SemanticModel.Compilation;
        var intrinsic = SyncIntrinsicClassifier.Classify(callee);
        if (intrinsic.Kind == SyncIntrinsicKind.WarpShuffle)
        {
            // Warp.ActiveMask() included: it meets with the mask 0 at its site, and the reconvergence regions around it
            // come from the stack body (WriteBody)
            var frame = RequireFrame(call, ref context);
            if (!frame.Layout.TryGetSite(call, frame.TreeOrdinal, out var site))
            {
                throw InternalError(
                    $"the warp collective at {SourcePlace.Format(context.Query.SemanticModel, call)} has no site");
            }

            return StackWarpCall.Collective(WarpShuffleShape.ForKind(intrinsic.Shuffle).StackFunction, site,
                SourcePlace.Format(context.Query.SemanticModel, call));
        }

        if (!CppNameResolver.IsEmittedDeviceFunction(callee, dialect) ||
            !SuspensionAnalysis.ReachesWarpShuffleOnStack(callee, compilation))
        {
            return StackWarpCall.None;
        }

        var callerFrame = RequireFrame(call, ref context);
        if (!callerFrame.Layout.TryGetCallOffset(call, callerFrame.TreeOrdinal, out var offset))
        {
            throw InternalError(
                $"the call of {MessageText.Name(callee)} at " +
                $"{SourcePlace.Format(context.Query.SemanticModel, call)} has no frame");
        }

        return StackWarpCall.FrameCall(offset);
    }

    /// <summary>Appends <c>uint32_t __gsharp_warp_frame</c>, the hidden last parameter of the stack form.</summary>
    public static void AppendFrameParameter(bool hasParameters, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendIf(hasParameters, ", ");
        context.Emitter.Writer.Append($"uint32_t {GSharpInternalNames.StackWarp.Frame}");
    }

    /// <summary>
    /// Appends <c>__gsharp_warp_frame, &lt;site&gt;u, "&lt;place&gt;"</c> and a comma when arguments follow: the first
    /// arguments of a collective.
    /// </summary>
    public static void AppendCollectivePrefix(in StackWarpCall plan, bool argumentsFollow, ref TranspileContext context)
    {
        context.Emitter.Writer.Append($"{GSharpInternalNames.StackWarp.Frame},{plan.Site}u,");
        CppStringLiteral.Append(context.Emitter.Writer, plan.Place);
        context.Emitter.Writer.AppendIf(argumentsFollow, ",");
    }

    /// <summary>
    /// Appends <c>__gsharp_warp_frame+&lt;offset&gt;u</c>, the hidden last argument of a call of a device function that
    /// reaches a collective, after a comma when arguments precede it.
    /// </summary>
    public static void AppendFrameArgument(in StackWarpCall plan, bool argumentsPrecede, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendIf(argumentsPrecede, ",");
        context.Emitter.Writer.Append($"{GSharpInternalNames.StackWarp.Frame}+{plan.Offset}u");
    }

    /// <summary>
    /// Writes the first lines of the stack form of a kernel that reaches a collective: its visit counts on the stack of
    /// the GPU thread, and its frame at offset 0:
    /// <c>uint32_t __gsharp_stack_warp_visits[&lt;size&gt;] = {};</c>,
    /// <c>__gsharp_stack_warp_begin(__gsharp_stack_warp_visits, &lt;size&gt;u);</c>,
    /// <c>constexpr uint32_t __gsharp_warp_frame = 0u;</c>
    /// </summary>
    public static void WriteKernelPrologue(in MethodEmissionPlan plan, ref TranspileContext context)
    {
        if (plan.Kind != MethodEmissionKind.Kernel || context.Emitter.StackWarp is not { } frame)
        {
            return;
        }

        var size = frame.Layout.Size;
        context.Emitter.Writer.AppendLine($"uint32_t {GSharpInternalNames.StackWarp.Visits}[{size}] = {{}};");
        context.Emitter.Writer.AppendLine(
            $"{GSharpInternalNames.StackWarp.Begin}({GSharpInternalNames.StackWarp.Visits}, {size}u);");
        context.Emitter.Writer.AppendLine($"constexpr uint32_t {GSharpInternalNames.StackWarp.Frame} = 0u;");
    }

    private static StackWarpFrame RequireFrame(InvocationExpressionSyntax call, ref TranspileContext context) =>
        context.Emitter.StackWarp ?? throw InternalError(
            $"the call at {SourcePlace.Format(context.Query.SemanticModel, call)} reaches a warp collective from code " +
            "without a frame");

    private static InvalidOperationException InternalError(string what) =>
        new($"G#: internal error: the native stack model: {what} (please report this G# bug)");
}
