namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

/// <summary>A method that an operation runs or references, and the syntax a diagnostic about it points at.</summary>
internal readonly record struct OperationCallee(IMethodSymbol Method, SyntaxNode Site);
