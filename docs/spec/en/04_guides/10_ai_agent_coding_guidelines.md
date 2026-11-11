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

### ❌ Hallucination: Multi-dimensional arrays `T[,]` or reference collections in Arguments

AI agents often assume `Span<T>` cannot be used in kernel parameters, or attempt to pass multi-dimensional arrays `float[,]` and reference collections like `List<T>`.

- **Rule**: In device code and kernel signatures, 1D managed arrays (`T[]`), `Span<T>`, and `ReadOnlySpan<T>` (along with `Memory<T>`, `ReadOnlyMemory<T>`, `ArraySegment<T>`, and `ImmutableArray<T>`) ARE supported with runtime bounds checks.
- **Prohibited in Signatures**: Multi-dimensional arrays `T[,]`, reference-type collections like `List<T>`, `class`, and `string`.

## 3. Host-Side Launcher Execution

### ❌ Hallucination: Omitting Partial Modifier or Using ILGPU Syntax

AI agents often invent calls like `Gpu.Launch(...)` (ILGPU syntax) or forget to declare the kernel class as `partial`.

- **Rule**: The GSharp Source Generator enhances the user's kernel class using Roslyn `partial class` source generation (enforced by `GSH0036`). Kernel classes MUST be declared with the `partial` modifier (e.g., `public partial class MyKernels`). Always invoke the launcher via `[ClassName].Launch[KernelName](...)`.

**✅ Correct Invocation:**

```csharp
// The kernel class must be partial; launcher methods are injected directly into MyKernels with prefix "Launch"
public partial class MyKernels
{
    [GpuKernel]
    public static void ProcessData(float[] data) { ... }
}

MyKernels.LaunchProcessData(data, gridDimX: 10, blockDimX: 256);
```

### ❌ Hallucination: Mishandling Return Types

If a kernel has a return type (e.g., `public static float Compute()`), the AI might try to pass an output array explicitly or assign the result to a single `float`.

- **Rule**: If a `[GpuKernel]` returns a type `T`, the generated `Launch` method automatically allocates and returns a `T[]` array of size `gridDim * blockDim` containing the result from every thread.

**✅ Correct Return Type Handling:**

```csharp
// Kernel signature: public static float Compute()
float[] results = MyKernels.LaunchCompute(gridDimX: 10, blockDimX: 256);
```

## 4. 2D/3D Grid Indexing

### ❌ Hallucination: Multi-dimensional array indexing

AI agents often try to access `array[x, y]`.

- **Rule**: You must manually flatten the indices and include a boundary guard. See the [2D/3D Grid Mapping Guide](09_2d_grid_mapping_and_image_processing.md) for the exact pattern.

## 5. Threading and Atomics

### ❌ Hallucination: Using `Interlocked` for floats or assuming `Gpu.AtomicAdd` supports `double`

AI agents often try to use `Interlocked.Add` for floats, which does not exist in standard C#, or assume `Gpu.AtomicAdd` supports `double`.

- **Rule**: Use `System.Threading.Interlocked` for standard integer atomics. Use `Gpu.AtomicAdd` when you need atomic addition on `int`, `uint`, `ulong`, or `float`. Note that `Gpu.AtomicAdd` does **NOT** support `double`.

## See also

### Getting Started

- [0. Getting Started with G# (GSharp)](../00_getting_started/00_index.md)

### Guides

- [4. Practical Guides and Best Practices](00_index.md)
- [The CPU to GPU Paradigm Shift: Anti-Patterns & Best Practices](06_antipatterns_and_best_practices.md)
