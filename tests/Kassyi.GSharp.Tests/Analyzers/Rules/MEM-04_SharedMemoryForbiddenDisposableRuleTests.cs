using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Diagnostics.Rules;
using Kassyi.GSharp.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers.Rules;

public class MEM_04_SharedMemoryForbiddenDisposableRuleTests
{
    private const string ID = nameof(MEM_04_SharedMemoryForbiddenDisposableRuleTests);

    [ModuleInitializer]
    public static void Register()
    {
        using var stream = typeof(MEM_04_SharedMemoryForbiddenDisposableRuleTests).Assembly.GetManifestResourceStream("Kassyi.GSharp.Tests.TestCases.Analyzers.MEM-04_SharedMemoryDisposableTestCases.cs")
            ?? throw new System.IO.FileNotFoundException("Could not find embedded test cases resource.");
        using var reader = new System.IO.StreamReader(stream);
        MegaCompilationCache.RegisterSource("MEM-04_SharedMemoryForbiddenDisposableRuleTests.cs", reader.ReadToEnd());
    }

    [Theory]
    [InlineData("TestDisposable")]
    [InlineData("TestExplicitDisposable")]
    [InlineData("TestCustomDispose")]
    [InlineData("TestTargetTypedDisposable")]
    [InlineData("TestGenericConstraint")]
    [Trait("FeatureId", "MEM-04")]
    [Trait("Category", "SadPath")]
    public void Validate_MEM04_SharedMemoryForbiddenDisposableRule_ReportsError_ForDisposableTypes(string methodName)
    {
        var rule = SharedMemoryForbiddenDisposableRule.Instance;
        var (model, method) = MegaCompilationCache.GetMethodContext($"Class_{ID}", methodName);
        var creationNode = method.DeclaringSyntaxReferences[0].GetSyntax()
            .DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().First();

        var reportedDiagnostics = new List<Diagnostic>();
        var context = new ValidationContext(model, method, reportedDiagnostics.Add);

        rule.Analyze(creationNode, ref context);

        var diagnostic = Assert.Single(reportedDiagnostics);
        Assert.Equal(GSharpDiagnostics.SharedMemoryDisposableNotSupported.Id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Theory]
    [InlineData("TestValid")]
    [InlineData("TestDisposeBool")]
    [InlineData("TestUnrelatedNamespace")]
    [Trait("FeatureId", "MEM-04")]
    public void Validate_MEM04_SharedMemoryForbiddenDisposableRule_Allows_NonDisposableTypes(string methodName)
    {
        var rule = SharedMemoryForbiddenDisposableRule.Instance;
        var (model, method) = MegaCompilationCache.GetMethodContext($"Class_{ID}", methodName);
        var creationNode = method.DeclaringSyntaxReferences[0].GetSyntax()
            .DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().First();

        var reportedDiagnostics = new List<Diagnostic>();
        var context = new ValidationContext(model, method, reportedDiagnostics.Add);

        rule.Analyze(creationNode, ref context);

        Assert.Empty(reportedDiagnostics);
    }
}

