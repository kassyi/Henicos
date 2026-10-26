---
title: "2. Type System and Mapping"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "This document specifies the strict mapping rules between C# primitive types and CUDA C++ types, as well as the pointer and memory semantics enforce..."
---

# 2. Type System and Mapping

This document specifies the strict mapping rules between C# primitive types and CUDA C++ types, as well as the pointer and memory semantics enforced by the G# framework.

## 2.1 Primitive Type Mappings

> [!NOTE]
> `nint` and `nuint` are unsupported due to the ambiguity of target pointer sizes between host and device architectures. Use explicit `long` or `ulong` for 64-bit address offsets.

The G# AST transpiler maps .NET `SpecialType` definitions directly to their CUDA C++ counterparts. Any type not explicitly defined in this mapping is currently unsupported and will result in a compilation error.

| C# Type | .NET SpecialType | CUDA C++ Type | Size (Bytes) |
| :--- | :--- | :--- | :--- |
| `int` | `System_Int32` | `int` | 4 |
| `uint` | `System_UInt32` | `unsigned int` | 4 |
| `short` | `System_Int16` | `short` | 2 |
| `ushort` | `System_UInt16` | `unsigned short` | 2 |
| `byte` | `System_Byte` | `unsigned char` | 1 |
| `sbyte` | `System_SByte` | `char` | 1 |
| `long` | `System_Int64` | `long long` | 8 |
| `ulong` | `System_UInt64` | `unsigned long long` | 8 |
| `float` | `System_Single` | `float` | 4 |
| `double` | `System_Double` | `double` | 8 |
| `bool` | `System_Boolean` | `bool` | 1 |

## 2.2 Custom Structs (User-Defined Types)

G# supports custom user-defined C# `struct` types. When a kernel or device method references a custom `struct` (either as a parameter, local variable, or array element), the transpiler automatically extracts its definition and emits an equivalent C++ `struct` above the kernel. 

To completely prevent "Multiple Definition" conflicts when multiple kernels use identically named structs from different C# namespaces, the transpiler handles namespace collisions transparently under the hood. You do not need to worry about naming conflicts across different namespaces.

### 2.2.1 Struct Constraints
* **Unmanaged Types & Arrays**: Structs should primarily contain primitive types or other structs. While standard reference types (`class`, `string`) are strictly prohibited, **C# managed arrays (`T[]`) are explicitly permitted** as struct fields. The generator transparently applies "Deep Struct Marshaling" to automatically transpose these host arrays into valid device pointers (`T*`) during execution.
* **Nested Structs**: Structs can contain fields of other custom struct types. The AOT transpiler automatically resolves the dependency graph and emits the corresponding C++ struct definitions in topological order (dependencies first).
* **Methods and Properties**: Instance methods and properties within a struct are currently unsupported by the AST transpiler. Structs should be treated strictly as Plain Old Data (POD) containers (`public int X; public int Y;`).
* **Pass-by-Value Limits**: Passing large structs by value to a kernel consumes constant memory and register space. The transpiler emits a warning (`GSharpDiagnostics.LargeStructByValueWarning`) if a struct larger than 32 bytes is passed by value. Passing via `ref` or as an array pointer is recommended for large types.

### 2.2.2 Inline Arrays and Fixed Buffers
G# supports stack-allocated, fixed-size arrays within structs for fast, unmanaged local memory access.
* **`[InlineArray]`**: C# 13 `[System.Runtime.CompilerServices.InlineArray(length)]` attribute is supported. The transpiler converts this to a standard C++ array field and automatically emits `operator[]` overloads for seamless device-side indexing.
* **`unsafe fixed`**: C# `unsafe fixed` buffers (e.g., `public fixed uint Buffer[16];`) are mapped to C++ fixed-size arrays (`unsigned int Buffer[16];`). Pointer arithmetic and indexing on these buffers are natively supported in CUDA C++.

### 2.2.3 Struct Instantiation
While heap-allocated objects (`class`) remain strictly forbidden on the GPU, stack-allocated structs can be instantiated natively within kernels. 
The AOT transpiler supports classic initialization (`new MyStruct()`), C# 9.0 **Target-typed `new` expressions** (`new()`), and C# 10 **`record struct` / Primary Constructors** (`new Point(0, 0)`).

```csharp
// Execution Context: G# Kernel (GPU)
// All expressions are perfectly valid inside a [GpuKernel]
Vector8 vec1 = new Vector8 { X = 1, Y = 2 };
Vector8 vec2 = new() { X = 1, Y = 2 };
Point p = new Point(0, 0); // record struct / Primary Constructor
```
*Note: A constructor of a struct declared in the compilation, a primary constructor included, becomes a C++ function that runs the field and property initializers in declaration order and then the body (`new Point(0, 0)` calls `__gsharp_ctor_...(0, 0)`); the parameters of a primary constructor that members read are stored in the struct, at the offsets of the hidden fields that C# gives them. When the CUDA launcher converts a struct with such a hidden field member by member (its host and device layouts differ), it reads and writes the host value through a generated mirror struct that declares the same fields in the same order and names the hidden ones, after checking that both have the same size and that each named field sits at the same offset. Other constructors (the `System.Numerics.Vector2/3/4` and `Float2/3/4`-style vector types, structs from another assembly) become C++ brace initialization (`float3{x, y, z}`). For C# Object Initializers (e.g., `new() { X = 1 }`), the transpiler emits an Immediately Invoked Function Expression (IIFE) using a C++ lambda (e.g., `[&]() { Point __gsharp_tmp; __gsharp_tmp.X = 1; return __gsharp_tmp; }()`) to safely execute arbitrary member assignments in C++ without requiring explicit constructors.*

**`with` expressions** copy a struct and change members (`var moved = p with { X = 5 };`). The transpiler emits the same kind of lambda: it copies the operand once, assigns the initializers in the written order (each reads the original value) and returns the copy; the source is unchanged. It works for `struct`, `readonly struct`, `record struct` and `readonly record struct`, nested and generic structs, and structs with array fields (a shallow copy, as in C#). The assigned member must be a field or an auto-property. Reported as **GSH0003**: `with` on a class or `record` class (it allocates), a property whose `init` accessor has a body (the device struct has no setter function), and a `[GpuReadOnly]` member.

### 2.2.4 P/Invoke and Layout Compatibility (`__Native_Struct`)
G# generates structurally compatible C++ definitions for C# custom structs. To ensure exact binary layout compatibility during memory transfers (marshaling) and P/Invoke boundary crossings, the generator natively aligns with C#'s `[StructLayout]` configurations (e.g., Sequential or Explicit).
* **Explicit Packing**: If a `Pack = X` property is defined on `[StructLayout]`, the transpiler translates this accurately into `#pragma pack(push, X)` and `#pragma pack(pop)` blocks surrounding the C++ struct.
* **Alignment Guarantees**: The transpiler emits appropriate padding and layout attributes (`__Native_Struct` mapping to `#pragma pack` or `alignas`), guaranteeing that host memory arrays can be safely memcopied to device memory without alignment corruption.

### 2.2.5 CUDA Vector Value Types & Lowering Rules

G# introduces sequentially laid-out vector value types (e.g., `Float2/3/4`, `Int2/3/4`, `UInt2/3/4`, `Double2/3/4`, `Long2/3/4`, `ULong2/3/4`) matching CUDA memory layouts to saturate memory bandwidth effectively (e.g., executing 128-bit vector loads). 

**Transpiler Lowering Rules**
To maintain idiomatic C# coding standards, developers interact with these structs using uppercase properties (`.X`, `.Y`, `.Z`, `.W`). The Roslyn transpilation engine unconditionally lowers these member accesses to their CUDA C++ lowercase equivalents (`.x`, `.y`) during code generation. 

**Nested Vector Alignment (CUDA ABI)**
Nested vector types strictly respect CUDA ABI natural alignment requirements. For example, passing a C# struct with a nested `Float4` vector requires strict 16-byte alignment. The `CudaLayoutRules` enforces specific sizing and alignment based on the number of components:

- **2-component and 4-component vectors** (e.g., `Float2`, `Float4`) align to their total size.
- **3-component vectors**: `Float3`, `Int3`, and `UInt3` are 12 bytes in size with a 4-byte alignment constraint. `Double3`, `Long3`, and `ULong3` are 24 bytes in size with an 8-byte alignment constraint.

G# automatically resolves memory layout discrepancies between the .NET CLR and NVIDIA CUDA ABI by generating `LayoutKind.Explicit` native shadow structs. These explicitly specify field offsets and pad unaligned preceding fields, preventing field offset divergence and device-side memory corruption.

```csharp
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static void VectorLoadStore(Float4* input, Float4* output)
{
    // Triggers a 128-bit coalesced memory load
    Float4 vec = input[ThreadIndex.X];

    // C# uppercase properties are lowered to "vec.x", "vec.y" in emitted C++
    vec.X += 1.0f;
    vec.Y *= 2.0f;

    // Triggers a 128-bit coalesced memory store
    output[ThreadIndex.X] = vec;
}
```

### 2.2.6 Kernel Argument Contexts and Deep Struct Marshaling

To combat "Argument Hell" (kernels with dozens of parameters), G# supports packing kernel arguments into context structures containing managed arrays (`T[]`).

When a custom struct containing arrays is passed to a `Launch` wrapper, the G# generator applies **Deep Struct Marshaling** natively. It automatically creates zero-allocation `stackalloc` blocks on the host to pin and resolve all managed arrays into valid CUDA device pointers (`T*`) within a native shadow struct (`Aot_...`), without requiring developer boilerplate or intermediate classes.

For an in-depth explanation and code examples, see the [Deep Struct Marshaling and Argument Hell Elimination Guide](../04_guides/11_deep_struct_marshaling_guide.md).

## 2.3 Memory Model and Pointers

G# enforces strict rules on memory access to ensure valid device pointer dereferencing and thread safety.

### 2.2.1 Arrays as Pointers
C# arrays provided as kernel parameters are exclusively passed as flat pointers to device memory (Global Memory).
* A C# parameter `int[] data` transpiles to `int* data` in CUDA.
* Multidimensional arrays (`int[,]`) are unsupported. Developers must manually compute offsets on flattened 1D arrays (e.g., `y * width + x`).
* **Implicit Marshaling**: When invoking the generated `LaunchX` stub, the host C# array is automatically pinned via `GCHandle`, allocated on the GPU (`cuMemAlloc`), copied to device memory (`cuMemcpyHtoD`), and copied back after execution (`cuMemcpyDtoH`).

### 2.3.2 Modifiers (`ref` and `out`)
C# parameter modifiers denote specific pointer behaviors:
* `ref T` transpiles to `T*` (A pointer to a mutable variable, typically in Global or Shared Memory).
* `out T` transpiles to `T*` (Functions identical to `ref` in C++, but enforced by C# compiler rules to guarantee initialization).
* **Automatic Pointer Resolution (Cascading)**: When passing a `ref` or `out` variable to another device function, the Semantic Model correctly identifies that the variable is already a pointer. The transpiler safely cascades the reference without generating double-address operators (`&`), preventing `T**` compilation errors.
* **Member Access Transformation**: When accessing fields of a struct passed by `ref` or `out` (or any expression resolved as a pointer type by the semantic model), the AOT AST transpiler automatically converts the C# member access operator (`.`) to the C++ pointer access operator (`->`).

### 2.3.3 Pointer Mathematics and `unsafe` Native Pointers
The `CudaTypeMapper` intrinsically supports `IPointerTypeSymbol`. This means developers can explicitly declare pointer variables (`int* ptr`) within `unsafe` blocks.
Unsafe pointer arithmetic is fully permitted and transpiles 1:1 to C++ (e.g., `int* ptr = data + offset;`). Pointer access is restricted to primitive types and unmanaged structs.

## 2.4 Implicit Typing (`var`)

> [!WARNING]
> **Literal Initialization Constraints**: To prevent subtle type-casting bugs in CUDA C++ (where `auto` might mistakenly infer a signed `int` for `0` when an `unsigned int` was expected), G# prohibits initializing `var` with untyped bare literals. You must use explicit literal suffixes.
> 
> * ❌ `var count = 0;` (Unsupported / Compilation Error)
> * ✨ `var count = 0u;` (Correct: explicitly typed as `uint`)
> * ✨ `var magnitude = 1.0f;` (Correct: explicitly typed as `float`)
>
> This safety constraint is intentionally designed to be "user-friendly" by strictly preventing silent precision loss or signedness errors on the GPU.

G# fully supports implicitly typed local variables (`var`). The transpiler now maps C#'s `var` keyword directly to C++'s `auto` keyword, enabling native type inference at the C++ compiler level.

```csharp
// Execution Context: Host (CPU)
var origin = new Point(0, 0); // Safely transpiled to `auto origin = Point{0, 0};`
var distance = origin.X;      // Safely transpiled to `auto distance = origin.X;`
```
This enables developers to write highly readable, modern C# code, while offloading the type resolution directly to the NVCC compiler during the AOT phase.

## 2.3 Prohibited Allocations

The following C# memory operations are strictly prohibited within a G# kernel:

1. **Heap Allocation**: The `new` keyword cannot be used to instantiate classes or reference types. (e.g., `new int[10]` or `new List<int>()` are invalid; for a small per-thread sequence use `stackalloc` with a `Span<T>`, or an `[InlineArray]` struct plus an `int` count, instead of a `List<T>`). The only exceptions are stack-allocated object creations for value type structs (e.g., `new Vector8()`, target-typed `new()`, or `record struct` instantiation) and both static and dynamic `SharedMemory<T>` declarations.
2. **String Manipulation**: `string` is a managed reference type. Strings cannot be passed to, instantiated, or concatenated within a kernel.
3. **Box/Unbox**: Casting value types to `object` or interfaces is prohibited.

All dynamic heap memory allocations (`new`) must occur strictly on the host (CPU) before the kernel is launched. The generated launcher stubs automatically handle the marshaling of these host arrays to device pointers.

## 2.5 Local Initialization and `[SkipLocalsInit]`

By default, standard C# variables are expected to be initialized. 
However, for high-performance GPU kernels, unnecessary zero-initialization of large structs or buffers can waste registers and instructions.
When a kernel method or module is annotated with `[SkipLocalsInit]`, the transpiler explicitly optimizes away the output of `default` or parameterless `new()` assignment code (e.g., `= ...;`). The local variables are emitted as uninitialized declarations in C++, matching the performance characteristics of raw C++ while relying on the compiler for optimal register allocation.

## 2.6 Constants (`const`)

G# fully supports the use of `const` fields and local variables. The AST transpiler resolves C# constants at compile time and emits them as hardcoded literals in the resulting CUDA C++ code.

1. **`const` Fields:** When a kernel or device method references a class or struct `const` field (e.g., `public const uint MyConst = 42;`), the transpiler directly evaluates the Roslyn `ConstantValue` and substitutes the literal `42` into the generated C++ source.
2. **Local `const`:** Local variable declarations using `const` (e.g., `const int offset = 5;`) are transpiled natively as `const int offset = 5;` in C++, permitting the NVCC compiler to apply standard constant folding and dead-code elimination.
3. **`[GpuReadOnly]` Attribute:** Struct fields annotated with the `[GpuReadOnly]` attribute instruct the transpiler to emit a `const` qualifier on the field in the resulting C++ struct definition. This provides a strong immutability guarantee at the C++ compiler level for specific struct properties.

## See also



### BCL API > Attributes

- [Attributes](../03_bcl_api/attributes/00_index.md)
- [ConstantAttribute Class (G#)](../03_bcl_api/attributes/ConstantAttribute.md)

### BCL API > Math Primitives

- [Math and BitOperations](../03_bcl_api/math_primitives/00_index.md)
- [Vector2, Vector3, Vector4 Structs (System.Numerics)](../03_bcl_api/math_primitives/VectorTypes.md)

