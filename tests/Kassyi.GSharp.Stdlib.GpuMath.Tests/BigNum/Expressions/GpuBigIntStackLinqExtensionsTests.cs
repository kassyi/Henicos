using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.ZeroAlloc.Linq;
using Xunit;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigIntStackLinqExtensionsTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public GpuBigIntStackLinqExtensionsTests()
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

    private GpuBigInt Create(BigInteger val, int numWords = 8)
    {
        bool isNeg = val < 0;
        if (isNeg) val = -val;

        var bytes = val.ToByteArray(isUnsigned: true, isBigEndian: false);
        var buffer = new uint[numWords];
        Buffer.BlockCopy(bytes, 0, buffer, 0, Math.Min(bytes.Length, buffer.Length * 4));

        var u = new GpuBigUInt(_context, buffer);
        return new GpuBigInt(u, isNeg);
    }

    private static BigInteger ToBigInt(GpuBigInt gpuVal)
    {
        var val = gpuVal.Value.ToBigInteger();
        return gpuVal.IsNegative ? -val : val;
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ParallelStackEnumerable_Sum_CalculatesCorrectSum()
    {
        // Arrange
        const int numElements = 16;
        var array = new GpuBigInt[numElements];
        BigInteger expectedSum = 0;

        for (int i = 0; i < numElements; i++)
        {
            BigInteger val = (i + 1) * 10;
            array[i] = Create(val);
            expectedSum += val;
        }

        // Act
        var sumExpr = array.AsStackEnumerable().AsParallel().Sum();
        var result = sumExpr.Evaluate(_context);

        // Assert
        Assert.Equal(expectedSum, ToBigInt(result));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ParallelStackEnumerable_SelectAndSum_CalculatesCorrectSum()
    {
        // Arrange
        const int numElements = 16;
        var array = new GpuBigInt[numElements];
        var constant = Create(5);
        BigInteger expectedSum = 0;

        for (int i = 0; i < numElements; i++)
        {
            BigInteger val = (i + 1) * 10;
            array[i] = Create(val);
            expectedSum += (val + 5);
        }

        // Act
        var sumExpr = array.AsStackEnumerable()
            .AsParallel()
            .Select(x => x + constant)
            .Sum();

        var result = sumExpr.Evaluate(_context);

        // Assert
        Assert.Equal(expectedSum, ToBigInt(result));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ParallelStackEnumerable_Max_CalculatesCorrectMax()
    {
        // Arrange
        const int numElements = 16;
        var array = new GpuBigInt[numElements];
        BigInteger expectedMax = long.MinValue;

        for (int i = 0; i < numElements; i++)
        {
            // mix of positive and negative numbers
            BigInteger val = (i % 2 == 0) ? (i + 1) * 10 : -(i + 1) * 5;
            array[i] = Create(val);
            if (val > expectedMax)
            {
                expectedMax = val;
            }
        }

        // Act
        var maxExpr = array.AsStackEnumerable().AsParallel().Max();
        var result = maxExpr.Evaluate(_context);

        // Assert
        Assert.Equal(expectedMax, ToBigInt(result));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ParallelStackEnumerable_Min_CalculatesCorrectMin()
    {
        // Arrange
        const int numElements = 16;
        var array = new GpuBigInt[numElements];
        BigInteger expectedMin = long.MaxValue;

        for (int i = 0; i < numElements; i++)
        {
            // mix of positive and negative numbers
            BigInteger val = (i % 2 == 0) ? (i + 1) * 10 : -(i + 1) * 5;
            array[i] = Create(val);
            if (val < expectedMin)
            {
                expectedMin = val;
            }
        }

        // Act
        var minExpr = array.AsStackEnumerable().AsParallel().Min();
        var result = minExpr.Evaluate(_context);

        // Assert
        Assert.Equal(expectedMin, ToBigInt(result));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ParallelStackEnumerable_Aggregate_CalculatesCorrectResult()
    {
        // Arrange
        const int numElements = 16;
        var array = new GpuBigInt[numElements];
        BigInteger expectedSum = 0;

        for (int i = 0; i < numElements; i++)
        {
            BigInteger val = (i + 1) * 10;
            array[i] = Create(val);
            expectedSum += val;
        }

        // Act
        // Use Aggregate to implement a sum
        var aggregateExpr = array.AsStackEnumerable()
            .AsParallel()
            .Aggregate((a, b) => a + b);
        var result = aggregateExpr.Evaluate(_context);

        // Assert
        Assert.Equal(expectedSum, ToBigInt(result));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ParallelStackEnumerable_Count_CalculatesCorrectCount()
    {
        // Arrange
        const int numElements = 16;
        var array = new GpuBigInt[numElements];

        for (int i = 0; i < numElements; i++)
        {
            array[i] = Create((i + 1) * 10);
        }

        // Act
        var count = array.AsStackEnumerable().AsParallel().Count();

        // Assert
        Assert.Equal(numElements, count);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ParallelStackEnumerable_Average_CalculatesCorrectResult()
    {
        // Arrange
        const int numElements = 16;
        var array = new GpuBigInt[numElements];
        BigInteger expectedSum = 0;

        for (int i = 0; i < numElements; i++)
        {
            BigInteger val = (i + 1) * 10;
            array[i] = Create(val);
            expectedSum += val;
        }

        var expectedAverage = expectedSum / numElements;

        // Act
        var avgExpr = array.AsStackEnumerable().AsParallel().Average();
        var result = avgExpr.Evaluate(_context);

        // Assert
        Assert.Equal(expectedAverage, ToBigInt(result));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ParallelStackEnumerable_Where_FiltersElementsCorrectly()
    {
        // Arrange
        const int numElements = 16;
        var array = new GpuBigInt[numElements];
        BigInteger expectedSum = 0;

        for (int i = 0; i < numElements; i++)
        {
            BigInteger val = (i + 1) * 10;
            array[i] = Create(val);

            // Only sum values > 100 on host for verification
            if (val > 100)
            {
                expectedSum += val;
            }
        }

        // Act
        // Use Where before Sum to filter elements. 
        // Note: predicate is evaluated on host. We read back to CPU just for the test if needed, 
        // but here we can just use ToBigInt.
        var sumExpr = array.AsStackEnumerable()
            .Where(x => ToBigInt(x) > 100)
            .AsParallel()
            .Sum();
        var result = sumExpr.Evaluate(_context);

        // Assert
        Assert.Equal(expectedSum, ToBigInt(result));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ParallelStackEnumerable_Sum_CompileToGraph_CalculatesCorrectSum()
    {
        // Arrange
        const int numElements = 16;
        var array = new GpuBigInt[numElements];
        BigInteger expectedSum = 0;

        for (int i = 0; i < numElements; i++)
        {
            BigInteger val = (i + 1) * 10;
            array[i] = Create(val);
            expectedSum += val;
        }

        // Act
        var result = array.AsStackEnumerable()
            .AsParallel()
            .Sum()
            .EvaluateWithCudaGraph(_context);

        Assert.Equal(expectedSum, ToBigInt(result));
    }
}
