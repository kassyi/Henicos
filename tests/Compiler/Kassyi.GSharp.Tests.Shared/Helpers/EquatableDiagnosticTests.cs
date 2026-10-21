using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using Kassyi.GSharp.Compiler.Helpers;
using Kassyi.GSharp.Compiler.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

public class EquatableDiagnosticTests
{
    private static Diagnostic CreateDiagnostic(string id, string message)
    {
        var descriptor = new DiagnosticDescriptor(id, "Title", message, "Category", DiagnosticSeverity.Warning, true);
        return Diagnostic.Create(descriptor, Location.None);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void Equals_ReturnsTrue_ForSameDiagnostics()
    {
        var diag1 = CreateDiagnostic("GSH0001", "Test message").AsEquatable();
        var diag2 = CreateDiagnostic("GSH0001", "Test message").AsEquatable();

        Assert.True(diag1.Equals(diag2));
        Assert.True(diag1.Equals((object)diag2));
        Assert.True(diag1 == diag2);
        Assert.False(diag1 != diag2);
        Assert.Equal(diag1.GetHashCode(), diag2.GetHashCode());
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void Equals_ReturnsFalse_ForDifferentDiagnostics()
    {
        var diag1 = CreateDiagnostic("GSH0001", "Test message").AsEquatable();
        var diag2 = CreateDiagnostic("GSH0002", "Different message").AsEquatable();

        Assert.False(diag1.Equals(diag2));
        Assert.False(diag1.Equals((object)diag2));
        Assert.False(diag1 == diag2);
        Assert.True(diag1 != diag2);
        Assert.NotEqual(diag1.GetHashCode(), diag2.GetHashCode());
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ToString_ReturnsDiagnosticToString()
    {
        var diag = CreateDiagnostic("GSH0001", "Test message");
        var equatable = diag.AsEquatable();

        Assert.Equal(diag.ToString(), equatable.ToString());
    }
}
