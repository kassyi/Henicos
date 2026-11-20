namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The visit counts that the stack form of a kernel or device function gives its warp collectives and the calls of
/// device functions that reach one (<see cref="WarpFrameLayout"/>), for <see cref="ManagedExecutionRewriter"/>.
/// </summary>
/// <remarks>
/// [WHY] <see cref="ManagedStackEligibility"/> keeps a function whose counts cannot be placed on its state machine, so a
/// stack form never meets a collective or a call without a place. One that does is a G# bug: it throws instead of
/// taking site or offset 0, which would make two instances of a collective meet as one.
/// </remarks>
internal sealed class ManagedStackWarpLayout
{
    private readonly WarpFrameLayout _layout;
    private readonly int _treeOrdinal;
    private readonly string _methodName;

    private ManagedStackWarpLayout(WarpFrameLayout layout, int treeOrdinal, string methodName)
    {
        _layout = layout;
        _treeOrdinal = treeOrdinal;
        _methodName = methodName;
    }

    /// <summary>The visit counts of the frame of the method and of the frames of its calls.</summary>
    public int Size => _layout.Size;

    /// <summary>
    /// The layout of the stack form of <paramref name="method"/>, or <see langword="null"/> when it reaches no warp
    /// collective.
    /// </summary>
    /// <param name="method">The kernel or device function.</param>
    /// <param name="declaration">Its declaration, the body that the stack form copies.</param>
    /// <param name="compilation">The compilation.</param>
    /// <param name="isKernel">Whether <paramref name="method"/> is a kernel, whose counts the GPU thread holds.</param>
    /// <exception cref="InvalidOperationException">
    /// The method reaches a collective, and its counts cannot be placed or a kernel's are more than
    /// <see cref="WarpFrameLayout.MaxSize"/>.
    /// </exception>
    public static ManagedStackWarpLayout? Create(IMethodSymbol method, MethodDeclarationSyntax declaration,
        Compilation compilation, bool isKernel)
    {
        if (!SuspensionAnalysis.ReachesWarpShuffle(method, compilation))
        {
            return null;
        }

        if (!WarpFrameLayout.TryGet(method, compilation, out var layout))
        {
            throw InternalError($"the visit counts of the warp collectives of '{method.Name}' cannot be placed");
        }

        if (isKernel && layout.Size > WarpFrameLayout.MaxSize)
        {
            throw InternalError(
                $"'{method.Name}' needs {layout.Size} warp visit counts, more than {WarpFrameLayout.MaxSize}");
        }

        return new ManagedStackWarpLayout(layout,
            SemanticModelExtensions.GetSyntaxTreeOrdinal(compilation, declaration.SyntaxTree), method.Name);
    }

    /// <summary>The site of the warp collective <paramref name="call"/> of the method's body.</summary>
    /// <exception cref="InvalidOperationException">The layout has no site for the call.</exception>
    public int GetSite(InvocationExpressionSyntax call) =>
        _layout.TryGetSite(call, _treeOrdinal, out var site)
            ? site
            : throw InternalError(
                $"the warp collective at {SourcePlace.Format(call.GetLocation())} of '{_methodName}' has no site");

    /// <summary>
    /// The offset of the frame of the device function that <paramref name="call"/> calls, from the method's frame.
    /// </summary>
    /// <exception cref="InvalidOperationException">The layout has no frame for the call.</exception>
    public int GetCallOffset(InvocationExpressionSyntax call) =>
        _layout.TryGetCallOffset(call, _treeOrdinal, out var offset)
            ? offset
            : throw InternalError(
                $"the call at {SourcePlace.Format(call.GetLocation())} of '{_methodName}' of a device function " +
                "that reaches a warp collective has no frame");

    private static InvalidOperationException InternalError(string what) =>
        new($"G#: internal error: {what} on the managed stack model (please report this G# bug).");
}
