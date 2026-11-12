using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.StackArrays;

/// <summary>A stack array storage: the C++ scope it lives in, and its name in the message.</summary>
internal readonly record struct Storage(SyntaxNode Scope, string Name);
