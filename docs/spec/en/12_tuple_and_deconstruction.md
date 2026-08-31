# Specification: Tuple Support and Deconstruction (GSH-02)

## 1. Overview
G# (GSharp) transpiles C# `System.ValueTuple` types and tuple deconstruction syntax into native CUDA C++ code for GPU execution. Because GPU device execution mandates zero dynamic memory allocations, tuples in G# must strictly consist of value types (`struct`, primitive types).

---

## 2. Happy Path Specifications

### 2.1 Tuple Return Values in GPU Device Functions
- `[GpuDeviceFunction]` methods can define `(T1, T2, ...)` or `ValueTuple<T1, T2, ...>` as return types.
- Inside device code, returning tuple literals (e.g., `(a, b)`) or `new ValueTuple<T1, T2>(a, b)` transpiles into CUDA C++ tuple construct/copy logic.

### 2.2 Variable Declaration Deconstruction
- Local variable declaration deconstruction using `var (a, b) = ...` or `(int a, float b) = ...` transpiles to CUDA C++ local variable definitions assigned from tuple elements.

### 2.3 Discards in Deconstruction
- Discards using underscore `_` (e.g., `var (a, _) = ...` or `(a, _) = ...`) ignore unused tuple elements during transpilation and do not create unnecessary temporary variables in CUDA C++.

---

## 3. Edge Case Specifications

### 3.1 Reassignment Deconstruction
- Reassignment of previously declared variables via deconstruction (e.g., `(existingA, existingB) = ...`) transpiles to element-wise assignment into existing local CUDA variables without redeclaring variable types.

### 3.2 Nested Tuple Deconstruction
- Deeply nested tuples (e.g., `((a, b), c) = ...`) transpile recursively to unpack inner tuple elements into individual CUDA variables.

---

## 4. Sad Path & Safety Constraints

### 4.1 Prohibition of Reference Types in Tuples
- Including reference types (`class`, managed object, `string`, managed array) in tuples passed to or used within `[GpuKernel]` or `[GpuDeviceFunction]` is strictly prohibited.
- Violation triggers Roslyn static analysis diagnostics (e.g., `GSH0001` / CS compilation error for invalid device types), blocking compilation prior to GPU code generation.

---

## 5. Memory Model & Zero-Allocation Principles
- All tuple types are value types (`struct`) on the stack or registers in CUDA C++.
- Tuple deconstruction operates entirely with zero dynamic heap allocation.
