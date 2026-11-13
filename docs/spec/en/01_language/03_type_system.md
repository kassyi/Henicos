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
| `sbyte` | `System_SByte` | `signed char` | 1 |
| `long` | `System_Int64` | `long long` | 8 |
| `ulong` | `System_UInt64` | `unsigned long long` | 8 |
| `float` | `System_Single` | `float` | 4 |
| `double` | `System_Double` | `double` | 8 |
| `bool` | `System_Boolean` | `bool` | 1 |
| `char` | `System_Char` | `char16_t` | 2 |

### 2.1.1 Arithmetic Conversions

G# follows the C# numeric promotions, not the C++ usual arithmetic conversions. The two differ where `uint` meets a signed type: in mixed binary operators, in unary minus and in the branches of a conditional. C# computes `uint` mixed with `int`, `short` or `sbyte` as `long` (64 bits), negates a `uint` as a `long` and converts both branches of a target-typed conditional to its type; C++ computes all three as `unsigned int`. The transpiler writes the C# conversions where C++ would pick another type, so a kernel gives the C# result on the GPU and on the CPU paths:

| C# | Type in C# | Generated C++ |
| :--- | :--- | :--- |
| `u + n`, `u < n` (`uint u`, `int n`) | `long` | `(long long)u+(long long)n`, `(long long)u<(long long)n` |
| `u + s` (`short s` or `sbyte s`) | `long` | `(long long)u+(long long)s` |
| `u - 8` (a non-negative `int` constant) | `uint` | `u-8` |
| `u + -8` | `long` | `(long long)u+(-8LL)` |
| `-u` | `long` | `-(long long)u` |
| `long y = c ? u : n;` | `long` | `(c?(long long)u:(long long)n)` |

```csharp
// Execution Context: Device (GPU)
uint index = indices[tid];
int limit = limits[tid];        // may be negative
if (index < limit)              // compared as long: false when limit is negative
{
    output[tid] = data[index];
}
```

A mixed comparison or division is a 64-bit operation on the GPU. To compute in 32 bits, convert one operand so that both have the same type, as you would in C#:

```csharp
// Execution Context: Device (GPU)
uint sum = u + (uint)n;         // uint + uint: 32-bit arithmetic
```

## 2.2 Custom Structs (User-Defined Types)

G# supports custom user-defined C# `struct` types. When a kernel or device method references a custom `struct` (either as a parameter, local variable, or array element), the transpiler automatically extracts its definition and emits an equivalent C++ `struct` above the kernel. 

To completely prevent "Multiple Definition" conflicts when multiple kernels use identically named structs from different C# namespaces, the transpiler handles namespace collisions transparently under the hood. You do not need to worry about naming conflicts across different namespaces.

### 2.2.1 Struct Constraints
* **Unmanaged Types & Arrays**: Structs should primarily contain primitive types or other structs. While standard reference types (`class`, `string`) are strictly prohibited, **C# managed arrays (`T[]`) are explicitly permitted** as struct fields. The generator transparently applies "Deep Struct Marshaling" to copy these host arrays to the device; on the device each array field is a 16-byte `GSharpArray<T>` (data pointer, length and flags), so its `Length` is known on the device (bounds checks follow [6. Semantics](06_semantics.md)).
* **Nested Structs**: Structs can contain fields of other custom struct types. The AOT transpiler automatically resolves the dependency graph and emits the corresponding C++ struct definitions in topological order (dependencies first).
* **Methods and Properties**: Constructors (primary constructors included), auto-properties, computed properties and properties that use the `field` keyword, user-defined conversion operators, `operator true` / `operator false`, and a `Dispose()` called by a `using` are transpiled ([2.2.3](#223-struct-instantiation)). Instance methods are not: an instance `[GpuDeviceFunction]` is reported as [GSH0066](../03_bcl_api/diagnostics/GSH0066.md), and a call from GPU code to an instance method of a struct as [GSH0107](../03_bcl_api/diagnostics/GSH0107.md). Write a `static` `[GpuDeviceFunction]` that takes the struct as a parameter instead.
* **Pass-by-Value Limits**: Kernel parameters are passed in the kernel parameter space, which holds 4 KB. The transpiler emits the warning [GSH0064](../03_bcl_api/diagnostics/GSH0064.md) when the parameters of a kernel exceed it; pass large structs in an array or a buffer. (The former 32-byte warning GSH0014 is deprecated and disabled.)

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
*Note: A constructor of a struct declared in the compilation, a primary constructor included, becomes a C++ function that runs the field and property initializers in declaration order and then the body (`new Point(0, 0)` calls `__gsharp_ctor_...(0, 0)`); the parameters of a primary constructor that members read are stored in the struct, at the offsets of the hidden fields that C# gives them. When the CUDA launcher converts a struct with such a hidden field member by member (its host and device layouts differ), it reads and writes the host value through a generated mirror struct that declares the same fields in the same order and names the hidden ones, after checking that both have the same size and that each named field sits at the same offset. The `System.Numerics.Vector2/3/4` and `Float2/3/4`-style component constructors stay C++ brace initialization (`float3{x, y, z}`), and `new Vector3(1f)`, `new Vector3(xy, z)` and the other `Vector` constructors that the dialect maps call a prelude function. `new S()` with no parameterless constructor declared is `default(S)`, as in C#: no initializer runs. Inside a generic struct, `new T()` calls the parameterless constructor of the type argument of the construction being emitted, so a declared one (or a parameterless primary constructor) runs its initializers and body. Any other constructor without a body in the compilation (a struct from another assembly, a declared parameterless constructor included, `new Vector3(span)`) is reported as **GSH0003**. Arguments named in another order than the parameters are evaluated in the written order, as in C#. For C# Object Initializers (e.g., `new() { X = 1 }`), the transpiler emits an Immediately Invoked Function Expression (IIFE) using a C++ lambda (e.g., `[&]() { Point __gsharp_tmp; __gsharp_tmp.X = 1; return __gsharp_tmp; }()`) to safely execute arbitrary member assignments in C++ without requiring explicit constructors.*

**`with` expressions** copy a struct and change members (`var moved = p with { X = 5 };`). The transpiler emits the same kind of lambda: it copies the operand once, assigns the initializers in the written order (each reads the original value) and returns the copy; the source is unchanged. It works for `struct`, `readonly struct`, `record struct` and `readonly record struct`, nested and generic structs, and structs with array fields (a shallow copy, as in C#). The assigned member can be a field, an auto-property, or a property whose `set` or `init` accessor has a body (a computed property, or one that uses the `field` keyword), whose accessor the copy calls. Reported as **GSH0003**: `with` on a class or `record` class (it allocates), and a `[GpuReadOnly]` member.

**Properties that use the `field` keyword** (`public int X { get; set => field = value * 2; }`) run their accessors as in C#: the device stores the backing field, under a name of its own, at the offset C# gives it, and calls the accessors as member functions, the ones C# implements (`get;`, `set;`, `init;`) included. A property initializer and an assignment in a constructor to a property without a setter write the backing field, as in C#; a compound assignment or increment of such a property in a constructor reads it through the getter and writes the backing field, as in C#. When the launcher converts such a struct member by member, it copies the stored value through the generated mirror struct without running the accessors. Reported as **GSH0003**: an operator macro whose target is such a property (the target writes through a reference, which would skip the setter).

**Private fields** of a struct that a kernel takes or returns cross to the device and back like the others. When the launcher converts the struct member by member, it copies the fields that its generated code cannot name (private fields, and internal fields of another assembly) through the generated mirror struct, which has the struct's layout kind, `Pack`, `Size` and, for `LayoutKind.Explicit`, its `[FieldOffset]` values; before the first copy the launcher checks the mirror's size and the offset of each of its fields against the struct. The type of every field must be accessible from the kernel's class, where the launcher is generated: a field of a private nested type is reported as **GSH0108**.

### 2.2.4 Struct Layout and Host-Device Transfer

For every user-defined `struct`, G# computes both the CLR host layout and the target dialect's C++ device layout from
one ordered member list: instance fields, auto-property backing fields, `fixed` buffers, `[InlineArray(N)]` elements,
hidden fields of captured primary-constructor parameters, and backing fields of field-like events (`event Action E;`).

* **`[StructLayout]` (`Sequential`, `Explicit`, `Auto`, `Pack`, `Size`)**: G# supports `LayoutKind.Sequential` (the C#
  default for structs), `LayoutKind.Explicit` with `[FieldOffset]`, and `LayoutKind.Auto`, along with `Pack` and `Size`:
  * **Packing (`Pack`)**: When `Pack > 0`, the transpiler wraps the C++ struct definition in `#pragma pack(push, Pack)`
    and `#pragma pack(pop)`, capping field alignment on the device just as on the CLR.
  * **Explicit Offsets (`[FieldOffset]`)**: In `LayoutKind.Explicit` structs, fields sharing an offset are emitted in an
    anonymous C++ `union`, and byte gaps between fields are filled with explicit padding arrays. Partially overlapping
    fields, offsets that are not multiples of a field's device alignment, union arms whose host and device layouts
    differ, and non-array reference fields are rejected as **GSH0078**; fields whose types have no device representation
    (such as `decimal` or `DateTime`) are reported as **GSH0079**.
  * **Minimum Size (`Size`)**: `StructLayout.Size` sets a minimum byte size. On the device, trailing padding is emitted
    and the total size is rounded up to the struct's alignment (`sizeof` is always a multiple of `alignof`), verified at
    C++ compile time via `static_assert` on `sizeof`, `alignof`, and every field's `offsetof`.
* **Structs from Referenced Assemblies**: Because `[StructLayout]` and `[FieldOffset]` are pseudo-custom attributes not
  returned by Roslyn's `GetAttributes()` on metadata symbols, G# reads layout kind, `Pack`, `Size`, and field offsets
  directly from the referenced assembly's ECMA-335 `TypeDef`, `ClassLayout`, and `FieldLayout` metadata tables.
* **Identical (Block Copy) vs. Converted (`AotContexts` Shadow) Transfer**:
  * **Identical (Block Copy)**: When the host layout is statically known (`Sequential` or `Explicit`, with no managed
    array or reference fields), the total byte size and every member's offset and byte size match between host and
    device, and all nested structs are also identical, G# classifies the struct as **identical**. No shadow struct or
    field-by-field conversion is emitted: value arguments and struct arrays are transferred as a single memory block.
  * **Run-Time Layout Guard**: For every block-copied struct (including nested identical structs), the generated
    launcher verifies once per kernel at first launch that `Unsafe.SizeOf<T>()` and `Unsafe.ByteOffset` of each
    accessible non-pointer field match the compiler's prediction, throwing `InvalidOperationException` via `LayoutGuard`
    if the runtime CLR layout ever diverges.
  * **Converted (Per-Field Copy)**: When host and device layouts differ—for example, due to vector alignment padding,
    `StructLayout.Size` alignment rounding, `LayoutKind.Auto`, or array fields—G# emits a `LayoutKind.Explicit` native
    shadow struct `{KernelClass}.AotContexts.{Name}` (with any trailing `Context` suffix stripped; name collisions
    report **GSH0058**) together with `Marshal_` and `Unmarshal_` methods that copy members individually. Passing an
    array of converted structs or returning a converted struct converts elements through a pooled `ArrayPool<byte>`
    buffer and reports warning **GSH0080**.

### 2.2.5 Vector Value Types and Alignment

G# maps vector structs in `Kassyi.GSharp.Core` (`Float2/3/4`, `Int2/3/4`, `UInt2/3/4`, `Double2/3/4`, `Long2/3/4`,
`ULong2/3/4`) and `System.Numerics` (`Vector2`, `Vector3`, `Vector4`) directly to native CUDA vector types (`float2`,
`float4`, `double4`, and so on) to enable wide coalesced memory loads and stores.

**Field Access Lowering**
In C#, vector components are accessed through uppercase fields (`.X`, `.Y`, `.Z`, `.W`). During transpilation, the
dialect lowers these member accesses to CUDA C++ lowercase fields (`.x`, `.y`, `.z`, `.w`).

**Device vs. Host Alignment**
Device byte sizes and alignments come from the target dialect's type table (`GSharpCuda.yml` on CUDA, `StandardCpp.yml`
on CPU):

* **2-component vectors** align to their total size: `Vector2`, `Float2`, `Int2`, and `UInt2` are 8 bytes with 8-byte
  alignment on CUDA; `Double2`, `Long2`, and `ULong2` are 16 bytes with 16-byte alignment.
* **3-component vectors** align to their element size: `Vector3`, `Float3`, `Int3`, and `UInt3` are 12 bytes with
  4-byte alignment; `Double3`, `Long3`, and `ULong3` are 24 bytes with 8-byte alignment.
* **4-component vectors** align to 16 bytes on CUDA: `Vector4`, `Float4`, `Int4`, and `UInt4` are 16 bytes with 16-byte
  alignment; `Double4`, `Long4`, and `ULong4` are 32 bytes with 16-byte alignment.

On the CLR host, these sequentially laid-out vector structs align to their scalar element size (4 bytes for 32-bit
components, 8 bytes for 64-bit components). When a nested vector sits at the same offset on both sides (for instance, at
offset 0 or after suitably sized fields), the enclosing struct remains **identical** and is block-copied. When CUDA
alignment introduces padding that the CLR does not add (such as a `byte` followed by a `Float4`), G# classifies the
struct as **converted** and copies its fields through `{KernelClass}.AotContexts.{Name}` at their exact device offsets.

```csharp
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static void VectorLoadStore(Float4* input, Float4* output)
{
    // Triggers a 128-bit coalesced memory load
    Float4 vec = input[ThreadIndex.X];

    // C# uppercase fields are lowered to "vec.x", "vec.y" in emitted C++
    vec.X += 1.0f;
    vec.Y *= 2.0f;

    // Triggers a 128-bit coalesced memory store
    output[ThreadIndex.X] = vec;
}
```

### 2.2.6 Kernel Argument Contexts and Deep Struct Marshaling

To avoid long kernel parameter lists, G# allows packing kernel arguments into context structs that hold 1D managed
arrays (`T[]`) alongside scalars and nested structs.

A struct containing array fields is always marshaled as a **converted** type through `{KernelClass}.AotContexts.{Name}`.
In the host shadow struct, each array field is a 16-byte `GSharpArrayProxy` (holding data pointer, length, and flags);
in the emitted C++ struct, it is a `GSharpArray<T>`. Host arrays are pinned with `GCHandle` (never `stackalloc`):

* **CUDA**: `AotContexts.Marshal_<Key>` tracks host arrays in `DeviceArrayRegions` so aliased fields share one device
  buffer, allocates device memory with `cuMemAllocAsync`, and pins each host array via
  `GCHandle.Alloc(..., GCHandleType.Pinned)` around `cuMemcpyHtoDAsync_v2` (and `cuMemcpyDtoHAsync_v2` in `Unmarshal_`).
  All device regions are freed in the launcher's `finally` block via `DeviceArrayRegions.Free` (passing an array of
  structs that contain array fields warns **GSH0081**).
* **Native CPU**: `Marshal_<Key>` pins each non-null host array (or converted element shadow array) with
  `GCHandle.Alloc(..., GCHandleType.Pinned)`, records the handle in a per-argument `List<IntPtr>`, passes the pinned
  address in place, and frees every handle in the launcher's `finally` block.

For a complete guide and runnable examples, see
[4.11 Deep Struct Marshaling Guide](../04_guides/11_deep_struct_marshaling_guide.md).

## 2.3 Memory Model and Pointers

G# enforces strict rules on memory access to ensure valid device pointer dereferencing and thread safety.

### 2.2.1 Arrays as Pointers
C# arrays provided as kernel parameters are exclusively passed as flat pointers to device memory (Global Memory).
* A C# parameter `int[] data` transpiles to `GSharpArray<int> data` in CUDA: a 16-byte struct that holds the device pointer and the length (the last 4 bytes are reserved), so that `data.Length` is known on the device.
* Multidimensional arrays (`int[,]`) are unsupported. Developers must manually compute offsets on flattened 1D arrays (e.g., `y * width + x`).
* **Implicit Marshaling**: When invoking the generated `LaunchX` stub, the host C# array is automatically pinned via `GCHandle`, allocated on the GPU (`cuMemAlloc`), copied to device memory (`cuMemcpyHtoD`), and copied back after execution (`cuMemcpyDtoH`).
* **Null and empty arrays**: an array-like kernel argument means what it means in a C# call. `null` and an empty array are different values on the device, and the launcher neither rejects nor replaces either one, whatever the nullable annotation of the parameter. Reading a `null` array (`Length` or another `System.Array` member, an element, `foreach`) throws `NullReferenceException` on every path, with the place in the source on the native CPU build and on CUDA ([6. Semantics](06_semantics.md)); so does reading `Length`, an element or `foreach` of a default `ImmutableArray<T>`. An empty array reads as length 0 and is not `null`. `is null`, `== null` and converting the array to a span read nothing and never throw. A span, `Memory<T>`, `ArraySegment<T>` or `ImmutableArray<T>` over an empty array is not `default` (it compares unequal to `null`), and a `Memory<T>?` has a value whenever C# gives it one, `(Memory<T>?)Memory<T>.Empty` included.

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

1. **Heap Allocation**: The `new` keyword cannot be used to instantiate classes or reference types. (e.g., `new List<int>()` is invalid; for a small per-thread sequence use `stackalloc` with a `Span<T>`, or an `[InlineArray]` struct plus an `int` count, instead of a `List<T>`). The only exceptions are stack-allocated object creations for value type structs (e.g., `new Vector8()`, target-typed `new()`, or `record struct` instantiation), both static and dynamic `SharedMemory<T>` declarations, and a local variable initialized with an array of a constant size (`int[] a = new int[10];`), which becomes a zero-filled stack array of the thread (§2.5).
2. **String Manipulation**: `string` is a managed reference type. Strings cannot be passed to, instantiated, or concatenated within a kernel.
3. **Box/Unbox**: Casting value types to `object` or interfaces is prohibited.

All dynamic heap memory allocations (`new`) must occur strictly on the host (CPU) before the kernel is launched. The generated launcher stubs automatically handle the marshaling of these host arrays to device pointers.

## 2.5 Local Initialization and `[SkipLocalsInit]`

C# fills the storage of a `stackalloc T[N]` without an initializer and of a `new T[N]` with zeros. G# keeps that meaning on every dialect: such a local declaration becomes a value-initialized C++ array.

```csharp
Span<int> s = stackalloc int[4];          // int s[4]{};
ReadOnlySpan<int> r = stackalloc int[4];  // const int r[4]{};
int[] a = new int[4];                     // int a[4]{};
```

`[SkipLocalsInit]` leaves out the zero fill of a `stackalloc` without an initializer, as in C#: `Span<int> s = stackalloc int[4];` becomes `int s[4];`. C# leaves the contents unspecified, and in the emitted C++ reading an element before it is written is undefined behavior (nvcc may drop the read and every store that depends on it), so write every element before you read it. The attribute is looked for where Roslyn looks for it: the method, the property of an accessor, the method around a local function or lambda, every containing type, and the module.

- `new T[N]` is zeroed whatever the attribute says, because a C# array always starts zeroed.
- A `ReadOnlySpan<T>` over a `stackalloc` is a `const` C++ array, which needs an initializer, so it is always zeroed (C# allows a compiler to ignore the attribute).
- A `stackalloc` in a loop body is zeroed on every iteration, because C# allocates it again on each one (the CA2014 warning). Declare it before the loop when one zero fill is enough.
- A `stackalloc` outside a declaration (an argument, a pointer declaration, a loop condition) follows the same rules: its storage is declared before the statement and zeroed where the `stackalloc` is evaluated, every time, unless `[SkipLocalsInit]` applies.

Cost: nvcc keeps an array whose indices it resolves at compile time in registers, and removes the stores of zeros that a later write overwrites. An array indexed at run time stays in local memory and keeps the zero stores (about one 16-byte store per 16 bytes of the array, on every evaluation). For a work array whose elements are all written before they are read, add `[SkipLocalsInit]`, as you would in C#.

An explicit initializer is never left out: `Pair p = default;`, `default(Pair)`, `new Pair()`, `new()`, `new(1, 2)` and object initializers keep their C# value under `[SkipLocalsInit]`, as in C#, where they are initializations of their own and not part of the zero fill of the locals. A local without an initializer (`Pair p;`) is emitted without one; C# does not let it be read before it is assigned. nvcc removes the zero stores of an explicit `= default` that later writes overwrite in registers; a large struct kept in local memory (a big `[InlineArray]`) pays for them, as it does in C#. To avoid that cost, leave out the initializer and assign every element before reading it.

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

