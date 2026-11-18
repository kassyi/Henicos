namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// The plan of what a C++ lambda expression must capture: default reference capture <c>[&amp;]</c>,
/// plus state machine context or suspending call root by value when referenced in the body.
/// </summary>
/// <param name="CapturesContext">
/// Whether the lambda body references <see cref="GSharpInternalNames.StateMachine.Context"/>.
/// </param>
/// <param name="CapturesRoot">
/// Whether the lambda body references <see cref="GSharpInternalNames.SuspendingCall.Root"/>.
/// </param>
public readonly record struct LambdaCapturePlan(bool CapturesContext, bool CapturesRoot);

/// <summary>
/// Plans and emits C++ lambda capture clauses.
/// </summary>
/// <remarks>
/// <para>
/// On the native CPU state machine path, <c>__gsharp_ctx</c> is passed as a pointer parameter to state functions.
/// When a lambda or local function is stored in a delegate variable that is live across a barrier, default reference
/// capture <c>[&amp;]</c> would capture the state function's stack parameter by reference, leading to undefined
/// behavior (dangling stack reference) when invoked in a subsequent state. Capturing the pointer by value
/// (<c>[&amp;, __gsharp_ctx]</c> and/or <c>[&amp;, __gsharp_root]</c>) preserves valid pointer semantics across resumes
/// while capturing all other locals by reference.
/// </para>
/// </remarks>
public static class LambdaCapture
{
    private const string DefaultCapture = "[&]";
    private const string ContextCapture = "[&, " + GSharpInternalNames.StateMachine.Context + "]";
    private const string RootCapture = "[&, " + GSharpInternalNames.SuspendingCall.Root + "]";
    private const string BothCapture =
        "[&, " + GSharpInternalNames.StateMachine.Context + ", " + GSharpInternalNames.SuspendingCall.Root + "]";

    /// <summary>
    /// Creates a capture plan by inspecting identifiers in the lambda or local function body.
    /// </summary>
    /// <param name="body">The lowered body syntax node, or <see langword="null"/>.</param>
    /// <returns>A <see cref="LambdaCapturePlan"/> recording whether context or root is referenced.</returns>
    public static LambdaCapturePlan Create(SyntaxNode? body)
    {
        if (body == null)
        {
            return default;
        }

        var capturesContext = false;
        var capturesRoot = false;

        var enumerator = body.DescendantNodes()
            .AsStackEnumerable()
            .OfType<IdentifierNameSyntax>()
            .Enumerator;

        while (enumerator.TryGetNext(out var id))
        {
            var text = id.Identifier.ValueText;
            if (text == GSharpInternalNames.StateMachine.Context)
            {
                capturesContext = true;
                if (capturesRoot)
                {
                    break;
                }
            }
            else if (text == GSharpInternalNames.SuspendingCall.Root)
            {
                capturesRoot = true;
                if (capturesContext)
                {
                    break;
                }
            }
        }

        return new LambdaCapturePlan(capturesContext, capturesRoot);
    }

    /// <summary>
    /// Creates a capture plan from either a block or an expression body.
    /// </summary>
    /// <param name="block">The block body, or <see langword="null"/>.</param>
    /// <param name="expressionBody">The expression body, or <see langword="null"/>.</param>
    /// <returns>A <see cref="LambdaCapturePlan"/> recording whether context or root is referenced.</returns>
    public static LambdaCapturePlan Create(BlockSyntax? block, ExpressionSyntax? expressionBody) =>
        Create(block ?? (SyntaxNode?)expressionBody);

    // [&]
    // [&, __gsharp_ctx]
    // [&, __gsharp_root]
    // [&, __gsharp_ctx, __gsharp_root]
    /// <summary>
    /// Emits the C++ lambda capture clause to the specified writer.
    /// </summary>
    /// <param name="writer">The output writer.</param>
    /// <param name="plan">The capture plan.</param>
    public static void Append(SourceWriter writer, in LambdaCapturePlan plan)
    {
        if (plan.CapturesContext && plan.CapturesRoot)
        {
            writer.Append(BothCapture);
        }
        else if (plan.CapturesContext)
        {
            writer.Append(ContextCapture);
        }
        else if (plan.CapturesRoot)
        {
            writer.Append(RootCapture);
        }
        else
        {
            writer.Append(DefaultCapture);
        }
    }
}
