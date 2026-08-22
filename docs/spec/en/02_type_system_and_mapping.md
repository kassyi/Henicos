# 2. Type System and Mapping

This document specifies the strict mapping rules between C# primitive types and CUDA C++ types, as well as the pointer and memory semantics enforced by the G# framework.

## 2.1 Primitive Type Mappings

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

> [!NOTE]
> `nint` and `nuint` are unsupported due to the ambiguity of target pointer sizes between host and device architectures. Use explicit `long` or `ulong` for 64-bit address offsets.

## 2.2 Custom Structs (User-Defined Types)

G# supports custom user-defined C# `struct` types. When a kernel or device method references a custom `struct` (either as a parameter, local variable, or array element), the transpiler automatically extracts its definition and emits an equivalent C++ `struct` above the kernel.

### 2.2.1 Struct Constraints
* **Unmanaged Only**: Structs must contain only primitive types or other unmanaged structs. Reference types (`class`, `string`) are strictly prohibited.
* **Methods and Properties**: Instance methods and properties within a struct are currently unsupported by the AST transpiler. Structs should be treated strictly as Plain Old Data (POD) containers (`public int X; public int Y;`).
* **Pass-by-Value Limits**: Passing large structs by value to a kernel consumes constant memory and register space. The transpiler emits a warning (`GSharpDiagnostics.LargeStructByValueWarning`) if a struct larger than 32 bytes is passed by value. Passing via `ref` or as an array pointer is recommended for large types.

## 2.3 Memory Model and Pointers

G# enforces strict rules on memory access to ensure valid device pointer dereferencing and thread safety.

### 2.2.1 Arrays as Pointers
C# arrays provided as kernel parameters are exclusively passed as flat pointers to device memory (Global Memory).
* A C# parameter `int[] data` transpiles to `int* data` in CUDA.
* Multidimensional arrays (`int[,]`) are unsupported. Developers must manually compute offsets on flattened 1D arrays (e.g., `y * width + x`).
* **Implicit Marshaling**: When invoking the generated `Launch_X` stub, the host C# array is automatically pinned via `GCHandle`, allocated on the GPU (`cuMemAlloc`), copied to device memory (`cuMemcpyHtoD`), and copied back after execution (`cuMemcpyDtoH`).

### 2.2.2 Modifiers (`ref` and `out`)
C# parameter modifiers denote specific pointer behaviors:
* `ref T` transpiles to `T*` (A pointer to a mutable variable, typically in Global or Shared Memory).
* `out T` transpiles to `T*` (Functions identical to `ref` in C++, but enforced by C# compiler rules to guarantee initialization).

### 2.2.3 Pointer Mathematics
Unsafe pointer arithmetic is permitted within G# kernels (e.g., `int* ptr = data + offset;`). However, pointer access is restricted to primitive types mapped in section 2.1.

## 2.3 Prohibited Allocations

The following C# memory operations are strictly prohibited within a G# kernel:

1. **Heap Allocation**: The `new` keyword cannot be used to instantiate classes or reference types. (e.g., `new int[10]` or `new List<int>()` are invalid).
2. **String Manipulation**: `string` is a managed reference type. Strings cannot be passed to, instantiated, or concatenated within a kernel.
3. **Box/Unbox**: Casting value types to `object` or interfaces is prohibited.

All dynamic heap memory allocations (`new`) must occur strictly on the host (CPU) before the kernel is launched. The generated launcher stubs automatically handle the marshaling of these host arrays to device pointers.
