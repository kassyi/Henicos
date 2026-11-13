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
                $"{KernelHostNames.GetPointerLaunchName(KernelHostNames.GetMemberKey(host, true))}({host.PointerParameterTypes})"
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
        // step: in is not written
        Assert.Equal(
            new LauncherParameter("", "global::System.Int32", "global::System.Int32", null),
            LauncherSignatures.GetParameter(shapes[5], LauncherKind.Managed));
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
            LauncherSignatures.GetParameter(shapes[11], LauncherKind.Pointer));
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

        var conflicts = LauncherSignatures.FindConflicts(kernels);

        var (scaleIndex, scalePtrIndex) = reversed ? (1, 0) : (0, 1);
        Assert.Equal(LauncherConflict.None, conflicts[scaleIndex]);
        Assert.Equal(new LauncherConflict(scaleIndex, "LaunchScalePtr(global::System.IntPtr)"), conflicts[scalePtrIndex]);
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

        var conflicts = LauncherSignatures.FindConflicts(kernels);

        Assert.Equal(
            [LauncherConflict.None, new LauncherConflict(0, "LaunchAPtr(global::System.Int32)"), LauncherConflict.None],
            conflicts.ToArray());
    }

    private static KernelHostModel Host(string name, int ordinal, string managed, string pointer) =>
        new()
        {
            Name = name,
            OverloadOrdinal = ordinal,
            ManagedParameterTypes = managed,
            PointerParameterTypes = pointer,
            TypedPointerParameterTypes = pointer
        };

    // The kernel's parameters of a generated launcher, without the launch options, as LauncherSignatures compares them
    private static string FormatGeneratedParameters(IMethodSymbol method) =>
        string.Join(", ", method.Parameters
            .Take(method.Parameters.Length - KernelHostNames.LaunchOptions.Length)
            .Select(static p => (p.RefKind is RefKind.None ? "" : "ref ") + p.Type.GetFullyQualifiedCsharpName()));
}
