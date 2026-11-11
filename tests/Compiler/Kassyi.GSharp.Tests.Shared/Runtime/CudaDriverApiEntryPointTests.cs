#nullable enable
using System.Runtime.InteropServices;
using Kassyi.GSharp.Compiler.NativeApi;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Checks that every binding of <c>cuda_driver_api.yml</c> calls the driver symbol whose parameters it declares.
/// </summary>
/// <remarks>
/// <c>cuda.h</c> maps most API names to a versioned symbol (<c>#define cuArrayCreate cuArrayCreate_v2</c>). The plain
/// symbol is still exported, but it is the legacy entry point: it takes 32-bit sizes, an older descriptor or fewer
/// parameters, and on a context created through the current API it can fail with <c>CUDA_ERROR_INVALID_CONTEXT</c>.
/// A binding without <c>entryPoint</c> binds its own name, so a name that <c>cuda.h</c> redirects must name the
/// versioned symbol. The scan of <c>cuda.h</c> reads the header from <c>GSHARP_CUDA_INCLUDE_DIR</c>, the CUDA toolkit
/// (<c>CUDA_PATH</c>) or <c>/usr/local/cuda</c>. CI has no toolkit and fetches the header alone into
/// <c>GSHARP_CUDA_INCLUDE_DIR</c>, so with <c>CI=true</c> a missing header fails the scan instead of skipping it.
/// </remarks>
public class CudaDriverApiEntryPointTests
{
    // CU_AD_FORMAT_FLOAT
    private const int FloatArrayFormat = 0x20;

    [SkippableFact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void DriverApiYaml_NamesRedirectedByCudaHeader_BindTheVersionedSymbol()
    {
        var header = FindCudaHeader();
        const string NotFound = "cuda.h was not found under GSHARP_CUDA_INCLUDE_DIR, CUDA_PATH or /usr/local/cuda.";
        if (header is null && IsContinuousIntegration())
        {
            Assert.Fail($"{NotFound} CI fetches the header, so the scan must run.");
        }

        Skip.If(header is null, NotFound);

        var redirects = ReadHeaderRedirects(File.ReadAllText(header));
        Assert.Contains("cuArrayCreate", redirects.Keys);

        var unversioned = ReadDriverApi().Methods
            .Select(static m => (m.Name, Symbol: m.EntryPoint ?? m.Name))
            .Where(m => redirects.ContainsKey(m.Symbol))
            .Select(m => $"{m.Name} binds {m.Symbol}; cuda.h calls {redirects[m.Symbol]}")
            .ToList();

        Assert.Empty(unversioned);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    [InlineData("cuArrayCreate", "cuArrayCreate_v2")]
    [InlineData("cuDeviceTotalMem", "cuDeviceTotalMem_v2")]
    [InlineData("cuEventElapsedTime", "cuEventElapsedTime_v2")]
    [InlineData("cuMemAllocHost", "cuMemAllocHost_v2")]
    [InlineData("cuMemHostRegister", "cuMemHostRegister_v2")]
    [InlineData("cuStreamBeginCapture", "cuStreamBeginCapture_v2")]
    [InlineData("cuStreamDestroy", "cuStreamDestroy_v2")]
    public void DriverApiYaml_VersionedBinding_NamesTheHeaderSymbol(string methodName, string entryPoint)
    {
        var declaration = Assert.Single(ReadDriverApi().Methods, m => m.Name == methodName);

        Assert.Equal(entryPoint, declaration.EntryPoint);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void ArrayCreate_TwoDimensionalFloatArray_ReturnsHandle()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();

        var descriptor = new CudaArrayDescriptor
        {
            Width = 256,
            Height = 256,
            Format = FloatArrayFormat,
            NumChannels = 1,
        };
        var result = CudaDriverApi.cuArrayCreate(out var array, ref descriptor);
        if (array != IntPtr.Zero)
        {
            CudaException.ThrowIfError(CudaDriverApi.cuArrayDestroy(array));
        }

        Assert.Equal(CUresult.CudaSuccess, result);
        Assert.NotEqual(IntPtr.Zero, array);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceTotalMem_CurrentDevice_MatchesMemGetInfoTotal()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();

        CudaException.ThrowIfError(CudaDriverApi.cuCtxGetDevice(out var device));
        CudaException.ThrowIfError(CudaDriverApi.cuMemGetInfo_v2(out _, out var total));
        var result = CudaDriverApi.cuDeviceTotalMem(out var bytes, device);

        Assert.Equal(CUresult.CudaSuccess, result);
        Assert.Equal((ulong)total, (ulong)bytes);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void MemAllocHost_OnePage_ReturnsPinnedMemory()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();

        var result = CudaDriverApi.cuMemAllocHost(out var pointer, 4096);
        if (pointer != IntPtr.Zero)
        {
            CudaException.ThrowIfError(CudaDriverApi.cuMemFreeHost(pointer));
        }

        Assert.Equal(CUresult.CudaSuccess, result);
        Assert.NotEqual(IntPtr.Zero, pointer);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public unsafe void MemHostRegister_OnePage_Registers()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();

        var memory = NativeMemory.AlignedAlloc(4096, 4096);
        try
        {
            var result = CudaDriverApi.cuMemHostRegister((IntPtr)memory, 4096, 0);
            if (result == CUresult.CudaSuccess)
            {
                CudaException.ThrowIfError(CudaDriverApi.cuMemHostUnregister((IntPtr)memory));
            }

            Assert.Equal(CUresult.CudaSuccess, result);
        }
        finally
        {
            NativeMemory.AlignedFree(memory);
        }
    }

    private static NativeApiDefinition ReadDriverApi()
    {
        var path = RepositoryPaths.Combine("src", "NativeApi", "cuda_driver_api.yml");
        var parsed = NativeApiYamlReader.TryRead(File.ReadAllText(path), out var definition, out var error);

        Assert.True(parsed, error is null ? null : $"line {error.Line}: {error.Message}");
        Assert.NotNull(definition);
        return definition;
    }

    private static string? FindCudaHeader()
    {
        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        var includeDirectories = new[]
        {
            Environment.GetEnvironmentVariable("GSHARP_CUDA_INCLUDE_DIR"),
            string.IsNullOrEmpty(cudaPath) ? null : Path.Combine(cudaPath, "include"),
            "/usr/local/cuda/include",
        };
        return includeDirectories
            .Where(static directory => !string.IsNullOrEmpty(directory))
            .Select(static directory => Path.Combine(directory ?? "", "cuda.h"))
            .FirstOrDefault(File.Exists);
    }

    private static bool IsContinuousIntegration() =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    // The redirects of cuda.h without CUDA_API_PER_THREAD_DEFAULT_STREAM, where __CUDA_API_PTDS(api) and
    // __CUDA_API_PTSZ(api) expand to api: "#define <name> <target>" with two identifiers starting with "cu".
    // Identity mappings (cuMemcpyAsync to itself) are not redirects.
    private static Dictionary<string, string> ReadHeaderRedirects(string header)
    {
        var redirects = new Dictionary<string, string>();
        foreach (var rawLine in header.AsSpan().EnumerateLines())
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("#define ", StringComparison.Ordinal))
            {
                continue;
            }

            var rest = line["#define ".Length..].TrimStart();
            var spaceIndex = rest.IndexOf(' ');
            if (spaceIndex < 0)
            {
                continue;
            }

            var nameSpan = rest[..spaceIndex];
            var targetSpan = rest[spaceIndex..].Trim();

            if (targetSpan.IndexOf(' ') >= 0)
            {
                continue;
            }

            if (!nameSpan.StartsWith("cu", StringComparison.Ordinal))
            {
                continue;
            }

            var name = nameSpan.ToString();
            var target = StripPerThreadMacro(targetSpan.ToString());
            if (!target.StartsWith("cu", StringComparison.Ordinal) || name == target)
            {
                continue;
            }

            redirects.TryAdd(name, target);
        }

        return redirects;
    }

    private static readonly string[] s_perThreadMacros = ["__CUDA_API_PTDS(", "__CUDA_API_PTSZ("];

    private static string StripPerThreadMacro(string target)
    {
        var matchedMacro = s_perThreadMacros.AsStackEnumerable()
            .FirstOrDefault(macro => target.StartsWith(macro, StringComparison.Ordinal) && target.EndsWith(')'));
        return matchedMacro is not null ? target[matchedMacro.Length..^1] : target;
    }
}
