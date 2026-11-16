namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Which C++ target labels the statement of a <see cref="LabeledStatementSyntax"/> needs (#1990).
/// </summary>
internal readonly record struct LabeledTargetPlan(bool NeedsBreakLabel, bool NeedsContinueLabel);
