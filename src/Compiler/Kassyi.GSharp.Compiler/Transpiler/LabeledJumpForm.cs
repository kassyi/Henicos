namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// How the emitter writes a <c>break</c> or <c>continue</c> statement (#1990).
/// </summary>
internal enum LabeledJumpForm
{
    /// <summary>Written as a C++ <c>break;</c> or <c>continue;</c> (unlabeled, or naming the innermost target).</summary>
    Plain,

    /// <summary>Written as <c>goto __gsharp_LabeledBreak_...</c> or <c>goto __gsharp_LabeledContinue_...</c>.</summary>
    Goto,
}
