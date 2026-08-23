# 4. Limitations and Diagnostics

This document outlines the strict limitations imposed on C# code executed within the G# framework and details the diagnostic strategies employed to enforce these constraints.

## 4.1 Prohibited C# Features

The CUDA execution environment lacks a garbage collector, a robust runtime type information (RTTI) system, and standard .NET libraries. Consequently, the following C# features are explicitly prohibited inside `[GpuKernel]` methods:

| Feature | Reason for Prohibition |
| :--- | :--- |
| **Object Instantiation (`new`)** | Lack of a managed heap and garbage collector on the GPU. (Note: Stack-allocated `struct` instantiation and `SharedMemory<T>` are exceptions and fully supported). |
| **Strings (`string`)** | Strings are managed reference types. Fixed-size character arrays must be used instead, though not natively supported yet. |
| **LINQ (`System.Linq`)** | Relies heavily on delegates, iterators, and dynamic allocations. |
| **Virtual Methods / Interfaces** | Dynamic dispatch (v-tables) is unsupported in the current AOT transpiler. |
| **Exceptions (`throw`, `try-catch`)** | Exception unwinding is not supported in CUDA device code. |
| **Recursion** | Heavily restricted due to the highly limited thread stack size (typically a few kilobytes per thread) on GPUs. |
| **Reflection** | The .NET reflection metadata is entirely absent on the GPU. |
| **Generics (Partial)** | True runtime generics are unsupported. The AOT transpiler must be capable of generating a concrete C++ template instantiation if generics are used. |

## 4.2 Standard Diagnostics and Warnings

Because G# utilizes the standard Roslyn C# compiler to validate syntax before transpilation, certain standard C# rules must be carefully managed.

### 4.2.1 Suppressing Pointer Warnings
To manipulate raw memory arrays (e.g., `int*`), G# kernels must be marked with the `unsafe` keyword. Developers should configure their `.csproj` to allow unsafe blocks (`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`).

### 4.2.2 Dereference Warnings (`CS8602`)
When utilizing C# 8.0+ Nullable Reference Types, passing raw pointers might trigger `CS8602` (Dereference of a possibly null reference).
Since all pointer validity checks are pushed to the JIT/Runtime phase, developers may need to suppress these warnings locally via `#pragma warning disable CS8602` or configure analyzer rules appropriately.

## 4.3 Custom G# Diagnostics

The `Kassyi.Generators.GSharp` AOT pipeline enforces constraints natively during compilation by traversing the C# AST and reporting specific MSBuild diagnostics:

| ID | Severity | Title | Condition |
| :--- | :--- | :--- | :--- |
| **GSH0001** | Error | Object creation not supported | Triggered when `new` or `new()` is used on anything other than `SharedMemory<T>` or unmanaged `struct` types. |
| **GSH0002** | Error | String usage not supported | Triggered when the `string` keyword is used. |
| **GSH0003** | Error | Unsupported syntax | Triggered when unknown/unsupported C# syntax nodes are encountered. |
| **GSH0004** | Error | GpuKernel must return void | Triggered when a `[GpuKernel]` method declares a non-void return type. |
| **GSH1001** | Warning | Excessive static shared memory | Triggered when a static `SharedMemory<T>` allocation exceeds 32,768 bytes, which can reduce warp occupancy. |
| **GSH1002** | Warning | Large struct passed by value | Triggered when a custom struct $\ge$ 32 bytes is passed by value. Advises passing by array pointer or `ref` to conserve constant memory. |
| **GSH0005** | Error | GpuBigIntExpr Direct Assignment | Triggered when a `GpuBigIntExpr` (from lazy evaluation operators) is assigned to a local variable (`var x = a + b;`) without being resolved into `.Value`. Prevents silent "computation lost" bugs. |
