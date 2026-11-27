using System.Collections.Concurrent;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

[Category(TestCategory.Compiler)]
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

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsParallelDependent_PartialPolymorphicRecursion_TerminatesWithExpectedResult()
    {
        const string Source = """
            using Kassyi.GSharp.Core;

            namespace ContextInferencePartialPolyCases;

            public struct PolyWrap<T>
            {
                public T Value;
            }

            public static partial class Functions
            {
                [GpuDeviceFunction]
                public static partial int PartialPlain<T>(int depth);

                public static partial int PartialPlain<T>(int depth) =>
                    depth <= 0 ? 0 : PartialPlain<PolyWrap<T>>(depth - 1);

                [GpuDeviceFunction]
                public static partial int PartialThreadIndex<T>(int depth);

                public static partial int PartialThreadIndex<T>(int depth) =>
                    depth <= 0 ? ThreadIndex.X : PartialThreadIndex<PolyWrap<T>>(depth - 1);

                [GpuDeviceFunction]
                public static partial int PartialIndirectPlain<T>(int depth);

                public static partial int PartialIndirectPlain<T>(int depth) =>
                    depth <= 0 ? 0 : RelayPlain<T>(depth);

                [GpuDeviceFunction]
                public static int RelayPlain<U>(int depth) => PartialIndirectPlain<PolyWrap<U>>(depth - 1);
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(Source);
        var functions = compilation.GetTypeByMetadataName("ContextInferencePartialPolyCases.Functions")
            ?? throw new InvalidOperationException("Type 'Functions' not found.");
        var intType = compilation.GetSpecialType(SpecialType.System_Int32);
        var plain = functions.GetMembers("PartialPlain").OfType<IMethodSymbol>().Single().Construct(intType);
        var threadIndex = functions.GetMembers("PartialThreadIndex").OfType<IMethodSymbol>().Single().Construct(intType);
        var indirectPlain = functions.GetMembers("PartialIndirectPlain").OfType<IMethodSymbol>().Single().Construct(intType);
        var cache = new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);

        Assert.False(ContextInferenceEngine.IsParallelDependent(plain, cache, compilation));
        Assert.True(ContextInferenceEngine.IsParallelDependent(threadIndex, cache, compilation));
        Assert.False(ContextInferenceEngine.IsParallelDependent(indirectPlain, cache, compilation));
    }
}
