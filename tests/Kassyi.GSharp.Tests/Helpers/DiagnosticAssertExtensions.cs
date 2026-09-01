using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Helpers;

public static class DiagnosticAssertExtensions
{
    public static void AssertContainsError(this IEnumerable<Diagnostic> diagnostics, string diagnosticId)
    {
        Assert.Contains(diagnostics, d => d.Id == diagnosticId && d.Severity == DiagnosticSeverity.Error);
    }

    public static void AssertContains(this IEnumerable<Diagnostic> diagnostics, string diagnosticId)
    {
        Assert.Contains(diagnostics, d => d.Id == diagnosticId);
    }
}
