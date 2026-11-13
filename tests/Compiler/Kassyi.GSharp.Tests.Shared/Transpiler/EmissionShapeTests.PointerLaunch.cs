using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

// The Launch{Kernel}Ptr launcher takes each array as a GSharpArrayProxy and passes its length to the kernel (#720);
// the CPU dialect emits it with the CUDA signature and calls the native launcher through a function pointer
// (TestCases/Transpiler/Emission/PointerLaunchShapeKernels.cu.cs)
public partial class EmissionShapeTests
{
    private const string ArrayProxyType = "global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy";
    private const string PointerShapes = "global::Kassyi.GSharp.Tests.TestCases.Emission.PointerLaunchShapeKernels";

    // The plan: arrays and spans become proxies (nullable or not; only a Nullable<array-like> becomes a proxy?), a ref
    // parameter stays an address, a by-value struct its shadow struct and a scalar its value
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
                PointerArgumentKind.Array,
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
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cuda", "CudaContextScope __gsharp")]
    [InlineData("Cpu", "CpuKernelLoader.LoadAotKernelPtr")]
    public void PointerLauncher_RejectsOnlyProxiesThatDescribeNoArray(string dialect, string launchStart)
    {
        var launcher = PointerLauncher(Generate("PointerLaunchShapeKernels", dialect), "LaunchEveryKindKernelPtr");
        var launch = launcher.IndexOf(launchStart, StringComparison.Ordinal);
        Assert.True(launch >= 0, $"{launchStart} is not in the launcher.");

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
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void TypedPointerLauncher_ForwardsArrayBuffersAsProxies(string dialect)
    {
        var generated = Generate("PointerLaunchShapeKernels", dialect);

        Assert.Contains(
            "(output.ToProxy(), marker.DevicePointer, optional?.ToProxy() ?? default, ",
            generated,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("output.DevicePointer", generated, StringComparison.Ordinal);
    }

    // The CPU pointer launcher has the CUDA signature, so that code calling it builds with either dialect
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("LaunchEveryKindKernelPtr")]
    [InlineData("LaunchNativeFormsKernelPtr")]
    public void CpuPointerLauncher_HasTheCudaSignature(string name)
    {
        var cpu = SignatureLine(Generate("PointerLaunchShapeKernels", "Cpu"), name);
        var cuda = SignatureLine(Generate("PointerLaunchShapeKernels", "Cuda"), name);

        Assert.StartsWith("public static unsafe ", cpu, StringComparison.Ordinal);
        Assert.Equal(cuda, cpu);
    }

    // The CPU pointer launcher calls the native launcher export through an unmanaged function pointer, typed by the
    // pointer plan: the data and length of the caller's proxies, a ref as the caller's address, a by-value struct as the address
    // of its shadow parameter
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CpuPointerLauncher_CallsTheNativeLauncherThroughAFunctionPointer()
    {
        var launcher = PointerLauncher(Generate("PointerLaunchShapeKernels", "Cpu"), "LaunchEveryKindKernelPtr");
        const string EveryKindPointer = "delegate* unmanaged[Cdecl]<uint, uint, uint, uint, uint, uint, uint, long, " +
            $"{ArrayProxyType}, {ArrayProxyType}, {ArrayProxyType}, global::System.Int32*, {PointerShapes}.AotContexts.Pair*, " +
            "global::System.Int32, void>";

        AssertContainsLines(launcher.ToString(),
            $"{EveryKindPointer} __gsharp_hfunc = ({EveryKindPointer})global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.LoadAotKernelPtr(" +
            $"\"__gsharp_launch_{Ns}PointerLaunchShapeKernels__EveryKindKernel\", __gsharp_dll_path);");
        AssertContainsLines(launcher.ToString(),
            "__gsharp_hfunc(gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ, sharedMemBytes, " +
            $"__gsharp_launch_block_count, {Proxy("values")}, {Proxy("extra")}, {Proxy("words")}, " +
            "(global::System.Int32*)total, &pair, count);",
            "global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.ThrowIfNativeError(__gsharp_dll_path);");
        Assert.DoesNotContain("LoadAotKernel<", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("fixed (", launcher, StringComparison.Ordinal);
    }

    // The forms of their own: a Nullable<array-like> by the address of a copy of the caller's value, a ref struct whose
    // layouts differ as the address of its shadow, bool and char in the widths of the C++ parameters, the auto result
    // pinned
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CpuPointerLauncher_PassesEachNativeForm()
    {
        var launcher = PointerLauncher(Generate("PointerLaunchShapeKernels", "Cpu"), "LaunchNativeFormsKernelPtr");

        AssertContainsLines(launcher.ToString(),
            "fixed (global::System.Int32* p___gsharp_auto_result = __gsharp_auto_result)",
            "{",
            $"global::System.Nullable<{ArrayProxyType}> __gsharp_nullable_0 = memory.HasValue ? {Proxy("memory.Value")} : null;",
            "__gsharp_hfunc(gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ, sharedMemBytes, " +
            $"__gsharp_launch_block_count, &__gsharp_nullable_0, ({PointerShapes}.AotContexts.Shuffled*)shuffled, " +
            "&byValue, (global::System.Int32*)count, flag ? (byte)1 : (byte)0, (ushort)letter, p___gsharp_auto_result);");
        Assert.Contains(
            $"global::System.Nullable<{ArrayProxyType}>*, {PointerShapes}.AotContexts.Shuffled*, " +
            $"{PointerShapes}.AotContexts.Shuffled*, global::System.Int32*, byte, ushort, global::System.Int32*, void>",
            launcher,
            StringComparison.Ordinal
        );
        Assert.Contains("return __gsharp_auto_result;", launcher, StringComparison.Ordinal);
    }

    // C# execution runs Launch{Kernel} only, as on CUDA; the managed CPU path throws before the native library is
    // loaded
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CpuPointerLauncher_ManagedAndCSharpExecution_Throw()
    {
        var launcher = PointerLauncher(Generate("PointerLaunchShapeKernels", "Cpu"), "LaunchEveryKindKernelPtr");
        var load = launcher.IndexOf("LoadAotKernelPtr", StringComparison.Ordinal);

        AssertCheckBeforeLaunch(launcher, load,
            "if (global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.ShouldUseManagedExecution(__gsharp_dll_path))",
            "{",
            "throw new global::System.NotSupportedException(\"LaunchEveryKindKernelPtr runs only on the native CPU path");
        var cannotRun = launcher.IndexOf("CSharpKernelRunner.CannotRun(", StringComparison.Ordinal);
        Assert.True(cannotRun >= 0 && cannotRun < load, "The C# execution branch does not throw before the native call.");
        Assert.DoesNotContain("__gsharp_statemachine_", launcher, StringComparison.Ordinal);
    }

    // The proxy the CPU pointer launcher passes for the caller's <proxy>: its data and length, with the reserved
    // Flags 0 as every launcher writes them
    private static string Proxy(string proxy) =>
        $"new {ArrayProxyType} {{ Data = {proxy}.Data, Length = {proxy}.Length }}";

    // The declaration line of the launcher <name>
    private static string SignatureLine(string generated, string name)
    {
        var start = generated.IndexOf($" {name}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{name} is not generated.");
        var lineStart = generated.LastIndexOf('\n', start) + 1;
        return generated[lineStart..generated.IndexOf('\n', start)];
    }

    private static ReadOnlySpan<char> PointerLauncher(string generated, string name)
    {
        var start = generated.IndexOf($" {name}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{name} is not generated.");
        return LauncherFrom(generated, start);
    }

    private static void AssertCheckBeforeLaunch(ReadOnlySpan<char> launcher, int launch, params string[] lines)
    {
        var expected = string.Join("\n", lines);
        var at = launcher.IndexOf(expected, StringComparison.Ordinal);
        Assert.True(
            at >= 0,
            "Missing lines:\n" + expected + "\nin:\n" + launcher[..Math.Min(launcher.Length, 3000)].ToString()
        );
        Assert.True(at < launch, "The check comes after the launch begins:\n" + expected);
    }
}
