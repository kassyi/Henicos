namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>The arm that the managed copy adds to a switch expression that is not exhaustive.</summary>
internal enum UnmatchedSwitchArm
{
    /// <summary><c>var v =&gt; throw new SwitchExpressionException(v)</c>.</summary>
    SwitchExpressionWithValue,

    /// <summary><c>_ =&gt; throw new SwitchExpressionException()</c>, for an input that cannot be boxed.</summary>
    SwitchExpression,

    /// <summary>
    /// <c>_ =&gt; throw new InvalidOperationException()</c>, where the library has no SwitchExpressionException.
    /// </summary>
    InvalidOperation
}
