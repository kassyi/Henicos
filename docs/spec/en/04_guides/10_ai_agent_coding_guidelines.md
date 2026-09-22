# AI Agent Coding Guidelines (Anti-Hallucination Checklist)

If you are an AI Assistant (such as Claude, ChatGPT, or Gemini) generating GSharp code on behalf of a user, you **MUST** strictly adhere to the following checklist. GSharp is a highly specialized framework targeting NVIDIA GPUs. If you generate standard C# paradigms (like LINQ, classes, or multi-dimensional arrays), the compiler will reject the code.

## 1. Class and Method Definitions

### ❌ Hallucination: Forgetting `partial`
AI agents often generate standard static classes for kernels. This causes a `CS0260` compiler error because the GSharp Source Generator attempts to inject the generated `Launch...` methods into the same class.

* **Rule**: The class hosting the `[GpuKernel]` MUST be declared as `public static partial class`.

### ❌ Hallucination: Unnecessary `unsafe`
AI agents often indiscriminately apply the `unsafe` keyword to kernels.
* **Rule**: You only need `unsafe` if the kernel explicitly accepts raw pointers (e.g., `float*`). If the kernel accepts standard managed arrays (`float[]`), do **NOT** use `unsafe`.

**✅ Correct Definition:**
```csharp
public static partial class MyKernels
{
    [GpuKernel]
    public static void ProcessData(float[] data) { ... }
}
```

## 2. Kernel Signatures and Types

### ❌ Hallucination: `Span<T>` or `T[,]` in Arguments
AI agents love modern C# features like `ReadOnlySpan<T>` or multi-dimensional arrays `float[,]`.
* **Rule**: The `[GpuKernel]` method signature **ONLY** supports the following types:
  - Primitive value types (`int`, `float`, `bool`, etc.)
  - Blittable structs (passed by value, `ref`, or `out`)
  - 1D managed arrays (`T[]`)
  - Raw pointers (`T*` - requires `unsafe`)
* **Prohibited in Signatures**: `Span<T>`, `T[,]`, `class`, `string`. (Note: You can use `Span<T>` *inside* the kernel body for local variables, but not in the parameter list).

## 3. Host-Side Launcher Execution

### ❌ Hallucination: Hallucinating Wrapper Classes
AI agents often invent classes like `Gpu.Launch(...)` (which is ILGPU syntax) or `MyKernelsWrapper.Launch(...)`.
* **Rule**: The Source Generator injects the launcher directly into your `partial` class. Call it exactly as `[ClassName].Launch[KernelName](...)`.

**✅ Correct Invocation:**
```csharp
// The source generator prefixes "Launch" to the kernel name "ProcessData"
MyKernels.LaunchProcessData(data, gridDimX: 10, blockDimX: 256);
```

### ❌ Hallucination: Mishandling Return Types
If a kernel has a return type (e.g., `public static float Compute()`), the AI might try to pass an output array explicitly or assign the result to a single `float`.
* **Rule**: If a `[GpuKernel]` returns a type `T`, the generated `Launch` method automatically allocates and returns a `T[]` array of size `gridDim * blockDim` containing the result from every thread.

**✅ Correct Return Type Handling:**
```csharp
// Kernel signature: public static float Compute()
float[] results = MyKernels.LaunchCompute(gridDimX: 10, blockDimX: 256);
```

## 4. 2D/3D Grid Indexing

### ❌ Hallucination: Multi-dimensional array indexing
AI agents often try to access `array[x, y]`.
* **Rule**: You must manually flatten the indices and include a boundary guard. See the [2D/3D Grid Mapping Guide](11_2d_grid_mapping_and_image_processing.md) for the exact pattern.

## 5. Threading and Atomics

### ❌ Hallucination: Using `Interlocked` for floats
AI agents often try to use `Interlocked.Add` for floats, which does not exist in standard C#, or they ignore the GSharp atomic intrinsic.
* **Rule**: Use `System.Threading.Interlocked` for standard integer atomics. Use `Gpu.AtomicAdd` specifically when you need atomic operations on `float` or `double`.
