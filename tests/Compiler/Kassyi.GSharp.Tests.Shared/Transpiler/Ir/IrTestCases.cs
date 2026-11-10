#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>Builds the IR of methods of the test case files, for the tests of the IR builder.</summary>
internal static class IrTestCases
{
    public const string BuilderCases = "Ir/IrBuilderCases.cs";
    public const string OrderCases = "Transpiler/EvaluationOrderCases.cs";
    public const string NamedArgumentCases = "DefaultAndNamedArguments.cu.cs";

    private static readonly ConcurrentDictionary<string, Compilation> s_compilations = new(StringComparer.Ordinal);

    public static Compilation Compile(string testCase) =>
        s_compilations.GetOrAdd(testCase,
            static path => RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(path)));

    public static IMethodSymbol FindMethod(Compilation compilation, string type, string name) =>
        (compilation.GetTypeByMetadataName(type) ??
         throw new InvalidOperationException($"Type '{type}' not found."))
        .GetMembers(name)
        .OfType<IMethodSymbol>()
        .Single();

    public static IrMethod Build(string testCase, string type, string name)
    {
        var compilation = Compile(testCase);
        return IrBuilder.TryBuild(FindMethod(compilation, type, name), compilation, CancellationToken.None) ??
               throw new InvalidOperationException($"No IR for {type}.{name}.");
    }

    public static IrMethod BuildCase(string name) => Build(BuilderCases, "IrCases.Ir", name);

    /// <summary>The initializer of the local named <c>result</c> in the body.</summary>
    public static IrExpression Result(IrMethod method) =>
        method.Body.Statements
            .OfType<IrLocalDeclaration>()
            .Single(static declaration => declaration.Variable.Name == "result")
            .Initializer ?? throw new InvalidOperationException("result has no initializer.");

    public static string PrintResult(string testCase, string type, string name) =>
        IrPrinter.Print(Result(Build(testCase, type, name)));

    public static string PrintCaseResult(string name) => IrPrinter.Print(Result(BuildCase(name)));

    /// <summary>The single statement of the body (or the statement at <paramref name="index"/>).</summary>
    public static IrStatement Statement(IrMethod method, int index = 0) => method.Body.Statements[index];
}
