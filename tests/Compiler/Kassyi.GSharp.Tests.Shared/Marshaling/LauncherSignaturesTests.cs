using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

/// <summary>
/// Verifies the launcher signatures that the host model plans: the parameter types the launchers are written with
/// are the ones the duplicate check (GSH0059) compares, and the check picks the same kernel whatever the order of the
/// kernels.
/// </summary>
[Category(TestCategory.Compiler)]
public class LauncherSignaturesTests
{
    private const string Proxy = "global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy";
    private const string Ints = "global::System.Int32[]";

    private static readonly string s_source = RoslynTestUtility.ReadTestCase("Marshaling/LauncherSignatureSource.cs");

    // The parameter types of each generated launcher, read back from the generated method symbols, are the ones the
    // plan compares
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void FormatParameterTypes_EveryParameterKind_MatchesTheGeneratedLaunchers()
    {
        var (_, output, _) = GeneratorTestRunner.RunGenerator(s_source, "Cuda");
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        var kernels = output.GetTypeByMetadataName("Signatures.Kernels") ?? throw new InvalidOperationException();
        var generated = kernels.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(static m => m.Name.StartsWith("Launch", StringComparison.Ordinal))
            .Select(static m => $"{m.Name}({FormatGeneratedParameters(m)})")
            .ToHashSet(StringComparer.Ordinal);

        var hosts = kernels.GetMembers("Mixed")
            .OfType<IMethodSymbol>()
            .Where(static m => m.HasAttribute(KnownAttributes.GpuKernel))
            .Select(static m => KernelHostModel.Create(m, default))
            .ToList();
        Assert.Equal(2, hosts.Count);
        var expected = hosts
            .SelectMany(static host => new[]
            {
                $"LaunchMixed({host.ManagedParameterTypes})",
                $"LaunchMixedPtr({host.TypedPointerParameterTypes})",
                KernelHostNames.GetPointerLaunchName(KernelHostNames.GetMemberKey(host, true)) +
                $"({host.PointerParameterTypes})"
            })
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            string.Join('\n', expected.Order(StringComparer.Ordinal)),
            string.Join('\n', generated.Order(StringComparer.Ordinal)));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetParameter_ParameterKinds_WritesTheLauncherTypes()
    {
        var compilation = RoslynTestUtility.CreateCompilation(s_source);
        var mixed = compilation.GetTypeByMetadataName("Signatures.Kernels")?.GetMembers("Mixed")
            .OfType<IMethodSymbol>()
            .Single(static m => m.Parameters.Length > 1) ?? throw new InvalidOperationException();
        var shapes = mixed.Parameters.Select(KernelParameterShape.Classify).ToList();

        // optional: a nullable reference is written with its ?, which C# does not compare
        Assert.Equal(
            new LauncherParameter("", "global::System.Single[]?", "global::System.Single[]", null),
            LauncherSignatures.GetParameter(shapes[1], LauncherKind.Managed));
        // total: out is written, compared as any by-reference parameter
        Assert.Equal(
            new LauncherParameter("out", "global::System.Int64", "ref global::System.Int64", null),
            LauncherSignatures.GetParameter(shapes[4], LauncherKind.Managed));
        // step: an in beside a ref of its type is the caller's variable, written with in and compared as a by-reference
        // parameter
        Assert.Equal(
            new LauncherParameter("in", "global::System.Int32", "ref global::System.Int32", null),
            LauncherSignatures.GetParameter(shapes[5], LauncherKind.Managed));
        // narrow: any other in is a value, so it is not written
        Assert.Equal(
            new LauncherParameter("", "global::System.Int16", "global::System.Int16", null),
            LauncherSignatures.GetParameter(shapes[11], LauncherKind.Managed));
        Assert.Equal(
            new LauncherParameter("", "global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy?",
                "global::System.Nullable<global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy>", null),
            LauncherSignatures.GetParameter(shapes[2], LauncherKind.Pointer));
        Assert.Equal(
            new LauncherParameter("", "global::System.IntPtr", "global::System.IntPtr", null),
            LauncherSignatures.GetParameter(shapes[8], LauncherKind.TypedPointer));
        // optional: a typed buffer that takes null, compared without its ?
        Assert.Equal(
            new LauncherParameter("", "global::Kassyi.GSharp.Runtime.GpuBuffer<global::System.Single>?",
                "global::Kassyi.GSharp.Runtime.GpuBuffer<global::System.Single>", null),
            LauncherSignatures.GetParameter(shapes[1], LauncherKind.TypedPointer));
        Assert.Equal(
            new LauncherParameter("", "global::System.Int32", "global::System.Int32", "3"),
            LauncherSignatures.GetParameter(shapes[12], LauncherKind.Pointer));
    }

    // Scale and ScalePtr both produce LaunchScalePtr(IntPtr); the kernel whose name sorts first keeps it, in any order
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void FindConflicts_KernelsInEitherOrder_ReportsTheKernelThatSortsLater(bool reversed)
    {
        var scale = Host("Scale", -1, managed: "global::System.Int32", pointer: "global::System.IntPtr");
        var scalePtr = Host("ScalePtr", -1, managed: "global::System.IntPtr", pointer: "global::System.IntPtr");
        KernelHostModel[] kernels = reversed ? [scalePtr, scale] : [scale, scalePtr];

        var conflicts = LauncherSignatures.FindConflicts(kernels, emitsSource: false);

        var (scaleIndex, scalePtrIndex) = reversed ? (1, 0) : (0, 1);
        Assert.Equal(LauncherConflict.None, conflicts[scaleIndex]);
        Assert.Equal(
            new LauncherConflict(scaleIndex, "LaunchScalePtr(global::System.IntPtr)"),
            conflicts[scalePtrIndex]);
    }

    // A kernel with a conflict claims nothing: LaunchAPtrPtr of the third kernel only matches the excluded second one
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FindConflicts_ConflictWithAnExcludedKernel_KeepsTheLaterKernel()
    {
        KernelHostModel[] kernels =
        [
            Host("A", -1, managed: "global::System.Int32", pointer: "global::System.Int32"),
            Host("APtr", -1, managed: "global::System.Int32", pointer: "global::System.Int64"),
            Host("APtrPtr", -1, managed: "global::System.Int64", pointer: "global::System.Int16")
        ];

        var conflicts = LauncherSignatures.FindConflicts(kernels, emitsSource: false);

        Assert.Equal(
            [LauncherConflict.None, new LauncherConflict(0, "LaunchAPtr(global::System.Int32)"), LauncherConflict.None],
            conflicts.ToArray());
    }

    // The constant {key}_Source exists only with GSharpEmitSource, so it collides only then
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void FindConflicts_SourceConstant_CollidesOnlyWhenEmitted(bool emitsSource)
    {
        KernelHostModel[] kernels =
        [
            Host("LaunchX", -1, managed: Ints, pointer: Proxy),
            Host("X_Source", -1, managed: Ints, pointer: Proxy)
        ];

        var conflicts = LauncherSignatures.FindConflicts(kernels, emitsSource);

        Assert.Equal(LauncherConflict.None, conflicts[0]);
        Assert.Equal(emitsSource ? new LauncherConflict(0, "LaunchX_Source") : LauncherConflict.None, conflicts[1]);
    }

    // A kernel's own method is there from the start: the generated member that meets it is reported, whatever the order
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FindConflicts_GeneratedMemberMeetsAnOwnMethod_ReportsTheGeneratingKernel()
    {
        KernelHostModel[] kernels =
        [
            Host("WarmupFunctionPointerFoo", -1, managed: "", pointer: ""),
            Host("Foo", -1, managed: Ints, pointer: Proxy)
        ];

        var conflicts = LauncherSignatures.FindConflicts(kernels, emitsSource: false);

        Assert.Equal(
            [LauncherConflict.None, new LauncherConflict(0, "WarmupFunctionPointerFoo()")],
            conflicts.ToArray());
    }

    // A kernel named like a member of every kernel class collides with the class, not with a kernel
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FindConflicts_KernelNamedAotContexts_ReportsAClassMember()
    {
        KernelHostModel[] kernels =
            [Host("AotContexts", -1, managed: "global::System.Int32", pointer: "global::System.Int32")];

        var conflict = Assert.Single(LauncherSignatures.FindConflicts(kernels, emitsSource: false));

        Assert.Equal(new LauncherConflict(LauncherConflict.ClassMember, "AotContexts"), conflict);
        Assert.True(conflict.IsClassMember);
    }

    // The launch options that a launcher takes after the parameters of its kernel, as the plan spells them, are the
    // trailing parameters of the generated launcher: the plan compares a kernel of its own against them
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void LaunchOptionTypes_GeneratedLauncher_AreTheTrailingParameters()
    {
        var (_, output, _) = GeneratorTestRunner.RunGenerator(s_source, "Cpu");
        var kernels = output.GetTypeByMetadataName("Signatures.Kernels") ?? throw new InvalidOperationException();
        var launcher = kernels.GetMembers("LaunchMixed")
            .OfType<IMethodSymbol>()
            .First(static m => m.Parameters.Length > KernelHostNames.LaunchOptions.Length);

        var trailing = launcher.Parameters
            .Skip(launcher.Parameters.Length - KernelHostNames.LaunchOptions.Length)
            .Select(static p => p.Type.GetFullyQualifiedCsharpName());

        Assert.Equal(LauncherSignatures.LaunchOptionTypes, string.Join(", ", trailing));
    }

    // Foo(int[]) generates LaunchFoo(int[], <launch options>), which C# keeps beside a kernel LaunchFoo(int[]);
    // the same without parameters, and for the pointer launcher
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Foo", "global::System.Int32[]", "LaunchFoo")]
    [InlineData("Foo", "", "LaunchFoo")]
    [InlineData("Foo", "ref global::System.Int32", "LaunchFooPtr")]
    public void FindConflicts_KernelNamedLikeALauncher_ReportsNothing(string kernel, string parameters,
        string lookalike)
    {
        var isReference = parameters.StartsWith("ref", StringComparison.Ordinal);
        var pointer = isReference ? "global::System.IntPtr" : parameters.Length == 0 ? "" : Proxy;
        var own = isReference ? "global::System.IntPtr" : parameters;
        KernelHostModel[] kernels =
            [Host(kernel, -1, managed: parameters, pointer: pointer), Host(lookalike, -1, managed: own, pointer: own)];

        var conflicts = LauncherSignatures.FindConflicts(kernels, emitsSource: false);

        Assert.All(conflicts.ToArray(), static conflict => Assert.Equal(LauncherConflict.None, conflict));
    }

    // The warmup method takes no parameters and no launch options: a kernel of that name collides with it, and the
    // kernel that generates the warmup method is the one reported
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FindConflicts_KernelNamedLikeTheWarmupMethod_StillReportsIt()
    {
        KernelHostModel[] kernels =
        [
            Host("Foo", -1, managed: Ints, pointer: Proxy),
            Host("WarmupFunctionPointerFoo", -1, managed: "", pointer: "")
        ];

        var conflicts = LauncherSignatures.FindConflicts(kernels, emitsSource: false);

        Assert.Equal(new LauncherConflict(1, "WarmupFunctionPointerFoo()"), conflicts[0]);
        Assert.Equal(LauncherConflict.None, conflicts[1]);
    }

    private static KernelHostModel Host(string name, int ordinal, string managed, string pointer) =>
        new()
        {
            Name = name,
            OverloadOrdinal = ordinal,
            ManagedParameterTypes = managed,
            PointerParameterTypes = pointer,
            TypedPointerParameterTypes = pointer,
            OwnParameterTypes = managed
        };

    // The kernel's parameters of a generated launcher, without the launch options, as LauncherSignatures compares them
    private static string FormatGeneratedParameters(IMethodSymbol method) =>
        string.Join(", ", method.Parameters
            .Take(method.Parameters.Length - KernelHostNames.LaunchOptions.Length)
            .Select(static p => (p.RefKind is RefKind.None ? "" : "ref ") + p.Type.GetFullyQualifiedCsharpName()));
}
