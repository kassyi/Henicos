#nullable enable
using Kassyi.GSharp.Compiler.Extensions;
namespace Kassyi.GSharp.Tests.Shared.Analysis;

/// <summary>
/// Verifies which device functions get a managed version (<see cref="ManagedVersionAnalysis"/>, #711) and that the
/// managed versions call the managed versions of the functions they name.
/// </summary>
[Category(TestCategory.Compiler)]
public class ManagedVersionAnalysisTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Analysis/ManagedVersionCases.cs")
    );

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Functions", "Direct")]
    [InlineData("Functions", "Transitive")]
    [InlineData("Functions", "TwoLevels")]
    [InlineData("Functions", "AddAt")]
    [InlineData("Functions", "PassesMethodGroup")]
    [InlineData("Functions", "CallsExtension")]
    [InlineData("Extensions", "Doubled")]
    public void Test_Needs_ReachesAnOperatorMacro_IsTrue(string type, string name)
    {
        Assert.True(ManagedVersionAnalysis.Needs(GetMethod(type, name), s_compilation));
        Assert.True(ManagedVersionAnalysis.HasManagedVersion(GetMethod(type, name), s_compilation));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Plain")]
    [InlineData("CallsPlain")]
    [InlineData("Add")]
    public void Test_Needs_ReachesNoOperatorMacro_IsFalse(string name)
    {
        Assert.False(ManagedVersionAnalysis.Needs(GetMethod("Functions", name), s_compilation));
        Assert.False(ManagedVersionAnalysis.HasManagedVersion(GetMethod("Functions", name), s_compilation));
    }

    // Only the queried function is cached: a member of a cycle that a walk visits second must not be recorded as false
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Tick", true)]
    [InlineData("Tock", true)]
    [InlineData("Ping", false)]
    [InlineData("Pong", false)]
    public void Test_Needs_Cycle_FollowsWhetherTheCycleReachesAMacro(string name, bool expected)
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analysis/ManagedVersionCases.cs"));
        var other = name switch
        {
            "Tick" => "Tock",
            "Tock" => "Tick",
            "Ping" => "Pong",
            _ => "Ping"
        };

        // The other member first, then this one, in a fresh compilation
        Assert.Equal(expected, ManagedVersionAnalysis.Needs(GetMethod(compilation, "Functions", other), compilation));
        Assert.Equal(expected, ManagedVersionAnalysis.Needs(GetMethod(compilation, "Functions", name), compilation));
    }

    // A function that suspends is rewritten in its state machine
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Needs_SuspendingFunction_IsFalse()
    {
        var method = GetMethod("Functions", "Suspends");

        Assert.True(SuspensionAnalysis.IsSuspendingFunction(method, s_compilation));
        Assert.False(ManagedVersionAnalysis.Needs(method, s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_HasDirectRewrite_OnlyForTheBodyItself()
    {
        Assert.True(HasDirectRewrite("Direct"));
        Assert.True(HasDirectRewrite("Suspends"));
        Assert.False(HasDirectRewrite("Transitive"));
        Assert.False(HasDirectRewrite("Plain"));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ManagedVersion_CallsTheVersionOfACallee()
    {
        var source = GenerateManagedVersion("Functions", "Transitive");

        Assert.Contains(
            $"public static int {ManagedName("Functions", "Transitive")}(ref global::ManagedVersionCases.Ctx ctx, int a) => global::ManagedVersionCases.Functions.{ManagedName("Functions", "Direct")}(ref ctx, a) + 1;",
            source);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ManagedVersion_ExpandsTheOperatorMacro()
    {
        var source = GenerateManagedVersion("Functions", "Direct");

        Assert.Contains("global::ManagedVersionCases.Functions.Add(ref ctx, ref z, x, x);", source);
        Assert.DoesNotContain("z = x + x;", source);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ManagedVersion_PassesTheVersionOfAMethodGroup()
    {
        var source = GenerateManagedVersion("Functions", "PassesMethodGroup");

        Assert.Contains(
            $"global::Kassyi.GSharp.Core.Gpu.ForUnroll(0, 2, global::ManagedVersionCases.Functions.{ManagedName("Functions", "AddAt")});",
            source);
    }

    // The reduced call ctx.Doubled(a) becomes the static call of the version, which has no 'this'
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ManagedVersion_CallsTheVersionOfAReducedExtensionCall()
    {
        var caller = GenerateManagedVersion("Functions", "CallsExtension");
        var extension = GenerateManagedVersion("Extensions", "Doubled");

        Assert.Contains(
            $"global::ManagedVersionCases.Extensions.{ManagedName("Extensions", "Doubled")}(ref ctx, a)", caller);
        Assert.Contains(
            $"public static int {ManagedName("Extensions", "Doubled")}(ref global::ManagedVersionCases.Ctx ctx, int a)",
            extension);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ManagedVersion_OfACycle_CallsTheOtherVersion()
    {
        var source = GenerateManagedVersion("Functions", "Tick");

        Assert.Contains($"global::ManagedVersionCases.Functions.{ManagedName("Functions", "Tock")}(ref ctx, a - 1)", source);
        Assert.Contains("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]",
            source);
    }

    private static bool HasDirectRewrite(string name)
    {
        var method = GetMethod("Functions", name);
        Assert.True(method.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration));
        return ManagedVersionAnalysis.HasDirectRewrite(method, declaration, s_compilation);
    }

    private static string GenerateManagedVersion(string type, string name)
    {
        var method = GetMethod(type, name);
        Assert.True(method.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration));
        var plan = MethodEmissionPlan.Create(declaration, s_compilation.GetSemanticModel(declaration.SyntaxTree),
            isKernel: false);
        Assert.True(plan.HasManagedVersion);
        return ManagedCSharpTranspiler.GenerateManagedVersion(in plan);
    }

    private static string ManagedName(string type, string name) =>
        GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.ManagedVersion.MethodPrefix, GetMethod(type, name));

    private static IMethodSymbol GetMethod(string type, string name) => GetMethod(s_compilation, type, name);

    private static IMethodSymbol GetMethod(Compilation compilation, string type, string name) =>
        (compilation.GetTypeByMetadataName($"ManagedVersionCases.{type}") ??
         throw new InvalidOperationException($"Type '{type}' not found."))
        .GetMembers(name)
        .OfType<IMethodSymbol>()
        .First();
}
