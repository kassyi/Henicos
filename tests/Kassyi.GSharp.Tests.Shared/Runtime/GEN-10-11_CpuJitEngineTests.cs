using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Tests;

public class CpuJitEngineTests : TranspilerTestBase
{
    private readonly ITestOutputHelper _output;

    public CpuJitEngineTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AddKernelDelegate(IntPtr a, IntPtr b, IntPtr result, int length);

    [SkippableFact]
    [Trait("FeatureId", "GEN-10")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "GpuExecution")]
    public void CpuJitEngine_CompileAndExecute_Success()
    {
        Skip.IfNot(IsCpuDialect, "CpuJitEngine tests only run under CPU dialect.");

        var source = """
            extern "C" __declspec(dllexport) void AddKernel(int* a, int* b, int* result, int length) {
                for (int i = 0; i < length; i++) {
                    result[i] = a[i] + b[i];
                }
            }
            """;

        var kernel = CpuJitEngine.GetOrCompileKernel<AddKernelDelegate>("AddKernel", source);
        Assert.NotNull(kernel);

        int length = 5;
        int[] a = [1, 2, 3, 4, 5];
        int[] b = [10, 20, 30, 40, 50];
        int[] result = new int[length];

        unsafe
        {
            fixed (int* pA = a, pB = b, pResult = result)
            {
                kernel((IntPtr)pA, (IntPtr)pB, (IntPtr)pResult, length);
            }
        }

        Assert.Equal([11, 22, 33, 44, 55], result);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MultiplyWithMacroDelegate(IntPtr data, int length);

    [SkippableFact]
    [Trait("FeatureId", "GEN-11")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "GpuExecution")]
    public void CpuJitEngine_WithJitConstants_EvaluatesCorrectly()
    {
        Skip.IfNot(IsCpuDialect, "CpuJitEngine tests only run under CPU dialect.");

        var source = """
            extern "C" __declspec(dllexport) void MultiplyKernel(int* data, int length) {
                for (int i = 0; i < length; i++) {
                    data[i] = data[i] * MULTIPLIER;
                }
            }
            """;

        var macros = new Dictionary<string, string> { { "MULTIPLIER", "5" } };
        var kernel = CpuJitEngine.GetOrCompileKernel<MultiplyWithMacroDelegate>("MultiplyKernel", source, jitConstants: macros);
        Assert.NotNull(kernel);

        int length = 3;
        int[] data = [1, 2, 3];

        unsafe
        {
            fixed (int* pData = data)
            {
                kernel((IntPtr)pData, length);
            }
        }

        Assert.Equal([5, 10, 15], data);
        
        // Ensure changing the macro recompiles and outputs a different result
        var macros2 = new Dictionary<string, string> { { "MULTIPLIER", "10" } };
        var kernel2 = CpuJitEngine.GetOrCompileKernel<MultiplyWithMacroDelegate>("MultiplyKernel", source, jitConstants: macros2);
        
        int[] data2 = [1, 2, 3];
        unsafe
        {
            fixed (int* pData2 = data2)
            {
                kernel2((IntPtr)pData2, length);
            }
        }
        Assert.Equal([10, 20, 30], data2);
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-10")]
    [Trait("Category", "SadPath")]
    [Trait("Category", "GpuExecution")]
    public void CpuJitEngine_InvalidSyntax_ThrowsException()
    {
        Skip.IfNot(IsCpuDialect, "CpuJitEngine tests only run under CPU dialect.");

        var source = """
            extern "C" __declspec(dllexport) void InvalidKernel(int* data) {
                int sum = 0 // Missing semicolon
                data[0] = sum;
            }
            """;

        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            CpuJitEngine.GetOrCompileKernel<MultiplyWithMacroDelegate>("InvalidKernel", source);
        });

        // The MSVC compiler should throw a compilation error containing "error C"
        Assert.Contains("MSVC Compilation Failed", ex.Message);
    }
}
