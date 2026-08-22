# 3. Attributes and Semantics

G# introduces specialized C# attributes that govern compilation, entry point semantics, and JIT optimizations.

## 3.1 `[GpuKernel]`

The `[GpuKernel]` attribute acts as the entry point marker for a G# transpilation process. 

### 3.1.1 Semantic Rules
* **Target**: Methods only.
* **Return Type**: Must be `void`. Kernels execute asynchronously on the device and cannot return a primitive value directly. Use pointer arguments (e.g., `int* result`) to output data.
* **Accessibility**: Must be `public static` or `internal static`. Instance methods (requiring a `this` pointer) are strictly prohibited.
* **Transpilation**: The source generator targets the decorated method and transpiles it into an `extern "C" __global__ void` CUDA kernel. 

## 3.2 `[GpuDevice]`

The `[GpuDevice]` attribute marks a helper method intended for execution solely on the GPU device, callable only by `[GpuKernel]` methods or other `[GpuDevice]` methods.

### 3.2.1 Semantic Rules
* **Target**: Methods only.
* **Return Type**: Standard primitive types are permitted (unlike `[GpuKernel]`).
* **Accessibility**: Must be `public static` or `internal static`.
* **Transpilation**: The source generator transpiles the decorated method into an `inline __device__` CUDA C++ function by default.

### 3.2.2 Inlining Control via `[MethodImpl]`
The transpiler respects standard C# `[MethodImpl]` attributes to provide fine-grained control over the CUDA compiler's inlining behavior for `[GpuDevice]` methods. This is critical for managing GPU register pressure and instruction cache.

* `[MethodImpl(MethodImplOptions.AggressiveInlining)]`: Transpiled to `__forceinline__ __device__`. Instructs NVCC to always inline the method, eliminating function call overhead.
* `[MethodImpl(MethodImplOptions.NoInlining)]`: Transpiled to `__noinline__ __device__`. Prevents NVCC from automatically inlining large helper functions, keeping them as separate device function calls to conserve registers in the main kernel.

## 3.3 `[JitConstant]`

The `[JitConstant]` attribute marks a kernel argument as a compile-time constant for the JIT engine. This enables aggressive hardware-level optimizations, particularly register minimization through loop unrolling.

### 3.3.1 Semantic Rules
* **Target**: Kernel parameters only.
* **Allowed Types**: Must be a primitive integral type (e.g., `int`, `uint`).
* **Behavior**:
  1. The parameter is completely removed from the generated C++ function signature.
  2. The source generator injects a preprocessor macro (`#define [ParamName] [Value]`) at the top of the C++ source string during runtime.
  3. The JIT runtime calculates a unique hash for the resulting C++ string. Therefore, invoking the same kernel with different `[JitConstant]` values produces and caches physically distinct GPU binaries.

### 3.3.2 Automatic Loop Unrolling (`#pragma unroll`)
The G# transpiler unconditionally prepends `#pragma unroll` to every `for` loop encountered in the syntax tree.
* If a loop bound utilizes a `[JitConstant]`, the CUDA compiler (NVCC) evaluates the macro substitution at compile time. Identifying a constant trip count, NVCC fully unrolls the loop.
* If the loop bound is dynamic, NVCC ignores the `#pragma unroll` directive safely.

**Example C# Input:**
```csharp
[GpuKernel]
public static unsafe void MultiplyKernel(int* data, [JitConstant] int iterations)
{
    int sum = 0;
    for (int i = 0; i < iterations; i++)
    {
        sum += i;
    }
    data[0] = sum;
}
```

**Transpiled C++ Output (with runtime injection of `iterations = 5`):**
```cpp
#define iterations 5
extern "C" __global__ void MultiplyKernel(int* data)
{
    int sum = 0;
#pragma unroll
    for(int i=0;i<iterations;i++)
    {
        sum+=i;
    }
    data[0]=sum;
}
```
