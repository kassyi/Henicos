using System;
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

    public static readonly string Sources = $$"""
    using System;
    using Kassyi.GSharp.Core;

    public struct DummyDisposable : IDisposable
    {
        public void Dispose() { }
    }

    public struct DummyExplicitDisposable : IDisposable
    {
        void IDisposable.Dispose() { }
    }

    public struct DummyCustomDispose
    {
        public void Dispose() { }
    }

    public struct DummyValid
    {
        public int Value;
    }

    public struct DummyDisposeBool
    {
        public void Dispose(bool disposing) { }
    }

    class Class_{{ID}}
    {
        public void TestDisposable()
        {
            var mem = new SharedMemory<DummyDisposable>(64);
        }

        public void TestExplicitDisposable()
        {
            var mem = new SharedMemory<DummyExplicitDisposable>(64);
        }

        public void TestCustomDispose()
        {
            var mem = new SharedMemory<DummyCustomDispose>(64);
        }

        public void TestTargetTypedDisposable()
        {
            SharedMemory<DummyDisposable> mem = new(64);
        }

        public void TestValid()
        {
            var mem = new SharedMemory<DummyValid>(64);
        }

        public void TestDisposeBool()
        {
            var mem = new SharedMemory<DummyDisposeBool>(64);
        }
    }
    """;

    [ModuleInitializer]
    public static void Register() => MegaCompilationCache.RegisterSource(Sources);

    [Theory]
    [InlineData("TestDisposable")]
    [InlineData("TestExplicitDisposable")]
    [InlineData("TestCustomDispose")]
    [InlineData("TestTargetTypedDisposable")]
    [Trait("FeatureId", "MEM-04")]
    [Trait("Category", "SadPath")]
    public void Validate_SharedMemoryForbiddenDisposableRule_ReportsError_ForDisposableTypes(string methodName)
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
    [Trait("FeatureId", "MEM-04")]
    public void Validate_SharedMemoryForbiddenDisposableRule_Allows_NonDisposableTypes(string methodName)
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
