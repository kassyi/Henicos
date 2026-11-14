using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Lowering;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// The queries of <see cref="IteratorTypeResolver"/> that decide which device functions expand as iterators: the element
/// types rejected by GSH0070 and their reasons, the shape of an iterator method, recursion, the number of body copies
/// and the <c>foreach</c> sources that are iterator values.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class IteratorTypeResolverTests
{
    private const string TestCase = "Transpiler/IteratorTypeResolver/IteratorShapes.cs";

    private static readonly Lazy<Compilation> s_compilation = new(static () =>
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(TestCase));
        var errors = compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return compilation;
    });

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("typeParameter", "element type 'TFree' cannot be a type parameter")]
    [InlineData("unmanagedTypeParameter", "element type 'TUnmanaged' cannot be a type parameter")]
    [InlineData("sharedMemory", "element type 'SharedMemory<int>' cannot be SharedMemory<T>")]
    [InlineData("refStruct", "element type 'RefOnly' cannot be a ref struct")]
    [InlineData("pointer", "element type 'int*' cannot be a pointer")]
    [InlineData("functionPointer", "element type 'delegate*<void>' cannot be a pointer")]
    [InlineData("array", "element type 'int[]' cannot be an array")]
    [InlineData("managedClass", "element type 'string' must be an unmanaged value type")]
    [InlineData("managedStruct", "element type 'WithManagedField' must be an unmanaged value type")]
    public void TryGetUnsupportedElementTypeReason_UnsupportedType_ReturnsItsReason(string parameter, string expected)
    {
        var supported = !IteratorTypeResolver.TryGetUnsupportedElementTypeReason(CandidateType(parameter), out var reason);

        Assert.False(supported);
        Assert.Equal(expected, reason);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("primitive")]
    [InlineData("plainStruct")]
    public void TryGetUnsupportedElementTypeReason_UnmanagedValueType_IsSupported(string parameter)
    {
        Assert.False(IteratorTypeResolver.TryGetUnsupportedElementTypeReason(CandidateType(parameter), out var reason));
        Assert.Null(reason);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Iterators", "Valid", true)]
    [InlineData("Iterators", "WithoutAttribute", false)]
    [InlineData("Iterators", "Generic", false)]
    [InlineData("Iterators", "ManagedElement", false)]
    [InlineData("Iterators", "ReturnsAnother", false)]
    [InlineData("Iterators", "YieldOnlyInLocalFunction", false)]
    [InlineData("Holder", "InstanceIterator", false)]
    public void IsDeviceIteratorMethod_TakesOnlyStaticNonGenericDeviceFunctionsWithADirectYield(
        string typeName, string methodName, bool expected)
    {
        Assert.Equal(expected, IteratorTypeResolver.IsDeviceIteratorMethod(Method(typeName, methodName)));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Valid", true, true)]
    [InlineData("ReturnsAnother", false, false)]
    [InlineData("YieldOnlyInLocalFunction", false, true)]
    [InlineData("TryAndLockWithoutYield", false, false)]
    public void ContainsYield_LooksIntoLocalFunctionsOnlyForAnyYield(string methodName, bool direct, bool any)
    {
        var body = Declaration(Method("Iterators", methodName)).Body;

        Assert.Equal(direct, IteratorTypeResolver.ContainsDirectYield(body));
        Assert.Equal(any, IteratorTypeResolver.ContainsAnyYield(body));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Self", true)]
    [InlineData("PingA", true)]
    [InlineData("PingB", true)]
    [InlineData("CallsHelpers", false)]
    [InlineData("Valid", false)]
    public void IsRecursiveIterator_FindsDirectAndMutualRecursion(string methodName, bool expected)
    {
        Assert.Equal(expected, IteratorTypeResolver.IsRecursiveIterator(Method("Iterators", methodName), s_compilation.Value));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("ThirtyTwoInIf", 32)]
    [InlineData("Four", 4)]
    [InlineData("NineForEachOfFour", 36)]
    [InlineData("TwoInsideArrayLoop", 2)]
    public void CountYieldReturns_CountsNestedYieldsAndMultipliesNestedExpansions(string methodName, int expected)
    {
        Assert.Equal(expected, IteratorTypeResolver.CountYieldReturns(Method("Counts", methodName), s_compilation.Value));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("whenTrue", true)]
    [InlineData("whenFalse", true)]
    [InlineData("coalesceLeft", true)]
    [InlineData("coalesceRight", true)]
    [InlineData("assignedLocal", true)]
    [InlineData("assignedValue", true)]
    [InlineData("plainArray", false)]
    public void IsIteratorForEachSource_IteratorValueOnEitherSideOfTheInitializer(string local, bool expected)
    {
        var method = Declaration(Method("ForEachSources", "Consumers"));
        var forEach = method.DescendantNodes()
            .OfType<ForEachStatementSyntax>()
            .Single(f => f.Expression is IdentifierNameSyntax { Identifier.ValueText: var name } && name == local);
        var model = s_compilation.Value.GetSemanticModel(forEach.SyntaxTree);

        Assert.Equal(expected, IteratorTypeResolver.IsIteratorForEachSource(forEach.Expression, model));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void TryResolveIteratorSource_LocalInitializedInDifferentSyntaxTree_ResolvesIteratorInvocation()
    {
        const string LibSource = """
            using System.Collections.Generic;
            using Kassyi.GSharp.Core;

            public static partial class IterLib
            {
                [GpuDeviceFunction]
                public static IEnumerable<int> Inner(int n)
                {
                    yield return n;
                    yield return n + 1;
                }

                [GpuDeviceFunction]
                public static IEnumerable<int> Outer(int n)
                {
                    var it = Inner(n);
                    foreach (var x in it)
                    {
                        yield return x * 10;
                    }
                }
            }
            """;

        const string KernelSource = """
            using Kassyi.GSharp.Core;

            public static partial class CrossFileKernels
            {
                [GpuKernel]
                public static void SumKernel(int[] output)
                {
                    int tid = ThreadIndex.X;
                    int sum = 0;
                    foreach (var v in IterLib.Outer(tid))
                    {
                        sum += v;
                    }
                    output[tid] = sum;
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilationAt(
            ("IterLib.cs", LibSource),
            ("CrossFileKernels.cu.cs", KernelSource));

        var libTree = compilation.SyntaxTrees.First(static t => t.FilePath.EndsWith("IterLib.cs", StringComparison.Ordinal));
        var kernelTree = compilation.SyntaxTrees.First(static t => t.FilePath.EndsWith("CrossFileKernels.cu.cs", StringComparison.Ordinal));
        var kernelModel = compilation.GetSemanticModel(kernelTree);

        var outerDecl = libTree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static m => m.Identifier.ValueText == "Outer");

        var annotatedBody = LoweringAnnotations.AnnotateOriginalSpans(
            outerDecl.Body ?? throw new InvalidOperationException("Outer has no body"),
            compilation);

        var forEach = annotatedBody.DescendantNodes()
            .OfType<ForEachStatementSyntax>()
            .Single(static f => f.Expression is IdentifierNameSyntax { Identifier.ValueText: "it" });

        var resolved = IteratorTypeResolver.TryResolveIteratorSource(
            forEach.Expression,
            kernelModel,
            out var invocation,
            out var iteratorMethod);

        Assert.True(resolved);
        Assert.NotNull(invocation);
        Assert.NotNull(iteratorMethod);
        Assert.Equal("Inner", iteratorMethod.Name);
        Assert.True(IteratorTypeResolver.IsIteratorForEachSource(forEach.Expression, kernelModel));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Generator_CrossFileIteratorEnumeratedThroughLocal_DoesNotEmitUndeclaredLoopVariable()
    {
        const string LibSource = """
            using System.Collections.Generic;
            using Kassyi.GSharp.Core;

            public static partial class IterLib
            {
                [GpuDeviceFunction]
                public static IEnumerable<int> Inner(int n)
                {
                    yield return n;
                    yield return n + 1;
                }

                [GpuDeviceFunction]
                public static IEnumerable<int> Outer(int n)
                {
                    var it = Inner(n);
                    foreach (var x in it)
                    {
                        yield return x * 10;
                    }
                }
            }
            """;

        const string KernelSource = """
            using Kassyi.GSharp.Core;

            public static partial class CrossFileKernels
            {
                [GpuKernel]
                public static void SumKernel(int[] output)
                {
                    int tid = ThreadIndex.X;
                    int sum = 0;
                    foreach (var v in IterLib.Outer(tid))
                    {
                        sum += v;
                    }
                    output[tid] = sum;
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilationAt(
            ("IterLib.cs", LibSource),
            ("CrossFileKernels.cu.cs", KernelSource));

        var (driver, output, diagnostics) = GeneratorTestRunner.RunGenerator(compilation, "Cpu");
        var generatedTrees = driver.GetRunResult().GeneratedTrees;
        var combinedGenerated = string.Join("\n", generatedTrees.Select(static t => t.GetText().ToString()));

        Assert.NotEmpty(combinedGenerated);
        Assert.Contains("__gsharp_il_1_1_it_arg_0", combinedGenerated);
        Assert.DoesNotContain(": __gsharp_il_1_1_it)", combinedGenerated);
    }

    private static ITypeSymbol CandidateType(string parameterName) =>
        Method("ElementTypes", "Candidates").Parameters.Single(p => p.Name == parameterName).Type;

    private static IMethodSymbol Method(string typeName, string methodName)
    {
        var compilation = s_compilation.Value;
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var type = tree.GetRoot().DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Single(t => t.Identifier.ValueText == typeName);
        var symbol = model.GetDeclaredSymbol(type) ?? throw new InvalidOperationException(typeName);
        return symbol.GetMembers(methodName).OfType<IMethodSymbol>().Single();
    }

    private static MethodDeclarationSyntax Declaration(IMethodSymbol method) =>
        (MethodDeclarationSyntax)method.DeclaringSyntaxReferences.Single().GetSyntax();
}
