# Advanced: Extending GpuBigInteger with Custom Algorithms

If you are a library contributor or a developer looking to extend GSharp by implementing entirely new arbitrary-precision mathematical algorithms (e.g., GCD, ModPow, Taylor Series expansions, or custom cryptography), this guide outlines the mandatory architectural patterns you must follow.

As explained in the earlier guides, `GpuBigInteger` and `GpuBigFloat` are strictly Host-Side orchestration types. Extending the mathematical ecosystem requires a careful split between Host-side orchestration and GPU-side execution.

---

## 1. The Anatomy of a Math Algorithm

Every advanced algorithm in `Stdlib.Math` must follow this three-step orchestration pattern on the Host CPU:

1. **Sign Evaluation**: Determine the sign of the output based on the input arguments' `.IsNegative` flags.
2. **Buffer Checkout**: Checkout temporary unsigned memory buffers (`GpuUIntBigInteger`) from the `GpuContext` memory pool.
3. **Execution Dispatch**: Dispatch the unsigned math to the GPU, either by reusing existing AST operators or launching a custom `[GpuKernel]`.

Here is the architectural template for a new algorithm, let's call it `MyCustomMath`:

```csharp
// Execution Context: Host (CPU)

using System;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.Contexts;

public static class MyCustomMath
{
    // The public API always runs on the Host CPU and accepts the signed wrapper
    public static GpuBigInteger Compute(GpuContext context, GpuBigInteger a, GpuBigInteger b)
    {
        // 1. Sign Evaluation (Host-Side)
        bool isResultNegative = a.IsNegative ^ b.IsNegative; // Example: XOR logic for multiplication-like operations

        // 2. Dispatch Unsigned Logic
        using var finalResult = new GpuUIntBigInteger(context, context.MaxWords);
        DispatchUnsignedLogic(context, a.Value, b.Value, finalResult);
        GpuUIntBigInteger absoluteResult = finalResult;

        // 3. Return a new Signed Wrapper
        return new GpuBigInteger(absoluteResult, isResultNegative);
    }

    // Implementation depends on chosen strategy (see below)
    private static void DispatchUnsignedLogic(GpuContext context, GpuUIntBigInteger uA, GpuUIntBigInteger uB, GpuUIntBigInteger finalResult)
        => throw new NotImplementedException();
}
```

---

## 1.5. The Role of GpuUIntBigInteger

You might wonder why we extract `.Value` (which returns a `GpuUIntBigInteger`) in Step 2, instead of just passing `GpuBigInteger` or operating on `GpuUIntBigInteger` inside the kernel.

[GpuUIntBigInteger](../03_bcl_api/math_bignum/gpu_big_int_and_uint/GpuUIntBigInteger.md) is **also a Host-Side orchestration type**. While `GpuBigInteger` handles the _sign_ (Signed Wrapper), `GpuUIntBigInteger` handles the _memory_ (Unsigned Wrapper). It is responsible for allocating GPU memory, managing its lifecycle (`IDisposable`), and tracking references.

Because it is a managed class running on the CPU, you cannot instantiate or call methods on `GpuUIntBigInteger` inside a GPU kernel.

When you pass a `GpuUIntBigInteger` as an argument to a `[GpuKernel]` from the Host, the GSharp transpiler automatically unwraps it. The kernel itself only ever sees the underlying `DevicePointer`—represented in C# as a raw `uint[]` buffer. Therefore, your custom kernel signature must accept `uint[]`, not `GpuUIntBigInteger`.

---

## 2. Choosing an Execution Strategy

When implementing the actual `DispatchUnsignedLogic`, you have two choices depending on the complexity of your algorithm.

### Strategy A: Reusing the AST (Expression Trees)

If your new algorithm can be expressed purely as a combination of existing basic operations (Addition, Subtraction, Multiplication, Division), **do not write a custom kernel**. Instead, construct an Expression Tree (AST) using the existing operators and call `.Evaluate()`.

```csharp
// Execution Context: Host (CPU)

using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.Contexts;

public static class MyCustomMath_StrategyA
{
    // ... Compute() method from above ...

    private static void DispatchUnsignedLogic(GpuContext context, GpuUIntBigInteger uA, GpuUIntBigInteger uB, GpuUIntBigInteger finalResult)
    {
        // Re-wrap temporarily just to build the AST
        var tempA = new GpuBigInteger(uA);
        var tempB = new GpuBigInteger(uB);

        // Build the AST: A^2 + B^2
        var expr = (tempA * tempA) + (tempB * tempB);

        // Let the GSharp runtime automatically pool memory and fuse kernels!
        using var evalResult = expr.Evaluate(context, null);
        // copy result into output buffer
    }
}
```

**Benefits**: Zero manual memory management, automatic kernel fusion, and built-in optimization.

### Strategy B: Writing a Custom Kernel

If your algorithm requires complex bitwise manipulation, tight loops, or conditions that cannot be expressed via standard operators (e.g., Lehmer's GCD algorithm or Montgomery Reduction), you must write a custom `[GpuKernel]`.

Inside the kernel, you are no longer dealing with `GpuBigInteger`. You are manipulating raw C# `uint[]` arrays. Because you are implementing arbitrary-precision math, you must use **PTX Hardware Intrinsics** to track carry and borrow bits across 32-bit word boundaries.

```csharp
// Execution Context: G# Kernel (GPU)

using Kassyi.GSharp.Core; // Exposes Gpu Intrinsics and [GpuKernel]

public static class MyCustomMathKernels
{
    // Gpu.AddWithCarry maps directly to native NVIDIA PTX instructions (add.cc / addc)
    [GpuKernel]
    public static void MyCustomUnsignedKernel(uint[] arrayA, uint[] arrayB, uint[] result, int maxWords)
    {
        ulong carry = 0;

        // Process words from least significant to most significant
        for (int i = 0; i < maxWords; i++)
        {
            result[i] = Gpu.AddWithCarry(arrayA[i], arrayB[i], ref carry);
        }
    }
}
```

> [!TIP]
> **Extreme Parallelism**: The single-thread loop above is fine for small buffers, but for megabyte-sized numbers, you should distribute the addition across thousands of threads. Reference `GpuBigIntKernels.cu.cs` to see how GSharp implements parallel chunk-addition using `AtomicAddWithCarry`.

---

## 3. Memory Management (The Zero-Allocation Rule)

If you chose Strategy B (Custom Kernel), you must manually manage your temporary buffers.

> [!WARNING]
> **Never use unpooled `GpuBuffer<T>` to create intermediate scratch space during a loop.** This calls `cudaMalloc` under the hood and will instantly destroy performance and fragment GPU VRAM.

**❌ Bad: Allocating memory in a loop (Anti-Pattern)**

```csharp
// This will trigger cudaMalloc 1000 times! Extremely slow.
for (int i = 0; i < 1000; i++)
{
    using var temp = new GpuBuffer<uint>(1024); // ❌ Bypasses context pool
    // ...
}
```

**✅ Good: Checking out from the Context Pool**
Always checkout scratch buffers from the `GpuContext` and dispose them immediately when done.

```csharp
// Execution Context: Host (CPU)

using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.Contexts;

public static class MyCustomMath_StrategyB
{
    // ... Compute() method from above ...

    private static void DispatchUnsignedLogic(GpuContext context, GpuUIntBigInteger uA, GpuUIntBigInteger uB, GpuUIntBigInteger finalResult)
    {
        // 1. Checkout temporary buffers from the pre-allocated pool
        using var scratch1 = new GpuUIntBigInteger(context, context.MaxWords);
        using var scratch2 = new GpuUIntBigInteger(context, context.MaxWords);

        // 2. Dispatch your custom kernel via the auto-generated wrapper method.
        // The GSharp Source Generator automatically injects Launch{MethodName}
        // methods into the <KernelClass>Wrapper for any [GpuKernel].
        MyCustomMathKernelsWrapper.LaunchMyCustomUnsignedKernel(
            uA.DevicePointer, uB.DevicePointer, finalResult.DevicePointer, context.MaxWords,
            gridDimX: 1, blockDimX: 1
        );
    }
}
```

---

## Summary

By properly isolating the **Host-Side Sign Evaluation** from the **GPU-Side Unsigned Math**, and rigorously utilizing the `GpuContext` memory pool, you can seamlessly extend GSharp's `Stdlib.Math` library with any advanced algorithm imaginable.

## See also

### BCL API > Attributes

- [Attributes](../03_bcl_api/attributes/00_index.md)
- [GpuDeviceFunctionAttribute Class (G#)](../03_bcl_api/attributes/GpuDeviceFunctionAttribute.md)

### BCL API > Math Primitives

- [Math and BitOperations](../03_bcl_api/math_primitives/00_index.md)

### Guides

- [4. Practical Guides and Best Practices](00_index.md)
- [Advanced Math & Lazy Evaluation (Expression Trees)](07_stdlib_math_and_lazy_evaluation.md)
