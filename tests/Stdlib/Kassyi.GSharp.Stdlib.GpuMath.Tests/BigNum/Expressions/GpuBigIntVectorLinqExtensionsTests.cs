
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

[TestTarget(ExecutionTarget.GpuExecution)]
public partial class GpuBigIntVectorLinqExtensionsTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public GpuBigIntVectorLinqExtensionsTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _context = new GpuWorkspace(1024 * 1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _context.Dispose();
        _scope.Dispose();
    }

    private GpuBigInt CreateScalar(BigInteger val, int numWords = 8)
    {
        bool isNeg = val < 0;
        if (isNeg) val = -val;
        var bytes = val.ToByteArray(isUnsigned: true, isBigEndian: false);
        var buffer = new uint[numWords];
        Buffer.BlockCopy(bytes, 0, buffer, 0, Math.Min(bytes.Length, buffer.Length * 4));
        var u = new GpuBigUInt(_context, buffer);
        return new GpuBigInt(u, isNeg);
    }

    private GpuBigIntArray CreateArray(BigInteger[] vals, int numWords = 8)
    {
        var array = new GpuBigIntArray(_context, vals.Length, numWords);
        var valuesHost = new uint[vals.Length * numWords];
        var signsHost = new uint[vals.Length];

        for (int i = 0; i < vals.Length; i++)
        {
            BigInteger val = vals[i];
            bool isNeg = val < 0;
            if (isNeg) val = -val;
            var bytes = val.ToByteArray(isUnsigned: true, isBigEndian: false);
            var buffer = new uint[numWords];
            Buffer.BlockCopy(bytes, 0, buffer, 0, Math.Min(bytes.Length, buffer.Length * 4));
            Array.Copy(buffer, 0, valuesHost, i * numWords, numWords);
            signsHost[i] = isNeg ? 1u : 0u;
        }

        _context.Provider.CopyHostToDevice(array.Values.DevicePointer, valuesHost, valuesHost.Length);
        _context.Provider.CopyHostToDevice(array.Signs.DevicePointer, signsHost, signsHost.Length);
        return array;
    }

    private BigInteger[] ToHostArray(GpuBigIntArray array)
    {
        int length = array.Length;
        int words = array.WordsPerElement;
        var valuesHost = new uint[length * words];
        var signsHost = new uint[length];

        _context.Provider.CopyDeviceToHost(valuesHost, array.Values.DevicePointer, valuesHost.Length);
        _context.Provider.CopyDeviceToHost(signsHost, array.Signs.DevicePointer, signsHost.Length);

        var result = new BigInteger[length];
        for (int i = 0; i < length; i++)
        {
            var buffer = new uint[words];
            Array.Copy(valuesHost, i * words, buffer, 0, words);
            var byteCount = words * 4;
            var bytes = new byte[byteCount];
            Buffer.BlockCopy(buffer, 0, bytes, 0, byteCount);
            var val = new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
            if (signsHost[i] != 0) val = -val;
            result[i] = val;
        }
        return result;
    }

    private static BigInteger ToBigInt(GpuBigIntView gpuVal)
    {
        var val = gpuVal.Value.ToBigInteger();
        return gpuVal.IsNegative ? -val : val;
    }
}
