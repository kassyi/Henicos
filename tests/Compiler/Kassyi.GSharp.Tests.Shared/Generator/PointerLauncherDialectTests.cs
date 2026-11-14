namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// Verifies that the CPU dialect emits the same launchers as the CUDA dialect: <c>Launch{Name}</c>, the untyped
/// <c>Launch{Kernel}Ptr</c> and the typed <c>Launch{Name}Ptr</c> of an overloaded kernel, with the same parameter types
/// and accessibility, so that code calling them builds with either dialect.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class PointerLauncherDialectTests
{
    // Every kind of pointer launcher parameter (arrays, a nullable array-like value, ref / out, structs whose layouts
    // agree or differ, bool and char, an auto result), and overloads whose untyped pointer launchers are private
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Transpiler/Emission/PointerLaunchShapeKernels.cu.cs")]
    [InlineData("Generator/KernelOverload_Overloaded.cu.cs")]
    public void Launchers_CpuDialect_MatchTheCudaLaunchers(string testCase)
    {
        var source = RoslynTestUtility.ReadTestCase(testCase);

        var cpu = LaunchMembers(source, "Cpu");
        var cuda = LaunchMembers(source, "Cuda");

        Assert.Contains(cpu, static m => m.Contains("Ptr(", StringComparison.Ordinal));
        Assert.Equal(cuda, cpu);
    }

    // The overloads: C# picks one through the public typed launcher; the untyped one behind it stays private
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void OverloadedKernels_EitherDialect_ExposeOnlyTheTypedPointerLaunchers(string dialect)
    {
        var members = LaunchMembers(RoslynTestUtility.ReadTestCase("Generator/KernelOverload_Overloaded.cu.cs"), dialect);

        Assert.Contains(members, static m => m.StartsWith(
            "Public OverloadTest.K.LaunchRunPtr(None Kassyi.GSharp.Runtime.GpuBuffer<int>",
            StringComparison.Ordinal));
        Assert.Contains(members, static m => m.StartsWith(
            "Public OverloadTest.K.LaunchRunPtr(None Kassyi.GSharp.Runtime.GpuBuffer<float>",
            StringComparison.Ordinal));
        Assert.Equal(
            2,
            members.Count(static m => m.StartsWith("Private ", StringComparison.Ordinal) &&
                                      m.Contains("Ptr(None Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy",
                                          StringComparison.Ordinal))
        );
    }

    // "<accessibility> <type>.<name>(<ref kind> <type>, ...)" of each Launch* method of the output, sorted, after
    // checking that the output compiles
    private static List<string> LaunchMembers(string source, string dialect)
    {
        var (_, output, diagnostics) = GeneratorTestRunner.RunGenerator(source, dialect);
        var errors = diagnostics
            .Concat(output.GetDiagnostics())
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));

        var members = new List<string>();
        CollectLaunchMembers(output.Assembly.GlobalNamespace, members);
        members.Sort(StringComparer.Ordinal);
        return members;
    }

    private static void CollectLaunchMembers(INamespaceOrTypeSymbol container, List<string> members)
    {
        foreach (var member in container.GetMembers())
        {
            switch (member)
            {
                case INamespaceOrTypeSymbol nested:
                    CollectLaunchMembers(nested, members);
                    break;
                case IMethodSymbol { IsStatic: true } method when method.Name.StartsWith("Launch", StringComparison.Ordinal):
                    var parameters = method.Parameters.Select(static p => $"{p.RefKind} {p.Type.ToDisplayString()}");
                    members.Add(
                        $"{method.DeclaredAccessibility} {method.ContainingType.ToDisplayString()}.{method.Name}" +
                        $"({string.Join(", ", parameters)}) -> {method.ReturnType.ToDisplayString()}");
                    break;
            }
        }
    }
}
