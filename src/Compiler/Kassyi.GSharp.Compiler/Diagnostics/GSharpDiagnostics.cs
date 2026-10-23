[assembly:
    SuppressMessage("ReSharper", "UnusedMember.Global", Scope = "namespaceanddescendants",
        Target = "Kassyi.GSharp.Compiler.Diagnostics", Justification = "Public API exposed for compiler usage")]
#pragma warning disable RS2008 // Enable analyzer release tracking

namespace Kassyi.GSharp.Compiler.Diagnostics;

/// <summary>Defines Roslyn diagnostic descriptors and error rules for GSharp GPU kernel compilation.</summary>
/// <remarks>
/// **[AI AGENT INSTRUCTION]**
/// Do NOT manually add new DiagnosticDescriptor properties to this file.
/// This file is a partial class. All diagnostic rules are automatically generated at compile-time.
/// To add or modify a diagnostic, edit the <c>diagnostics.json</c> file in this directory.
/// </remarks>
public static partial class GSharpDiagnostics
{
}
