namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The expansion of a <c>&amp;&amp;</c> or <c>||</c> over an operator macro
/// (<see cref="OperatorMacroCallPlanner.PlanConditional"/>).
/// </summary>
internal readonly struct MacroConditionalPlan(IMethodSymbol test, bool passesTestByReference, bool spillsTarget)
{
    /// <summary>The <c>operator false</c> or <c>operator true</c> that tests the left operand.</summary>
    public IMethodSymbol Test { get; } = test;

    /// <summary>Whether the test takes the left operand by reference (<c>in</c>).</summary>
    public bool PassesTestByReference { get; } = passesTestByReference;

    /// <summary>Whether the target is evaluated into a reference before the left operand.</summary>
    public bool SpillsTarget { get; } = spillsTarget;
}
