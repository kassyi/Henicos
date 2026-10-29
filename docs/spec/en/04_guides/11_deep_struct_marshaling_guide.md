---
title: "4.11 Deep Struct Marshaling, AOT Contexts, and Argument Hell Elimination Guide"
site: "G# Documentation"
published: "2026-09-30T00:00:00.000Z"
language: "en"
description: "A comprehensive guide to eliminating Argument Hell using deep struct marshaling, AOT Contexts, named arguments, default parameters, and zero-allocation struct passing."
---

# 4.11 Deep Struct Marshaling, AOT Contexts, and Argument Hell Elimination Guide

When writing high-performance GPU kernels—especially in compute-intensive domains like cryptography, BigNum arithmetic, or physical simulations—developers inevitably confront **"Argument Hell"**:
Kernel methods and device helper signatures balloon to 15, 20, or even 30 parameters containing a chaotic mix of input arrays, scratch buffers, outputs, scalar lengths, strides, and twiddle tables.

In traditional CUDA C++ and C# P/Invoke interop:
- Passing structures containing managed reference types (such as `int[]` or `float[]` arrays) directly by value is strictly prohibited.
- Developers are forced to flatten every buffer into individual method arguments, manually pin each array on the host, manage device allocations, and track error cleanup.
- Refactoring or adding a new scratch buffer requires modifying dozens of call sites across host and device code.

G# fundamentally eradicates Argument Hell through two complementary architectural pillars:
1. **Deep Struct Marshaling & AOT Contexts**: Package complex buffers and scalars into cohesive C# structs. The G# Source Generator transparently maps host arrays to device pointers, orchestrates automatic Host-to-Device (HtoD) and Device-to-Host (DtoH) transfers, and safely reclaims GPU resources.
2. **Call-Site Syntactic Sugar**: Device-side support for **Named Arguments** (`param: value`), **Default / Optional Parameters** (`int stride = 1`), and the **"Base-Struct-and-Copy"** pattern.

---

## 1. Before and After: Eradicating Argument Hell

### The "Argument Hell" Antipattern (Before)

Without struct marshaling and parameter defaults, call chains become unreadable and brittle:

```csharp
// Host launch: 18 loose arguments with high risk of positional swapping bugs
GpuBigIntMegaKernels.LaunchFermatMegaKernel(
    d_x, d_y, d_result,
    xLength, yLength, resultLength,
    d_twiddles1, d_twiddles2, d_scratchA, d_scratchB, d_tmp,
    modulus, nttLength, radix, 1, 0, 1024, hStream
);

// Device kernel signature: Overwhelming and difficult to maintain
[GpuKernel]
public static void FermatMegaKernel(
    uint* x, uint* y, uint* result,
    int xLen, int yLen, int resLen,
    uint* twiddles1, uint* twiddles2, uint* scratchA, uint* scratchB, uint* tmp,
    ulong modulus, int nttLen, int radix, int stride, int offset, int blockSize
)
{
    // Calling a nested helper requires passing all 15+ loose parameters again!
    ExecuteDualNtt(scratchA, scratchB, tmp, twiddles1, twiddles2, nttLen, radix, 1, 0, false, 4);
}
```

### The Clean AOT Context & Modern Call Pattern (After)

With G# AOT Contexts combined with **default parameters** and **named arguments**, parameters are grouped logically into clean value types and calls become concise and self-documenting:

```csharp
// 1. Declare domain contexts
public static class AotContexts
{
    public struct DualNtt
    {
        public uint[] Buffer1;
        public uint[] Buffer2;
        public uint[] Twiddles;
        public int Length;
    }

    public struct FermatContext
    {
        public uint[] OperandA;
        public uint[] OperandB;
        public uint[] Result;
        public ulong Modulus;
        public DualNtt Ntt; // Nested contexts are fully supported!
    }
}

// 2. Define Device Helper with Default Parameters
[GpuDevice]
public static void ExecuteDualNtt(
    AotContexts.DualNtt ntt, 
    int stage = 0, 
    bool isInverse = false, 
    int unrollFactor = 4)
{
    // Implementation...
}

// 3. Consume cleanly on GPU (Using Named Arguments & Omitting Defaults)
[GpuKernel]
public static void FermatMegaKernel(AotContexts.FermatContext ctx)
{
    int tid = ThreadIndex.X;
    ctx.Result[tid] = ctx.OperandA[tid] + ctx.OperandB[tid];
    
    // Call 1: All optional parameters take their defaults (stage=0, isInverse=false, unrollFactor=4)
    ExecuteDualNtt(ctx.Ntt);

    // Call 2: Selectively override ONLY 'isInverse' using a named argument!
    ExecuteDualNtt(ctx.Ntt, isInverse: true);
}

// 4. Launch cleanly from Host (Arrays automatically allocated, pinned, and copied)
LaunchFermatMegaKernel(new AotContexts.FermatContext
{
    OperandA = hostA,
    OperandB = hostB,
    Result = hostResult,
    Modulus = primeModulus,
    Ntt = new AotContexts.DualNtt
    {
        Buffer1 = hostBuf1,
        Buffer2 = hostBuf2,
        Twiddles = hostTwiddles,
        Length = 1024
    }
});
```

---

## 2. Deep Struct Marshaling Mechanics

### How It Works Under the Hood

When a kernel accepts a custom struct containing arrays, the G# Source Generator synthesizes three key artifacts at compile time:

1. **Native CUDA Shadow Struct (`Aot_...`)**:
   Emits a memory-aligned C++ `struct` where managed array fields (`T[]`) are translated into raw device pointers (`T*`), and primitive scalars retain their blittable layout.
2. **Marshaling Routine (`Marshal_...`)**:
   Loops through the struct fields on the host. For every non-null array:
   - Queries array length and calculates byte size.
   - Looks the array up by reference in the launch's `DeviceArrayRegions` (the `deviceArrays` parameter). An array that
     the launch already copied, through another field of the same struct, a field of another struct argument or an
     element of a converted array, reuses that region and is neither allocated nor copied again: as in C#, the fields
     hold one array, so a write through one of them is seen through the others.
   - Otherwise allocates GPU global memory on the launch stream via `CudaDriverApi.cuMemAllocAsync` and records the
     region in `DeviceArrayRegions`.
   - Pins host memory using `GCHandle.Alloc(..., GCHandleType.Pinned)` and copies it to the device via
     `cuMemcpyHtoDAsync_v2`.
3. **Unmarshaling Routine (`Unmarshal_...`) and Cleanup**:
   Upon kernel completion:
   - `Unmarshal_` copies the device memory of every array field back to its host array via `cuMemcpyDtoHAsync_v2`. An
     array held by several fields is copied back once per field; every copy writes the same bytes.
   - The launcher then frees every region of the launch once, in its `finally` block, through
     `DeviceArrayRegions.Free`, which calls `cuMemFreeAsync` on the launch stream.

### Complete Memory Lifecycle

```mermaid
sequenceDiagram
    autonumber
    participant Host as C# Host Caller
    participant Launcher as G# Host Launcher (partial class)
    participant Driver as CUDA Driver API
    participant GPU as Device Kernel

    Host->>Launcher: LaunchKernel(new MyContext { Input = hostIn, Output = hostOut })
    activate Launcher
    
    Note over Launcher,Driver: Phase 1: Pre-Launch Marshaling (HtoD)
    Launcher->>Driver: cuMemAllocAsync(out native.Input, sizeIn, hStream)
    Launcher->>Driver: cuMemcpyHtoDAsync_v2(native.Input, pinnedHostIn, sizeIn, hStream)
    Launcher->>Driver: cuMemAllocAsync(out native.Output, sizeOut, hStream)
    Launcher->>Driver: cuMemcpyHtoDAsync_v2(native.Output, pinnedHostOut, sizeOut, hStream)
    Note over Launcher: Each region is recorded in DeviceArrayRegions; a field holding an array already recorded reuses its region
    
    Note over Launcher,GPU: Phase 2: Kernel Execution
    Launcher->>GPU: cuLaunchKernel(native_ctx by value)
    activate GPU
    GPU-->>GPU: Access ctx.Input[i] & write ctx.Output[i]
    GPU-->>Launcher: Kernel Execution Complete
    deactivate GPU
    
    Note over Launcher,Driver: Phase 3: Post-Launch Unmarshaling (DtoH)
    Launcher->>Driver: cuMemcpyDtoHAsync_v2(pinnedHostIn, native.Input, sizeIn, hStream)
    Launcher->>Driver: cuMemcpyDtoHAsync_v2(pinnedHostOut, native.Output, sizeOut, hStream)
    Note over Launcher,Host: hostOut now contains kernel results!
    
    Note over Launcher,Driver: Phase 4: Automatic Resource Cleanup (launcher finally, DeviceArrayRegions.Free)
    Launcher->>Driver: cuMemFreeAsync(native.Input, hStream)
    Launcher->>Driver: cuMemFreeAsync(native.Output, hStream)
    
    Launcher-->>Host: Return to Caller
    deactivate Launcher
```

---

## 3. Call-Site Syntactic Sugar: Default & Named Arguments

In addition to grouping arguments into structs, G# provides first-class support for C# call-site syntactic sugar directly inside device-side kernel and helper calls.

### 3.1 Default and Optional Parameters

In standard CUDA C++, function declarations with default parameters can suffer from declaration-order dependencies and header synchronization issues. G# resolves this at **compile-time AST transpilation**.

When a `[GpuDevice]` method declares parameters with default values, the call site can omit any trailing or optional arguments. The G# Transpiler (`InvocationExpressionTranspilerRule`) inspects the callee's semantic symbol, retrieves the default constant literal or syntax node, and **automatically fills in the omitted arguments into the generated C++ invocation**.

#### Example: Defining and Invoking Helper with Multiple Defaults

```csharp
// Device helper declaration with multiple default parameters
[GpuDevice]
public static void ProcessBlock(
    float* input,
    float* output,
    int length,
    float scale = 1.0f,
    float bias = 0.0f,
    bool applyRelu = true,
    int stride = 1)
{
    int tid = ThreadIndex.X;
    if (tid >= length) return;

    float val = (input[tid * stride] * scale) + bias;
    if (applyRelu && val < 0.0f) val = 0.0f;
    output[tid] = val;
}

[GpuKernel]
public static void KernelWithDefaults(AotContexts.VectorContext ctx)
{
    // Case 1: Supply ONLY the required parameters. All 4 optional arguments take defaults:
    ProcessBlock(ctx.Input, ctx.Output, ctx.Length);
    // Transpiles directly to CUDA C++ as:
    // ProcessBlock(ctx.Input, ctx.Output, ctx.Length, 1.0f, 0.0f, true, 1);

    // Case 2: Override 'scale' positionally, leaving 'bias', 'applyRelu', and 'stride' as defaults:
    ProcessBlock(ctx.Input, ctx.Output, ctx.Length, 2.5f);
    // Transpiles to CUDA C++ as:
    // ProcessBlock(ctx.Input, ctx.Output, ctx.Length, 2.5f, 0.0f, true, 1);
}
```

### 3.2 Named Arguments (`NameColon`)

In complex math routines, boolean flags like `isInverse: true` or configuration numbers like `stride: 2` are hazardous when passed positionally because adjacent parameters often share the same type (`int` or `bool`).

G# supports C# named arguments (`parameterName: value`). The transpiler maps each argument by name using `arg.NameColon`, matches it against the callee's parameter symbols, and reorders the arguments to match the C++ function signature:

#### Example: Combining Named Arguments with Default Arguments

```csharp
[GpuKernel]
public static void KernelWithNamedArgs(AotContexts.VectorContext ctx)
{
    // Case 1: Selectively override 'bias' and 'applyRelu' WITHOUT specifying 'scale':
    ProcessBlock(
        ctx.Input, 
        ctx.Output, 
        ctx.Length, 
        bias: 0.5f, 
        applyRelu: false
    );
    // Transpiles to CUDA C++ with 'scale' (1.0f) and 'stride' (1) automatically populated:
    // ProcessBlock(ctx.Input, ctx.Output, ctx.Length, 1.0f, 0.5f, false, 1);

    // Case 2: Complete argument reordering for self-documenting code:
    ProcessBlock(
        output: ctx.Output,
        input: ctx.Input,
        length: ctx.Length,
        stride: 2,
        scale: 4.0f
    );
    // Transpiles to CUDA C++ in the exact positional order defined by ProcessBlock:
    // ProcessBlock(ctx.Input, ctx.Output, ctx.Length, 4.0f, 0.0f, true, 2);
}
```

### 3.3 The "Base-Struct-and-Copy" Pattern

In multi-pass algorithms (e.g., Forward NTT followed by Inverse NTT, or iterative solvers), successive kernel steps often share 90% of the same parameters while varying only one or two fields.

Because contexts are standard C# value-type structs, you can clone and mutate them on the host or inside cooperative kernels with zero heap allocation:

```csharp
// Initialize base context once
var baseNtt = new AotContexts.DualNtt
{
    TemporaryBuffer = ctx.Workspace.TmpBuf,
    M = m, 
    N1 = n1, 
    N2 = n2
};

// Pass 1: Forward NTT on Operand A
var forwardA = baseNtt;
forwardA.Buffer1 = ctx.Workspace.NttBuf1A;
forwardA.Buffer2 = ctx.Workspace.NttBuf2A;
forwardA.Twiddles = ctx.Ntt.ForwardTwiddles;
GpuBigIntNttKernels.ExecuteDualForwardNtt(forwardA);

// Pass 2: Forward NTT on Operand B (reuse twiddles and dimensions!)
var forwardB = forwardA;
forwardB.Buffer1 = ctx.Workspace.NttBuf1B;
forwardB.Buffer2 = ctx.Workspace.NttBuf2B;
GpuBigIntNttKernels.ExecuteDualForwardNtt(forwardB);
```

---

## 4. Type Support Matrix & Constraints

When designing AOT Context structures, follow this type support specification:

| Type Category | Examples | Support Status | GPU Device Representation | Host Marshaling Behavior |
| :--- | :--- | :--- | :--- | :--- |
| **Primitive Scalars** | `int`, `uint`, `long`, `ulong`, `float`, `double`, `bool` | **Supported** | Blittable value type | Direct value copy |
| **G# Vector Types** | `Float2`, `Float4`, `Int4`, `Double2` | **Supported** | CUDA vector type (`float4`) | Direct value copy (16-byte aligned) |
| **1D Managed Arrays** | `uint[]`, `float[]`, `byte[]` | **Supported** | Raw device pointer (`T*`) | `cuMemAlloc` + `HtoD` + `DtoH` + `cuMemFree` |
| **Raw Pointers** | `uint*`, `void*`, `IntPtr` | **Supported** | Device pointer (`T*`) | Raw pointer pass (for pre-allocated buffers) |
| **Nested Structs** | Custom user structs | **Supported** | Nested C++ `struct` | Recursive marshaling & unmarshaling |
| **Enums** | `enum MyEnum : int` | **Supported** | Integral scalar type | Cast to underlying integer type |
| **Nullable / Null Arrays** | `uint[]? buf = null` | **Supported** | `nullptr` | Allocation skipped; the device sees `null` (`is null` is true). Reading it (`Length`, an element, `foreach`) throws `NullReferenceException`, as in C# (§7) |
| **Reference Classes** | `class MyClass { ... }` | **Prohibited** | N/A | Compile-time diagnostic error |
| **Multi-dimensional Arrays** | `int[,]`, `float[,,]` | **Prohibited** | N/A | Must flatten to 1D `int[]` |
| **Jagged Arrays** | `int[][]` | **Prohibited** | N/A | Must flatten to 1D `int[]` |
| **Strings & Objects** | `string`, `object` | **Prohibited** | N/A | Managed heap objects not allowed on GPU |
| **Ref Structs** | `ref struct MyRefStruct` | **Prohibited** | N/A | Cannot be boxed or passed as generic arguments |

> [!IMPORTANT]
> **Field Accessibility**: All members within a context struct intended for GPU access must be declared as **`public` fields** or **public auto-properties** (`{ get; set; }`). Private or internal fields are ignored by native struct generators.

---

## 5. Struct Passing Semantics: Value vs Reference

G# supports both pass-by-value and pass-by-reference semantics for context structures:

### Pass-by-Value (`MyContext ctx`) — *Recommended*

```csharp
[GpuKernel]
public static void Compute(AotContexts.MyContext ctx) { ... }
```
- **Device Mechanics**: The native struct is passed by value via CUDA's `__global__` parameter buffer. Fields are loaded into hardware registers or local memory.
- **Array Mutations**: Array fields (`ctx.Output[tid] = ...`) modify shared global GPU memory and are reflected back to the host array.
- **Scalar Mutations**: Modifying a scalar field (`ctx.Length = 10;`) only modifies the local register on that executing thread. It does not affect other threads or the host.

### Pass-by-Reference (`ref MyContext ctx`)

```csharp
[GpuKernel]
public static void Compute(ref AotContexts.MyContext ctx) { ... }
```
- **Device Mechanics**: G# allocates a dedicated GPU memory buffer for the structure itself (`cuMemAlloc_v2(sizeof(Aot_MyContext))`), copies the struct to device memory, and passes the device pointer (`Aot_MyContext*`) to the kernel.
- **Post-Kernel Sync**: After kernel execution, the updated struct is copied back from GPU device memory to the host struct via `cuMemcpyDtoH_v2`.
- **Use Case**: Used when GPU kernel threads need to update scalar metadata (e.g., dynamic error codes, counter offsets, converged iteration counts) that must be read back on the host.

---

## 6. Pre-Allocated Buffers and `LaunchXPtr`

For maximum developer ergonomics during prototyping and standalone calls, `LaunchX(Context)` automatically performs HtoD allocation, data copy, DtoH copy, and freeing.

However, in high-throughput hot loops (e.g., 10,000 iterations per second), repeatedly allocating and freeing GPU buffers induces driver overhead.

### The Two Execution Modes

1. **Host Array Mode (`LaunchKernel(ctx)`)**:
   Pass a struct containing managed `T[]` arrays. Zero boilerplate, full automatic round-trip.
2. **Device Pointer Mode (`LaunchKernelPtr(...)`)**:
   Pass pre-allocated GPU pointers (`IntPtr` or `GpuBuffer<T>.DevicePointer`).
   
> [!WARNING]
> **Device Pointer Mode Constraint**:
> Calling `LaunchKernelPtr` with a struct that contains managed arrays (`T[]`) will throw a `NotSupportedException` at runtime:
> *"Passing a struct with arrays is not supported when isDevicePtr is true."*
> If you require context structs in hot loops with pre-allocated buffers, define the struct fields as `IntPtr` or raw pointers (`uint*`), or use `GpuBuffer<T>`.

---

## 7. Handling Null and Optional Buffers

In many complex kernels, certain scratch buffers or debug telemetry arrays are optional:

```csharp
public struct SolverContext
{
    public float[] MatrixA;
    public float[] VectorB;
    public float[] Solution;
    public float[]? DebugTelemetry; // Optional debug buffer
}
```

G# natively handles `null` array members:
- On the host, if `ctx.DebugTelemetry == null`, the generator **skips** `cuMemAlloc_v2`.
- In the native shadow struct, `native.DebugTelemetry` is set to `IntPtr.Zero` (`nullptr`).
- An empty array (`new float[0]`, `[]`) is not `null`: it has no allocation either, but its device pointer is
  `GSharpArrayProxy.EmptyArrayData`, a non-null address that is never dereferenced (the native CPU build passes the
  pinned address of the empty array). On every path `ctx.DebugTelemetry is null` is false for it and `Length` is 0.
- A launch never rejects a struct whose array field is `null`: a kernel that does not read the field runs normally.
- Reading a `null` array field (`Length`, an element, `foreach`) follows C# and throws `NullReferenceException`: on the
  native CPU build with the place in your source (`G#: a null array is read at Kernels.cu.cs(12,17)`), on the managed
  path as .NET throws it, and on the checked CUDA build. The release CUDA build does not check. The same holds for an
  array local, a device function parameter or an array element that holds `null`. Converting the array to a span
  (`ReadOnlySpan<T> s = ctx.DebugTelemetry;`) gives an empty span, as in C#. If you mean "no data", write `= []`.
- A `null` optional kernel argument (`float[]? bias` passed as `null` or `[]`) keeps its own contract: the kernel reads
  it as an array of length 0, also through a local or a device function.
- On the GPU, the kernel can safely test for presence:

```csharp
[GpuKernel]
public static void Solve(SolverContext ctx)
{
    // Normal computation...
    
    // Optional telemetry logging
    if (ctx.DebugTelemetry != null)
    {
        ctx.DebugTelemetry[ThreadIndex.X] = intermediateResidual;
    }
}
```

---

## 8. Seamless Interop with Existing `[GpuDevice]` Functions

A common concern when refactoring a legacy codebase to use Context structures is whether existing helper functions taking raw pointers (`uint*`, `float*`) need to be rewritten.

**No rewriting is required.** Inside any `[GpuKernel]` or `[GpuDevice]` method, array fields within a context struct are already typed as raw pointers (`T*`):

```csharp
// Existing device function expecting loose pointers
[GpuDevice]
public static void FastMontgomeryMultiply(uint* a, uint* b, uint* result, uint* scratch, int len)
{
    // Unchanged legacy implementation...
}

// Modern kernel receiving clean context
[GpuKernel]
public static void ModernKernel(AotContexts.CryptoContext ctx)
{
    // Pass struct fields directly as raw pointers!
    FastMontgomeryMultiply(ctx.OperandA, ctx.OperandB, ctx.Result, ctx.Scratch, ctx.Length);
}
```

---

## 9. Comprehensive End-to-End Example

Here is a complete, runnable example illustrating context definition, device helper invocation with default and named arguments, kernel implementation, and host invocation:

```csharp
using System;
using Kassyi.GSharp.Core;

namespace MyProject.GpuDemo;

public static partial class VectorMathKernels
{
    // 1. Group arguments into an AotContexts container
    public static class AotContexts
    {
        public struct VectorProcessContext
        {
            public float[] Input;
            public float[] Output;
            public float Factor;
            public int Length;
            public float[]? Telemetry; // Optional buffer (null supported)
        }
    }

    // 2. Define a reusable Device Helper with Default Parameters
    [GpuDevice]
    public static float TransformCell(
        float inputVal, 
        float factor, 
        float offset = 0.0f, 
        bool applySquare = false, 
        float clampMax = 1000.0f)
    {
        float result = (inputVal * factor) + offset;
        if (applySquare) result = result * result;
        if (result > clampMax) result = clampMax;
        return result;
    }

    // 3. Define the GPU Kernel consuming the context and invoking helper with Named Arguments
    [GpuKernel]
    public static void ProcessVectorKernel(AotContexts.VectorProcessContext ctx, float offset = 0.0f)
    {
        int idx = ThreadIndex.X;
        if (idx >= ctx.Length) return;

        // Call 1: Use helper, omitting 'applySquare' (false) and 'clampMax' (1000.0f)
        float primary = TransformCell(ctx.Input[idx], ctx.Factor, offset: offset);
        ctx.Output[idx] = primary;

        // Call 2: If telemetry buffer is present, invoke with named argument 'applySquare: true'
        if (ctx.Telemetry != null)
        {
            ctx.Telemetry[idx] = TransformCell(
                ctx.Input[idx], 
                ctx.Factor, 
                offset: offset, 
                applySquare: true // Selectively override parameter
            );
        }
    }

    // 4. Host Dispatch
    public static void RunDemo()
    {
        const int count = 1024;
        var hostIn = new float[count];
        var hostOut = new float[count];

        for (int i = 0; i < count; i++) hostIn[i] = i * 1.5f;

        // Launch directly using Context struct & named launch arguments!
        VectorMathKernels.LaunchProcessVectorKernel(
            new AotContexts.VectorProcessContext
            {
                Input = hostIn,
                Output = hostOut,
                Factor = 2.0f,
                Length = count
            },
            offset: 10.0f, // Named argument for optional parameter
            gridDimX: 4,
            blockDimX: 256
        );

        // Results in hostOut are immediately synchronized back!
        Console.WriteLine($"Result[0] = {hostOut[0]}"); // (0 * 2.0) + 10 = 10.0
        Console.WriteLine($"Result[1] = {hostOut[1]}"); // (1.5 * 2.0) + 10 = 13.0
    }
}
```

---

## See also

### Language Specification

- [2. Type System and Mapping](../01_language/03_type_system.md)
- [3. Function and Method Invocations](../01_language/04_functions_and_methods.md)

### BCL API > Attributes

- [GpuKernelAttribute Class (G#)](../03_bcl_api/attributes/GpuKernelAttribute.md)
- [GpuDeviceFunctionAttribute Class (G#)](../03_bcl_api/attributes/GpuDeviceFunctionAttribute.md)
