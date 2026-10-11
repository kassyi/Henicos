namespace Kassyi.GSharp.Compiler.Transpiler.Rules;

/// <summary>
/// Provides an abstraction for resolving <see cref="ISyntaxNodeTranspilerRule"/> strategies by <see cref="SyntaxKind"/>.
/// </summary>
public interface ITranspilerRuleRegistry
{
    /// <summary>
    /// Gets the registered transpiler rule for the specified syntax kind, if any.
    /// </summary>
    ISyntaxNodeTranspilerRule? GetRule(SyntaxKind kind);
}
