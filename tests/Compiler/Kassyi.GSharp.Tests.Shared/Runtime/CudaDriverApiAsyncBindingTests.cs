using Kassyi.GSharp.Compiler.NativeApi;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Validates that the Driver API bindings required for asynchronous completion and stream-ordered
/// memory operations are declared in <c>cuda_driver_api.yml</c> and generated into <see cref="CudaDriverApi"/>.
/// </summary>
public class CudaDriverApiAsyncBindingTests
{
    private delegate CUresult StreamContextQuery(IntPtr stream, out IntPtr context);

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CudaDriverApi_AsyncCompletionBindings_HaveExpectedSignatures()
    {
        // Compile-time contract: method group conversions fail to build if a binding or its signature is missing.
        Func<IntPtr, CUresult> eventQuery = CudaDriverApi.cuEventQuery;
        Func<IntPtr, CUresult> streamQuery = CudaDriverApi.cuStreamQuery;
        Func<IntPtr, IntPtr, IntPtr, CUresult> launchHostFunc = CudaDriverApi.cuLaunchHostFunc;
        Func<IntPtr, uint, IntPtr, IntPtr, CUresult> memsetD32Async = CudaDriverApi.cuMemsetD32Async;
        StreamContextQuery streamGetContext = CudaDriverApi.cuStreamGetCtx;

        Assert.NotNull(eventQuery);
        Assert.NotNull(streamQuery);
        Assert.NotNull(launchHostFunc);
        Assert.NotNull(memsetD32Async);
        Assert.NotNull(streamGetContext);
    }

    [Theory]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    [InlineData("cuEventQuery", 1)]
    [InlineData("cuStreamQuery", 1)]
    [InlineData("cuLaunchHostFunc", 3)]
    [InlineData("cuMemsetD32Async", 4)]
    [InlineData("cuStreamGetCtx", 2)]
    public void Test_DriverApiYaml_DeclaresAsyncBinding_ExactlyOnce(string methodName, int parameterCount)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../../src/NativeApi", "cuda_driver_api.yml"));
        var parsed = NativeApiYamlReader.TryRead(File.ReadAllText(path), out var definition, out var error);

        Assert.True(parsed, error is null ? null : $"line {error.Line}: {error.Message}");
        Assert.NotNull(definition);

        var declaration = Assert.Single(definition.Methods, m => m.Name == methodName);
        Assert.Equal(parameterCount, declaration.Parameters.Length);
    }
}
