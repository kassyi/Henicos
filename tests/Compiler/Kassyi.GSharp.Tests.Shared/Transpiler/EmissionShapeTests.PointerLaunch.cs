using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

// The Launch{Kernel}Ptr launcher takes each array as a GSharpArrayProxy and passes its length to the kernel (#720)
// (TestCases/Transpiler/Emission/PointerLaunchShapeKernels.cu.cs)
public partial class EmissionShapeTests
{
    private const string ArrayProxyType = "global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy";

    // The plan: arrays and spans become proxies (optional or not), a ref parameter stays an address, a by-value struct
    // its shadow struct and a scalar its value
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PointerLaunchPlan_ClassifiesEveryKindOfParameter()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Transpiler/Emission/PointerLaunchShapeKernels.cu.cs"));
        var kernel = compilation.GetTypeByMetadataName("Kassyi.GSharp.Tests.TestCases.Emission.PointerLaunchShapeKernels")
            ?.GetMembers("EveryKindKernel")
            .OfType<IMethodSymbol>()
            .Single();
        Assert.NotNull(kernel);

        var kinds = kernel.Parameters
            .Select(static p => PointerLaunchPlan.Classify(KernelParameterShape.Classify(p)))
            .ToArray();

        Assert.Equal(
            [
                PointerArgumentKind.Array,
                PointerArgumentKind.OptionalArray,
                PointerArgumentKind.Array,
                PointerArgumentKind.Reference,
                PointerArgumentKind.ShadowStruct,
                PointerArgumentKind.Value
            ],
            kinds
        );
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PointerLauncher_TakesArraysAsProxiesAndPassesTheirLength()
    {
        var launcher = PointerLauncher(Generate("PointerLaunchShapeKernels", "Cuda"), "LaunchEveryKindKernelPtr");

        Assert.Contains(
            $"LaunchEveryKindKernelPtr({ArrayProxyType} values, {ArrayProxyType} extra, {ArrayProxyType} words, global::System.IntPtr total, ",
            launcher,
            StringComparison.Ordinal
        );
        foreach (var name in (string[])["values", "extra", "words"])
        {
            Assert.Contains($"[0] = {name}.Data;", launcher, StringComparison.Ordinal);
            Assert.Contains($" + 1))[0] = {name}.Length;", launcher, StringComparison.Ordinal);
        }

        // The reserved word after the length is 0 for every array; the caller's Flags never reach the device
        foreach (var ordinal in (int[])[0, 1, 2])
        {
            Assert.Contains($"((int*)(__gsharp_proxy_{ordinal} + 1))[1] = 0;", launcher, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(".Flags", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("Length is unknown", launcher, StringComparison.Ordinal);
    }

    // The checks of a proxy, before the launch: one with a negative length, or with no data and a length, describes no
    // array; a null or empty proxy is passed as it is, optional or not, as a C# call passes a null or empty array
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void PointerLauncher_RejectsOnlyProxiesThatDescribeNoArray()
    {
        var launcher = PointerLauncher(Generate("PointerLaunchShapeKernels", "Cuda"), "LaunchEveryKindKernelPtr");
        var launch = launcher.IndexOf("CudaContextScope __gsharp", StringComparison.Ordinal);

        foreach (var name in (string[])["values", "extra", "words"])
        {
            AssertCheckBeforeLaunch(launcher, launch,
                $"if ({name}.Length < 0){{",
                $"throw new global::System.ArgumentOutOfRangeException(\"{name}\", {name}.Length, " +
                "\"The array length must not be negative.\");",
                "}",
                $"if ({name}.Data == global::System.IntPtr.Zero && {name}.Length != 0){{",
                "throw new global::System.ArgumentException(" +
                $"\"A null array has no elements, so its length must be 0.\", \"{name}\");",
                "}");
        }

        Assert.DoesNotContain("ArgumentNullException", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("= default;", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("total.Length", launcher, StringComparison.Ordinal);
    }

    // The typed launcher of an overloaded kernel forwards an array buffer with its length and a ref buffer as its
    // address; an optional buffer may be null
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void TypedPointerLauncher_ForwardsArrayBuffersAsProxies()
    {
        var generated = Generate("PointerLaunchShapeKernels", "Cuda");

        Assert.Contains(
            "(output.ToProxy(), marker.DevicePointer, optional?.ToProxy() ?? default, ",
            generated,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("output.DevicePointer", generated, StringComparison.Ordinal);
    }

    private static string PointerLauncher(string generated, string name)
    {
        var start = generated.IndexOf($" {name}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{name} is not generated.");
        return generated[start..generated.IndexOf("#pragma warning restore CS8500", start, StringComparison.Ordinal)];
    }

    private static void AssertCheckBeforeLaunch(string launcher, int launch, params string[] lines)
    {
        var expected = string.Join("\n", lines);
        var at = launcher.IndexOf(expected, StringComparison.Ordinal);
        Assert.True(at >= 0, "Missing lines:\n" + expected + "\nin:\n" + launcher[..Math.Min(launcher.Length, 3000)]);
        Assert.True(at < launch, "The check comes after the launch begins:\n" + expected);
    }
}
