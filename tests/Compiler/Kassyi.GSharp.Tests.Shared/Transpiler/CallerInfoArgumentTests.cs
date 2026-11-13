using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Verifies that <see cref="CallerInfoArgument"/> gives, for each kind of caller-info parameter, the value that C#
/// itself passes at the call: the test source is compiled with a <c>/pathmap</c> and run, and every call's value is
/// compared with what the compiler reads for the same call (#946).
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class CallerInfoArgumentTests
{
    private const string SourceDirectory = @"C:\work\project\";
    private const string SourcePath = SourceDirectory + @"Kernels\CallerInfo.cs";
    private const string MappedDirectory = "/_/";

    private static readonly CSharpCompilation s_compilation = CreateCompilation();

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetValue_EveryCallerInfoKind_MatchesTheValueCSharpPasses()
    {
        var expected = RunCompiledSource();
        var actual = ReadCompilerValues();

        Assert.Equal(expected, actual);
        Assert.Contains(MappedDirectory + "Kernels/CallerInfo.cs", expected);
        Assert.Contains("Run", expected);
        Assert.Contains("1 + 2", expected);
        Assert.Contains(700, expected);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetValue_ArgumentPassed_IsNull()
    {
        var tree = s_compilation.SyntaxTrees.Single();
        var model = s_compilation.GetSemanticModel(tree);
        var call = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(static c => c.ArgumentList.Arguments is [{ Expression: LiteralExpressionSyntax }]);
        var method = Assert.IsAssignableFrom<IMethodSymbol>(model.GetSymbolInfo(call).Symbol);

        Assert.Null(CallerInfoArgument.GetValue(method.Parameters[0], call, model));
    }

    // The value of each element of Run, as the compiled source computes it
    private static object?[] RunCompiledSource()
    {
        using var stream = new MemoryStream();
        var result = s_compilation.Emit(stream);
        Assert.True(
            result.Success,
            string.Join("\n", result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
        stream.Position = 0;
        var assembly = new AssemblyLoadContext($"CallerInfo_{Guid.NewGuid():N}", isCollectible: true)
            .LoadFromStream(stream);
#pragma warning disable RS0030 // Reflection to call an assembly that the test compiled at run time
        var run = assembly.GetType("CallerInfoCases.CallerInfoArgumentSource", throwOnError: true)
            ?.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(run);
        return Assert.IsType<object?[]>(run.Invoke(null, null));
#pragma warning restore RS0030
    }

    // The value the compiler reads for the omitted caller-info parameter of the outermost call of each element of Run
    private static object?[] ReadCompilerValues()
    {
        var tree = s_compilation.SyntaxTrees.Single();
        var model = s_compilation.GetSemanticModel(tree);
        var elements = tree.GetRoot()
            .DescendantNodes()
            .OfType<CollectionExpressionSyntax>()
            .Single()
            .Elements
            .Cast<ExpressionElementSyntax>();
        return elements
            .Select(element => element.Expression
                .DescendantNodesAndSelf()
                .First(static node => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax))
            .Select(call =>
            {
                var method = Assert.IsAssignableFrom<IMethodSymbol>(model.GetSymbolInfo(call).Symbol);
                return CallerInfoArgument.GetValue(method.Parameters[^1], call, model);
            })
            .ToArray();
    }

    private static CSharpCompilation CreateCompilation()
    {
        var compilation = RoslynTestUtility.CreateCompilationAt(
            SourcePath,
            RoslynTestUtility.ReadTestCase("Transpiler/CallerInfoArgumentSource.cs"));
        var resolver = new SourceFileResolver(
            ImmutableArray<string>.Empty,
            baseDirectory: null,
            [new KeyValuePair<string, string>(SourceDirectory, MappedDirectory)]);
        return compilation.WithOptions(compilation.Options
            .WithSourceReferenceResolver(resolver)
            .WithOutputKind(OutputKind.DynamicallyLinkedLibrary));
    }
}
