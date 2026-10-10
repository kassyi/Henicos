using System.Collections.Concurrent;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

public class ContextInferenceEngineTests
{
    private (SemanticModel, IMethodSymbol) GetMethodSymbol(string methodName, [System.Runtime.CompilerServices.CallerFilePath] string currentFile = "")
    {
        // Dynamically load the AST and SemanticModel from the implicitly resolved .cu.cs file based on this test class name
        return RoslynTestUtility.GetMethodContext<ContextInferenceTestClass>(methodName, currentFile);
    }

    [Fact]
    public void IsParallelDependent_ObjectCreation_ReturnsTrue()
    {
        var (model, sym) = GetMethodSymbol("TestObjectCreation");
        var cache = new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
        bool result = ContextInferenceEngine.IsParallelDependent(sym, cache, model.Compilation);
        Assert.True(result);
    }

    [Fact]
    public void IsParallelDependent_BinaryExpression_ReturnsTrue()
    {
        var (model, sym) = GetMethodSymbol("TestBinary");
        var cache = new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
        bool result = ContextInferenceEngine.IsParallelDependent(sym, cache, model.Compilation);
        Assert.True(result);
    }

    [Fact]
    public void IsParallelDependent_AssignmentProperty_ReturnsTrue()
    {
        var (model, sym) = GetMethodSymbol("TestAssignmentProperty");
        var cache = new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
        bool result = ContextInferenceEngine.IsParallelDependent(sym, cache, model.Compilation);
        Assert.True(result);
    }

    [Fact]
    public void IsParallelDependent_AssignmentOperator_ReturnsTrue()
    {
        var (model, sym) = GetMethodSymbol("TestAssignmentOperator");
        var cache = new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
        bool result = ContextInferenceEngine.IsParallelDependent(sym, cache, model.Compilation);
        Assert.True(result);
    }
}
