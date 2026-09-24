# AI Agent Coding Guidelines (Anti-Hallucination Checklist)

If you are an AI Assistant (such as Claude, ChatGPT, or Gemini) generating GSharp code on behalf of a user, you **MUST** strictly adhere to the following checklist. GSharp is a highly specialized framework targeting NVIDIA GPUs. If you generate standard C# paradigms (like LINQ, classes, or multi-dimensional arrays), the compiler will reject the code.

## 1. Class and Method Definitions

### ❌ Hallucination: Unnecessary `unsafe`

AI agents often indiscriminately apply the `unsafe` keyword to kernels.

- **Rule**: You only need `unsafe` if the kernel explicitly accepts raw pointers (e.g., `float*`). If the kernel accepts standard managed arrays (`float[]`), do **NOT** use `unsafe`.

**✅ Correct Definition:**

```csharp
public static class MyKernels
{
    [GpuKernel]
    public static void ProcessData(float[] data) { ... }
}
```

## 2. Kernel Signatures and Types

### ❌ Hallucination: `Span<T>` or `T[,]` in Arguments

AI agents love modern C# features like `ReadOnlySpan<T>` or multi-dimensional arrays `float[,]`.

- **Rule**: The `[GpuKernel]` method signature **ONLY** supports the following types:
    - Primitive value types (`int`, `float`, `bool`, etc.)
    - Blittable structs (passed by value, `ref`, or `out`)
    - 1D managed arrays (`T[]`)
    - Raw pointers (`T*` - requires `unsafe`)
- **Prohibited in Signatures**: `Span<T>`, `T[,]`, `class`, `string`. (Note: You can use `Span<T>` _inside_ the kernel body for local variables, but not in the parameter list).

## 3. Host-Side Launcher Execution

### ❌ Hallucination: Calling Kernel Directly or Using ILGPU Syntax

AI agents often invent calls like `Gpu.Launch(...)` (which is ILGPU syntax) or attempt to call `MyKernels.LaunchProcessData(...)` directly on the kernel class.

- **Rule**: The GSharp Source Generator emits a companion static class suffixed with `Wrapper` (e.g., `MyKernelsWrapper`). Always invoke the launcher via `[ClassName]Wrapper.Launch[KernelName](...)`.

**✅ Correct Invocation:**

```csharp
// The source generator emits a companion static class "MyKernelsWrapper" with prefix "Launch"
MyKernelsWrapper.LaunchProcessData(data, gridDimX: 10, blockDimX: 256);
```

### ❌ Hallucination: Mishandling Return Types

If a kernel has a return type (e.g., `public static float Compute()`), the AI might try to pass an output array explicitly or assign the result to a single `float`.

- **Rule**: If a `[GpuKernel]` returns a type `T`, the generated `Launch` method automatically allocates and returns a `T[]` array of size `gridDim * blockDim` containing the result from every thread.

**✅ Correct Return Type Handling:**

```csharp
// Kernel signature: public static float Compute()
float[] results = MyKernelsWrapper.LaunchCompute(gridDimX: 10, blockDimX: 256);
```

## 4. 2D/3D Grid Indexing

### ❌ Hallucination: Multi-dimensional array indexing

AI agents often try to access `array[x, y]`.

- **Rule**: You must manually flatten the indices and include a boundary guard. See the [2D/3D Grid Mapping Guide](09_2d_grid_mapping_and_image_processing.md) for the exact pattern.

## 5. Threading and Atomics

### ❌ Hallucination: Using `Interlocked` for floats

AI agents often try to use `Interlocked.Add` for floats, which does not exist in standard C#, or they ignore the GSharp atomic intrinsic.

- **Rule**: Use `System.Threading.Interlocked` for standard integer atomics. Use `Gpu.AtomicAdd` specifically when you need atomic operations on `float` or `double`.

## See also

### Getting Started

- [0. Getting Started with G# (GSharp)](../00_getting_started/00_index.md)

### Guides

- [4. Practical Guides and Best Practices](00_index.md)
- [The CPU to GPU Paradigm Shift: Anti-Patterns & Best Practices](06_antipatterns_and_best_practices.md)
