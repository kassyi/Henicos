namespace Kassyi.GSharp.Compiler;

/// <summary>A <c>Gpu</c> macro and the overload a call resolved to.</summary>
/// <remarks>
/// The ordinals are those of the overload's parameters, not positions in a call: a call may name its arguments in any
/// order, so an argument is found through <see cref="GetArgument"/>, never by indexing the argument list.
/// </remarks>
internal readonly struct GpuMacroCall(
    GpuMacroKind kind,
    IMethodSymbol? method = null,
    bool hasContext = false,
    int bodyOrdinal = -1)
{
    public GpuMacroKind Kind { get; } = kind;

    /// <summary>The overload; <c>null</c> for a call classified without a semantic model.</summary>
    public IMethodSymbol? Method { get; } = method;

    /// <summary>Whether the overload takes <c>ref TContext ctx</c> before its other arguments (ForUnroll / GridStrideLoop).</summary>
    public bool HasContext { get; } = hasContext;

    /// <summary>
    /// The ordinal of the first parameter after the context parameter (<c>start</c> or <c>maxBounds</c>).
    /// </summary>
    public int ValueOrdinal => HasContext ? 1 : 0;

    /// <summary>The ordinal of the body parameter; meaningful only when <see cref="HasBody"/> is set.</summary>
    public int BodyOrdinal { get; } = bodyOrdinal;

    /// <summary>Whether the overload takes a body lambda (SyncGrid has a form without one).</summary>
    public bool HasBody { get; } = bodyOrdinal >= 0;

    /// <summary>
    /// The argument of the call that the parameter at <paramref name="ordinal"/> receives, or <c>null</c> when the call
    /// has none. Without <see cref="Method"/> only a call without named arguments is read, by position.
    /// </summary>
    public ArgumentSyntax? GetArgument(SeparatedSyntaxList<ArgumentSyntax> arguments, int ordinal)
    {
        if (Method is { } method)
        {
            return ordinal < method.Parameters.Length
                ? ArgumentMatchingHelper.FindArgument(arguments, method.Parameters[ordinal])
                : null;
        }

        var hasName = arguments
            .AsStackEnumerable()
            .Any(static argument => argument.NameColon is not null);
        return !hasName && ordinal < arguments.Count ? arguments[ordinal] : null;
    }

    /// <summary>The body lambda of the call, or <c>null</c> when the body is not written as a lambda.</summary>
    public LambdaExpressionSyntax? GetBody(SeparatedSyntaxList<ArgumentSyntax> arguments) =>
        HasBody ? GetArgument(arguments, BodyOrdinal)?.Expression as LambdaExpressionSyntax : null;

    /// <summary>
    /// The context argument (<c>ref ctx</c>) of the call, or <c>null</c> for an overload without one.
    /// </summary>
    public ExpressionSyntax? GetContext(SeparatedSyntaxList<ArgumentSyntax> arguments) =>
        HasContext ? GetArgument(arguments, 0)?.Expression : null;
}
