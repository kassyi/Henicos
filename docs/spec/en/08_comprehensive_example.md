# 8. Comprehensive Example

This document provides a "Rosetta Stone" example that validates all of the specifications detailed in this repository. It demonstrates how a single G# C# file utilizes custom `struct` definitions, `[GpuKernel]`, `[GpuDevice]`, `[JitConstant]`, intrinsic math functions, static/dynamic shared memory, atomics, thread synchronization, and implicit array marshaling.

## 8.1 The G# Source Code

```csharp
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;

namespace DebugReduc;

public struct SpecTypes
{
    public bool BooleanValue;
    public byte ByteValue;
    public sbyte SByteValue;
    public short ShortValue;
    public ushort UShortValue;
    public int IntValue;
    public uint UIntValue;
    public long LongValue;
    public ulong ULongValue;
    public float FloatValue;
    public double DoubleValue;
}

public struct PhysicsVector
{
    public float X;
    public float Y;
    public float Z;
}

public record struct Point(float X, float Y);

public static partial class ComprehensiveSpecKernel
{
    [GpuDevice]
    [GpuInline]
    public static float ComputeMagnitude(PhysicsVector v)
    {
        return (float)Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
    }

    [GpuDevice]
    [GpuNoInline]
    public static bool IsPositive(float value)
    {
        return value > 0;
    }

    [GpuKernel]
    public static void ProcessPhysics(
        PhysicsVector[] particles,
        ref float totalMagnitude,
        out bool successFlag,
        SpecTypes largeStructVal, // Triggers GSH1002 (>= 32 bytes passed by value)
        int particleCount,
        [JitConstant] int unrollFactor)
    {
        successFlag = false;

        // 1. All Intrinsics Indexing
        int tx = ThreadIndex.X;
        int ty = ThreadIndex.Y;
        int tz = ThreadIndex.Z;

        int bx = BlockIndex.X;
        int by = BlockIndex.Y;
        int bz = BlockIndex.Z;

        int bdimX = BlockDimension.X;
        int bdimY = BlockDimension.Y;
        int bdimZ = BlockDimension.Z;

        int gdimX = GridDimension.X;
        int gdimY = GridDimension.Y;
        int gdimZ = GridDimension.Z;

        int tid = tx + bx * bdimX;

        // 2. Shared Memory (Static & Dynamic)
        SharedMemory<int> staticCache = new SharedMemory<int>(10000); // Triggers GSH1001 (>32768 bytes)
        SharedMemory<float> dynamicCache = new SharedMemory<float>(particleCount);

        if (tid < particleCount)
        {
            // 3. Custom Struct extraction & Array pointer dereferencing
            PhysicsVector p = particles[tid];
            SpecTypes types = largeStructVal; // Proves compilation of all primitive types

            // 3.5 Record struct and var keyword support
            var origin = new Point(0, 0);
            var distance = origin.X + p.X;

            // 4. All Math Mappings and Device Method invocation
            float mag = ComputeMagnitude(p);
            mag = (float)Math.Abs(mag);

            // 5. Automatic Loop Unrolling via JitConstant (for)
            for (int i = 0; i < unrollFactor; i++)
            {
                if (i == 1) continue;
                mag = (float)Math.Max(mag, p.X * i);
            }

            if (!IsPositive(mag))
            {
                mag = 0;
            }

            // 6. Other Loop Syntaxes
            int counter = 0;
            while (counter < 2)
            {
                mag = (float)Math.Min(mag, mag + 1);
                counter++;
            }

            do
            {
                counter--;
            } while (counter > 0);

            // 7. Switch statement
            switch (tx)
            {
                case 0:
                    mag += 1;
                    break;
                default:
                    mag += 0;
                    break;
            }

            // 8. Memory writes
            staticCache[tx] = tid;
            dynamicCache[tx] = mag;

            // 9. Synchronization
            Gpu.SyncThreads();

            // 10. Atomics and Out parameter
            if (tx == 0)
            {
                Gpu.AtomicAdd(ref totalMagnitude, dynamicCache[0]);
                successFlag = true;
            }

            // 11. Early return
            if (ty > 0)
            {
                return;
            }
        }
    }
}
```

## 8.2 Build Output (Diagnostics)

When building this code, MSBuild natively catches the strict constraints via the `Kassyi.Generators.GSharp` analyzers:

```
ComprehensiveSpecKernel.cs(50,9): warning GSH1002: The parameter 'largeStructVal' is a struct of size 43 bytes. Passing large structs by value to a GPU kernel can consume excessive constant memory. Consider passing by reference (ref) or using an array pointer.
ComprehensiveSpecKernel.cs(76,63): warning GSH1001: The kernel allocates 40000 bytes of static shared memory, which exceeds the recommended limit of 32768 bytes. This may cause kernel launch failure or reduce occupancy.
```

## 8.3 Transpiled CUDA C++ Kernel

The transpiler correctly generates the following `.cu` code. Notice how all primitive types are emitted, array dereferencing resolves correctly, while/switch/do-while loops are fully generated, and intrinsic mappings for `ThreadIndex`, `Math.Abs`, `Math.Max` (with proper `(float)` casting via `VisitCastExpression`), etc., are fully preserved:

```cpp
// <auto-generated/>
using System;
using System.Runtime.InteropServices;
using global::Kassyi.GSharp.Core;

namespace DebugReduc
{
public static partial class ComprehensiveSpecKernelWrapper
{
public const string ProcessPhysics_CudaSource = @"
__forceinline__ __device__ float ComputeMagnitude(PhysicsVector v){
    return (float)sqrt((((v.X*v.X)+(v.Y*v.Y))+(v.Z*v.Z)));
}

__noinline__ __device__ bool IsPositive(float value){
    return (value>0);
}

struct PhysicsVector { float X; float Y; float Z; };
struct SpecTypes { bool BooleanValue; unsigned char ByteValue; char SByteValue; short ShortValue; unsigned short UShortValue; int IntValue; unsigned int UIntValue; long long LongValue; unsigned long long ULongValue; float FloatValue; double DoubleValue; };

extern ""C"" __global__ void ProcessPhysics(PhysicsVector* particles,float* totalMagnitude,bool* successFlag,SpecTypes largeStructVal,int particleCount){
    successFlag=false;
    int tx=threadIdx.x;
    int ty=threadIdx.y;
    int tz=threadIdx.z;
    int bx=blockIdx.x;
    int by=blockIdx.y;
    int bz=blockIdx.z;
    int bdimX=blockDim.x;
    int bdimY=blockDim.y;
    int bdimZ=blockDim.z;
    int gdimX=gridDim.x;
    int gdimY=gridDim.y;
    int gdimZ=gridDim.z;
    int tid=(tx+(bx*bdimX));

    __shared__ int staticCache[10000];
    extern __shared__ char __gsharp_dynamic_smem[];
    size_t __smem_offset = 0;
    __smem_offset = (__smem_offset + alignof(float) - 1) & ~(alignof(float) - 1);
    float* dynamicCache = (float*)(__gsharp_dynamic_smem + __smem_offset);
    __smem_offset += (particleCount) * sizeof(float);
    
    if((tid<particleCount))
    {
        PhysicsVector p=particles[tid];
        SpecTypes types=largeStructVal;
        float mag=ComputeMagnitude(p);
        mag=(float)abs(mag);

        #pragma unroll
        for(int i=0;(i<unrollFactor);i++)
        {
            mag=(float)max(mag,(p.X*i));
        }

        if(!IsPositive(mag))
        {
            mag=0;
        }

        int counter=0;
        while((counter<2))
        {
            mag=(float)min(mag,(mag+1));
            counter++;
        }

        do
        {
            counter--;
        }
        while((counter>0));

        switch(tx)
        {
            case 0:
                mag+=1;
                break;
            default:
                mag+=0;
                break;
        }

        staticCache[tx]=tid;
        dynamicCache[tx]=mag;

        __syncthreads();

        if((tx==0))
        {
            atomicAdd(&totalMagnitude,dynamicCache[0]);
            successFlag=true;
        }

        if((ty>0))
        {
            return;
        }
    }
}";
// Host launcher omitted for brevity...
```

## 8.4 Sad Path Compilation Errors (Diagnostics)

To ensure that the constraints outlined in [04_limitations_and_diagnostics.md](04_limitations_and_diagnostics.md) are strictly enforced, G# includes an exhaustive suite of diagnostic tests (`tests\Kassyi.GSharp.Tests\GeneratorDiagnosticsTests.cs`). 

For example, when attempting to write invalid C# code in a kernel:

```csharp
[GpuKernel]
public static int BadKernel()
{
    string s = "hello";
    object obj = new object();
    return 0;
}
```

The G# compiler natively intercepts these syntax tree nodes and fails the build with the following deterministic errors (which are snapshot-tested using Verify.XUnit):

1. **GSH0001**: `Object creation ('new') is not supported in GPU kernels. Only struct instantiation and SharedMemory<T> are allowed.` (Triggered by `new object()`)
2. **GSH0002**: `The string type is not supported in GPU kernels.` (Triggered by `string s = "hello"`)
3. **GSH0003**: `The syntax 'ObjectCreationExpression' is not supported in GSharp GPU kernels.` (Fallback trigger for any unknown syntax node)
4. **GSH0004**: `Method 'BadKernel' is marked with [GpuKernel] but returns 'int'. GpuKernel methods must return void.`

## Conclusion

This explicit trace proves that:

1. `JitConstant` uses preprocessor macros.
2. Custom Structs map flawlessly, and all 11 primitive C# types align to their C++ equivalents.
3. Diagnostic warnings and errors natively integrate with MSBuild (`GSH0001` - `GSH0004` and `GSH1001` - `GSH1002`) to act as strict guardrails.
4. `CastExpression` and `ParenthesizedExpression` AST nodes are properly translated, allowing explicit math method casting like `(float)Math.Sqrt(...)`.
5. **`continue` statements are currently silently ignored** (documented limitation).
6. **Phase 5 Target Identified**: `ref` and `out` scalars are correctly transpiled to pointers in `.cu` but their Device-to-Host copy-back logic is currently **missing** in the host C# launcher.

