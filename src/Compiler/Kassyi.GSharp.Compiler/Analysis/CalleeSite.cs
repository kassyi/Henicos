namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>One callee reached from a body and the syntax of the operation that makes the call.</summary>
/// <param name="Callee">The called method, constructor, accessor, conversion, operator, or method-group target.</param>
/// <param name="Site">The syntax node of the operation that makes or references the call.</param>
internal readonly record struct CalleeSite(IMethodSymbol Callee, SyntaxNode Site);
