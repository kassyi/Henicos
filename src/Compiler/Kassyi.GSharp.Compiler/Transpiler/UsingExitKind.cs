namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>The kind of a jump out of the body of a <c>using</c>.</summary>
internal enum UsingExitKind
{
    /// <summary>A <c>return</c> of the function that holds the <c>using</c>.</summary>
    Return,

    /// <summary>A <c>break</c> of a loop or switch outside the body.</summary>
    Break,

    /// <summary>A <c>continue</c> of a loop outside the body.</summary>
    Continue,

    /// <summary>A <c>goto</c> to a label outside the body.</summary>
    Goto,

    /// <summary>A <c>goto case</c> or <c>goto default</c> of a switch outside the body.</summary>
    GotoCase
}
