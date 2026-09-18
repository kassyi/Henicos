namespace Henicos.PiBenchmark.ComputeBenchmarks;

using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Generated;

public class ReductionBenchmark : IComputeBenchmark
{
    private const int N = 16 * 1024 * 1024; // 16M elements
    private const int ThreadsPerBlock = 256;
    private const int ElementsPerBlock = ThreadsPerBlock * 2;
    private const int Blocks = N / ElementsPerBlock;

    private float[] _input = null!;
    private float[] _outputCpu = null!;
    private float[] _outputGsharp = null!;
    private float[] _outputNative = null!;

    private float _finalResultCpu;
    private float _finalResultGsharp;
    private float _finalResultNative;

    private GpuBuffer<float>? _dInput;
    private GpuBuffer<float>? _dOutput;

    public string Name => "Parallel Reduction";

    public void Initialize()
    {
        _input = new float[N];
        _outputCpu = new float[Blocks];
        _outputGsharp = new float[Blocks];
        _outputNative = new float[Blocks];

        var rand = new Random(42);
        for (var i = 0; i < N; i++)
        {
            _input[i] = (float)rand.NextDouble();
        }
    }

    public void RunReferenceCpu()
    {
        for (var b = 0; b < Blocks; b++)
        {
            var sum = 0.0f;
            var start = b * ElementsPerBlock;
            var end = start + ElementsPerBlock;
            if (end > N)
            {
                end = N;
            }

            for (var i = start; i < end; i++)
            {
                sum += _input[i];
            }

            _outputCpu[b] = sum;
        }

        _finalResultCpu = _outputCpu.Sum();
    }

    public void RunGSharp()
    {
        ReductionKernelsWrapper.Launch_ReductionKernel(
            _input, _outputGsharp, N,
            gridDimX: Blocks, blockDimX: ThreadsPerBlock);

        _finalResultGsharp = _outputGsharp.Sum();
    }

    public void RunNativeCuda()
    {
        NativeKernelInvoker.LaunchReduction(
            _input, _outputNative, N,
            gridDimX: (uint)Blocks, blockDimX: ThreadsPerBlock);

        _finalResultNative = _outputNative.Sum();
    }

    public void InitializeDeviceMemory()
    {
        _dInput = new GpuBuffer<float>(N);
        _dOutput = new GpuBuffer<float>(Blocks);

        _dInput.CopyToDevice(_input);
    }

    public void CleanupDeviceMemory()
    {
        if (_dOutput != null)
        {
            _dOutput.CopyToHost(_outputGsharp);
            _finalResultGsharp = _outputGsharp.Sum();
        }

        _dInput?.Dispose();
        _dOutput?.Dispose();
    }

    public void RunGSharp_Pure()
    {
        if (_dInput == null || _dOutput == null)
        {
            return;
        }

        ReductionKernelsWrapper.Launch_ReductionKernel_Device(
            _dInput.DevicePointer, _dOutput.DevicePointer, N,
            gridDimX: Blocks, blockDimX: ThreadsPerBlock);

        Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(Kassyi.GSharp.Runtime.Interop.CudaDriverApi
            .cuCtxSynchronize());
    }

    public void RunNativeCuda_Pure()
    {
        if (_dInput == null || _dOutput == null)
        {
            return;
        }

        NativeKernelInvoker.LaunchReduction_Device(
            _dInput.DevicePointer, _dOutput.DevicePointer, N,
            gridDimX: Blocks, blockDimX: ThreadsPerBlock);
    }

    public void Verify()
    {
        for (var b = 0; b < Blocks; b++)
        {
            var blockDiffGsharp = Math.Abs(_outputCpu[b] - _outputGsharp[b]);
            if (blockDiffGsharp > 1e-3f)
            {
                throw new Exception(
                    $"Mismatch in block {b} G# reduction sum: CPU={_outputCpu[b]}, G#={_outputGsharp[b]}, Diff={blockDiffGsharp}");
            }

            var blockDiffNative = Math.Abs(_outputCpu[b] - _outputNative[b]);
            if (blockDiffNative > 1e-3f)
            {
                throw new Exception(
                    $"Mismatch in block {b} Native reduction sum: CPU={_outputCpu[b]}, Native={_outputNative[b]}, Diff={blockDiffNative}");
            }
        }

        var diffGsharp = Math.Abs(_finalResultCpu - _finalResultGsharp);
        const float threshold = 1.0f;

        if (diffGsharp > threshold)
        {
            throw new Exception(
                $"Mismatch in final G# reduction sum: CPU={_finalResultCpu}, G#={_finalResultGsharp}, Diff={diffGsharp}");
        }

        var diffNative = Math.Abs(_finalResultCpu - _finalResultNative);
        if (diffNative > threshold)
        {
            throw new Exception(
                $"Mismatch in final Native reduction sum: CPU={_finalResultCpu}, Native={_finalResultNative}, Diff={diffNative}");
        }
    }
}
