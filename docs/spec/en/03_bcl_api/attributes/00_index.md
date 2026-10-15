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
- [CudaImportAttribute](CudaImportAttribute.md): Imports an external CUDA kernel or device function defined in a separate CUDA module.
- [ConstantAttribute](ConstantAttribute.md): Marks a kernel argument as an Ahead-Of-Time (AOT) preprocessor constant.
- [GpuReadOnlyAttribute](GpuReadOnlyAttribute.md): Marks a kernel parameter, field, or property with the const qualifier.
- [AdvancedMappingAttributes](AdvancedMappingAttributes.md): Advanced intrinsic and operator mapping attributes.
- [ParameterInteropAttributes](ParameterInteropAttributes.md): Attributes for controlling memory transfer and ownership.
- [GpuResourceAttribute](GpuResourceAttribute.md): Marks a class or struct as a GPU resource managed via `IGpuResourceScope`.
- [TakesOwnershipAttribute](TakesOwnershipAttribute.md): Indicates that the GPU takes ownership of the pointer.
- [SkipSnapshotConsistencyAttribute](SkipSnapshotConsistencyAttribute.md): Excludes a kernel class or method from isolated snapshot consistency tests.

## See also

### Architecture

- [2. Compiler and Runtime Architecture](../../02_architecture/00_index.md)
- [1. Generator Architecture (`Kassyi.GSharp.Compiler.Generators`)](../../02_architecture/01_aot_transpiler.md)
- [2. Runtime Execution and AOT Loader Pipeline (`Kassyi.GSharp.Runtime`)](../../02_architecture/02_runtime_loader.md)

### BCL API

- [BCL API and Hardware Intrinsics Reference](../00_index.md)
