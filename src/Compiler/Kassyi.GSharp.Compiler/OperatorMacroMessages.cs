namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The message arguments of GSH0003 for an operator macro that cannot be expanded: the resolution of its target
/// (<see cref="OperatorMacroResolver"/>, <see cref="OperatorMacroOverloadResolver"/>), its context
/// (<see cref="OperatorMacroContext"/>), where it is written (<see cref="OperatorMacroPosition"/>) and whether its
/// target and context are assigned (<see cref="OperatorMacroAssignment"/>).
/// </summary>
internal static class OperatorMacroMessages
{
    internal static string CompoundAssignment(string op) =>
        $"The compound assignment '{op}' does not expand operator macros: give each operator macro a simple " +
        "assignment of its own, as in 't = a + b; c = c + t;'";

    // <name> is "'c' " or empty when the target is no symbol
    internal static string TargetDoesNotConvert(string name, string targetName, IParameterSymbol resultParameter) =>
        $"The target {name}of operator macro '{targetName}' does not convert to " +
        $"'{FormatType(resultParameter.Type)}' of parameter '{resultParameter.Name}'";

    internal static string PropertyWithoutStorage(ExpressionSyntax target, string targetName,
        IParameterSymbol resultParameter) =>
        $"The target '{target}' of operator macro '{targetName}' is a property without storage: the target writes " +
        $"the result through its '{FormatRefKind(resultParameter.RefKind).TrimEnd()}' parameter " +
        $"'{resultParameter.Name}', which would reach a copy and never the setter; compute into a variable first " +
        $"and assign it, as in 't = a + b; {target} = t;'";

    internal static string UnassignedTarget(ExpressionSyntax target, string targetName, string variable) =>
        $"The target '{target}' of operator macro '{targetName}' is not assigned before the assignment: the target " +
        $"receives the result in the storage it holds, so assign '{variable}' first, as in '{variable} = default;'";

    internal static string UnassignedContext(string variable, string targetName) =>
        $"The context '{variable}' of operator macro '{targetName}' is not assigned before the assignment: the " +
        $"target receives the context variable itself, so assign '{variable}' first, as in '{variable} = default;'";

    internal static string IncrementOrDecrement(string op) =>
        $"Operator macro '{op}' is an increment or decrement operator, which assigns its operand and is not " +
        "expanded; write the assignment with a binary operator macro";

    internal static string Lifted(string op) =>
        $"Operator macro '{op}' is applied to nullable operands (a lifted operator), which it does not expand; " +
        "apply it to the values ('.Value')";

    // <failure> is what OperatorMacroOverloadResolver.ResolveOverload says about the overloads
    internal static string NoOverload(string target, string failure) => $"Operator macro target '{target}' {failure}";

    internal static string NoTargetMethod(string op) => $"Operator macro '{op}' names no target method";

    internal static string NestedTypeMismatch(string innerOp, ITypeSymbol innerType, string outerOp,
        ITypeSymbol operandType) =>
        $"Operator macro '{innerOp}' returns '{FormatType(innerType)}', but the operand of '{outerOp}' takes " +
        $"'{FormatType(operandType)}'; assign it to a variable first, as in 't = a + b; c = c + t;'";

    internal static string NoTemporary(string innerOp, string innerTargetName, IParameterSymbol innerResult) =>
        $"Operator macro '{innerOp}' is an operand of another operator macro, but its target '{innerTargetName}' " +
        $"takes the result as '{FormatRefKind(innerResult.RefKind)}{FormatType(innerResult.Type)} " +
        $"{innerResult.Name}', not as a ref or out parameter of an unmanaged type, so it has no temporary to " +
        "compute into; assign it to a variable first, as in 't = a + b; c = c + t;'";

    internal static string UnderConversion(string op) =>
        $"Operator macro '{op}' is under a user-defined conversion, which its expansion would drop; assign it to a " +
        "variable first, as in 't = a + b;', and convert the variable";

    internal static string NotExpandedHere(string op) =>
        $"Operator macro '{op}' is expanded only as the right side of an assignment statement, or as an operand of " +
        "an operator macro there; assign it to a variable first, as in 't = a + b;'";

    // The completions of "Operator macro target 'T.M' ..." by OperatorMacroOverloadResolver
    internal static string NoApplicableOverload(string operands, string why) =>
        $"has no overload that takes ({operands}): {why}";

    internal const string NoMethodOfThatName = "no method has that name";

    internal static string Ambiguous(string names, string last) => $"is ambiguous between {names} and {last}";

    internal const string NotStaticDeviceFunction = "not a static [GpuDeviceFunction] method";

    internal const string GenericCandidate = "generic methods are not candidates";

    internal static string ParameterCount(int parameterCount, int operandCount) =>
        $"takes {parameterCount} parameters, but the operator passes {operandCount} (or a context of a value type " +
        $"and {operandCount})";

    internal static string ReferenceNotResult(string keyword, string parameter) =>
        $"'{keyword}' parameter '{parameter}' is not the result";

    internal static string ReferenceTakesOnlyItsType(string keyword, IParameterSymbol parameter, ITypeSymbol from) =>
        $"'{keyword}' parameter '{parameter.Name}' takes only '{FormatType(parameter.Type)}', not " +
        $"'{FormatType(from)}'";

    internal static string DoesNotConvert(ITypeSymbol from, IParameterSymbol parameter) =>
        $"'{FormatType(from)}' does not convert to '{FormatType(parameter.Type)}' for '{parameter.Name}'";

    // OperatorMacroContext.TryFindContextVariable
    internal static string NoContext(string target, string type) =>
        $"Operator macro target '{target}' takes a context of type '{type}', but no parameter or local of that type " +
        "is in scope";

    internal static string SeveralContexts(string target, string type, int count, string names) =>
        $"Operator macro target '{target}' takes a context of type '{type}', but {count} parameters or locals of " +
        $"that type are in scope ({names})";

    internal static string FormatRefKind(RefKind refKind) => refKind.FormatRefKind();

    // uint[], OverloadCtx: the short C# spelling of a type in a message
    internal static string FormatType(ITypeSymbol type) => IteratorTypeResolver.FormatType(type);
}
