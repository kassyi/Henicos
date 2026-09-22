---
title: "System.Runtime.InteropServices: [In] and [Out] Attributes"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Standard .NET attributes used in G# to optimize memory transfers across the PCIe bus."
---

The standard .NET **`[In]`** and **`[Out]`** attributes from `System.Runtime.InteropServices` can be applied to array parameters in `[GpuKernel]` signatures to optimize memory transfers across the PCIe bus.

## Syntax

```csharp
// Execution Context: G# Kernel (GPU)
using System.Runtime.InteropServices;

[GpuKernel]
public static void VectorAdd([In] float[] a, [In] float[] b, [Out] float[] c, int n)
```

## Remarks

- **Default Behavior (`[In, Out]`)**: By default, G# assumes all arrays are read/write. It generates AOT wrapper code that copies data both to the device before kernel execution (`cuMemcpyHtoD`) and back to the host after execution (`cuMemcpyDtoH`).
- **`[In]` Attribute**: Indicates the array is read-only. The AOT transpiler will **skip copying the array back** to the host after execution, saving valuable PCIe bandwidth.
- **`[Out]` Attribute**: Indicates the array is write-only. The AOT transpiler will **skip copying the array to** the device before execution. It still allocates the required memory on the device (`cuMemAlloc`).

Using these attributes appropriately is highly recommended for performance-critical kernels, as redundant PCIe transfers can drastically degrade performance compared to native C++ CUDA.

## Examples

### Hash Kernel Data Transfer Optimization

```csharp
[GpuKernel]
public static unsafe void HashKernel([In] byte[] input, [Out] uint[] output, int numMessages)
{
    // The AOT wrapper will only copy 'input' to the GPU, and only copy 'output' back to the CPU.
}
```

## See also

- [Gpu BCL API Overview](../01_api_overview.md)
