namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The keyword the managed copy writes before an argument.
/// </summary>
internal enum ManagedArgumentModifier
{
    /// <summary>No keyword.</summary>
    None,
    /// <summary><c>ref</c>.</summary>
    Ref,
    /// <summary><c>in</c>.</summary>
    In
}

/// <summary>
/// Rules for argument keywords and caller-info forms in managed call emission.
/// </summary>
internal static class ManagedCallForms
{
    /// <summary>
    /// Whether caller-info arguments should be added to <paramref name="method"/>: user method (declared in compilation
    /// or device function) and not a member of a shared-memory type.
    /// </summary>
    public static bool AddsCallerInfo(IMethodSymbol method) =>
        IsUserMethod(method) && !method.ContainingType.IsSharedMemoryType();

    /// <summary>
    /// Whether an argument passed to a parameter with <paramref name="parameterRefKind"/> needs a variable test
    /// (<c>value.IsVariable()</c>) when no ref kind keyword is written at the call site.
    /// </summary>
    public static bool ArgumentNeedsVariableTest(RefKind parameterRefKind) =>
        parameterRefKind == RefKind.RefReadOnlyParameter;

    /// <summary>
    /// The argument modifier keyword to write before an argument passed to a parameter with
    /// <paramref name="parameterRefKind"/> based on whether the argument is a variable.
    /// </summary>
    // [WHY] A variable passed to a ref readonly parameter without a keyword is passed by reference, and C# warns
    // that it should carry one (CS9192). The user's source already reports or suppresses that warning, so the copy
    // writes the keyword (in), which passes the same reference without repeating it. A value that is not a variable
    // keeps no keyword: C# passes a temporary for it, and in would not compile
    public static ManagedArgumentModifier GetArgumentModifier(RefKind parameterRefKind, bool isVariable) =>
        ArgumentNeedsVariableTest(parameterRefKind) && isVariable
            ? ManagedArgumentModifier.In
            : ManagedArgumentModifier.None;

    /// <summary>
    /// Whether an extension method call receiver needs a variable test (<c>IsVariableOfType</c>).
    /// </summary>
    public static bool ReceiverNeedsVariableTest(RefKind parameterRefKind) =>
        parameterRefKind is RefKind.In or RefKind.RefReadOnlyParameter;

    /// <summary>
    /// The modifier keyword to write before an extension method call receiver.
    /// </summary>
    public static ManagedArgumentModifier GetReceiverModifier(
        RefKind parameterRefKind,
        bool isVariableOfParameterType) =>
        parameterRefKind switch
        {
            RefKind.Ref => ManagedArgumentModifier.Ref,
            RefKind.In or RefKind.RefReadOnlyParameter when isVariableOfParameterType => ManagedArgumentModifier.In,
            _ => ManagedArgumentModifier.None
        };

    // A method declared in this compilation, or a device function of another assembly (the managed path calls it, or
    // its managed version, with the parameters it declares)
    private static bool IsUserMethod(IMethodSymbol method) =>
        !method.OriginalDefinition.DeclaringSyntaxReferences.IsEmpty ||
        (method.ReducedFrom ?? method).OriginalDefinition.HasAttribute(KnownAttributes.GpuDeviceFunction);
}
