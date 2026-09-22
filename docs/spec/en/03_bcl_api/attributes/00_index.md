---
title: "Attributes"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Attributes for marking kernels, device functions, and optimizing parameters."
---

# Attributes

Attributes for marking kernels, device functions, and optimizing parameters.

## In This Section

- [GpuKernelAttribute](GpuKernelAttribute.md): Marks a C# method as a GPU execution entry point.
- [GpuDeviceFunctionAttribute](GpuDeviceFunctionAttribute.md): Marks a C# method to be transpiled into a GPU-callable device function.
- [ConstantAttribute](ConstantAttribute.md): Marks a kernel argument as an Ahead-Of-Time (AOT) preprocessor constant.
- [AdvancedMappingAttributes](AdvancedMappingAttributes.md): Advanced intrinsic and operator mapping attributes.
- [ParameterInteropAttributes](ParameterInteropAttributes.md): Attributes for controlling memory transfer and ownership.
- [TakesOwnershipAttribute](TakesOwnershipAttribute.md): Indicates that the GPU takes ownership of the pointer.

## See also

### Core Attributes
- [GpuKernelAttribute](GpuKernelAttribute.md)
- [GpuDeviceFunctionAttribute](GpuDeviceFunctionAttribute.md)

### Memory & Parameter Attributes
- [ConstantAttribute](ConstantAttribute.md)
- [ParameterInteropAttributes](ParameterInteropAttributes.md)
- [TakesOwnershipAttribute](TakesOwnershipAttribute.md)
- [AdvancedMappingAttributes](AdvancedMappingAttributes.md)

### BCL & Architecture
- [Gpu BCL API Overview](../00_index.md)
- [1. AOT Transpiler Architecture](../../02_architecture/01_aot_transpiler.md)
- [2. Runtime Kernel Loader](../../02_architecture/02_runtime_loader.md)

### Related Guides
- [Memory Safety Patterns Guide](../../04_guides/02_memory_safety_patterns.md)
- [Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)
