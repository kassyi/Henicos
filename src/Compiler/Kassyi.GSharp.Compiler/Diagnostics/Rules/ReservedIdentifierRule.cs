using System;
using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

/// <summary>
/// Rule that prevents the use of double-underscore for identifiers in user-defined GPU kernels,
/// as they are reserved for GSharp compiler internals.
/// </summary>
public static class ReservedIdentifierRule
{
    public static void Validate(string identifierName, Location location, Action<Diagnostic> reportDiagnostic)
    {
        if (string.IsNullOrEmpty(identifierName) || identifierName.Length < 2 || !identifierName.Contains("__"))
        {
            return;
        }

        reportDiagnostic(Diagnostic.Create(
            GSharpDiagnostics.ReservedIdentifierNotAllowed,
            location,
            identifierName
        ));
    }
}
