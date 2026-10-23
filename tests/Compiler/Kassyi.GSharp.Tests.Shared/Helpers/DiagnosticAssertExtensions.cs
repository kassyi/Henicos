#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Helpers;

public static class DiagnosticAssertExtensions
{
    /// <summary>Asserts that a transpilation recorded a failure with <paramref name="descriptor"/> (and the message part).</summary>
    public static void AssertFails(this IEnumerable<TranspileFailure> failures, DiagnosticDescriptor descriptor,
        string? messagePart = null)
    {
        Assert.Contains(failures, f => f.Descriptor.Id == descriptor.Id &&
                                       (messagePart == null || f.Format().Contains(messagePart, StringComparison.Ordinal)));
    }

    public static void AssertContainsError(this IEnumerable<Diagnostic> diagnostics, string diagnosticId)
    {
        Assert.Contains(diagnostics, d => d.Id == diagnosticId && d.Severity == DiagnosticSeverity.Error);
    }

    public static void AssertContainsError(this IEnumerable<Diagnostic> diagnostics, DiagnosticDescriptor descriptor)
    {
        AssertContainsError(diagnostics, descriptor.Id);
    }

    public static void AssertContainsError(this IEnumerable<Diagnostic> diagnostics, DiagnosticDescriptor descriptor, string messageSubstring)
    {
        Assert.Contains(diagnostics, d => d.Id == descriptor.Id && d.Severity == DiagnosticSeverity.Error && d.GetMessage().Contains(messageSubstring));
    }

    public static void AssertContains(this IEnumerable<Diagnostic> diagnostics, string diagnosticId)
    {
        Assert.Contains(diagnostics, d => d.Id == diagnosticId);
    }

    public static void AssertContains(this IEnumerable<Diagnostic> diagnostics, string diagnosticId, string messageSubstring)
    {
        Assert.Contains(diagnostics, d => d.Id == diagnosticId && d.GetMessage().Contains(messageSubstring));
    }

    public static void AssertContains(this IEnumerable<Diagnostic> diagnostics, DiagnosticDescriptor descriptor)
    {
        AssertContains(diagnostics, descriptor.Id);
    }

    public static void AssertContains(this IEnumerable<Diagnostic> diagnostics, DiagnosticDescriptor descriptor, string messageSubstring)
    {
        Assert.Contains(diagnostics, d => d.Id == descriptor.Id && d.GetMessage().Contains(messageSubstring));
    }
}
