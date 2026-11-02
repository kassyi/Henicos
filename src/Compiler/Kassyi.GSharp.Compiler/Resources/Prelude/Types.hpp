// --- Standard Types & Structures ---
struct float2 { float x, y; };
struct float3 { float x, y, z; };
struct float4 { float x, y, z, w; };
struct uint3 { uint32_t x, y, z; };
struct dim3 { uint32_t x, y, z; };

template <typename T>
struct System_Nullable {
    bool HasValue;
    T Value;
    System_Nullable() : HasValue(false), Value() {}
    System_Nullable(T val) : HasValue(true), Value(val) {}
};

// The scope of a C# using: disposes the resource when control leaves the enclosing block (end, return, break, continue,
// goto). Guards of one block are destroyed in reverse order, as C# disposes nested usings.
template <typename T>
struct __gsharp_using_scope {
    T& resource;
    inline ~__gsharp_using_scope() { resource.__gsharp_dispose(); }
};

template <class T> inline const T* __gsharp_temp_address(const T& v) { return &v; }

enum class GSharpErrorCode : int32_t {
    None = 0,
    NotSupported = 1,
    InvalidOperation = 2,
    IndexOutOfRange = 3,
    ArgumentOutOfRange = 4,
    Argument = 5,
    NullReference = 6
};
inline void __gsharp_trap(GSharpErrorCode code, const char* message);

// A C# array or span: data is nullptr for a null array (and the default span), and never nullptr for an array that
// exists, also an empty one (#706). flags uses the padding after length (16 bytes as before): bit 0
// (__gsharp_array_null_reads_empty) marks a null optional kernel argument, which reads as an empty array
template <typename T>
struct GSharpArray {
    T* data;
    int length;
    int flags;

    inline GSharpArray() : data(nullptr), length(0), flags(0) {}
    inline GSharpArray(T* d, int l) : data(d), length(l), flags(0) {}
    // A stack array (T name[N]) viewed as a span or array argument; T may be const for a ReadOnlySpan. G# reports the
    // uses that would keep the view beyond the array's scope (GSH0091)
    template <int N>
    inline GSharpArray(T (&storage)[N]) : data(storage), length(N), flags(0) {}

    // The span members that C# calls on a span or an array (Span<T>.Slice, Span<T>.Fill, MemoryExtensions.AsSpan)
    inline GSharpArray<T> Slice(int start) const { return GSharpArray<T>(data + start, length - start); }
    inline GSharpArray<T> Slice(int start, int count) const { return GSharpArray<T>(data + start, count); }
    inline void Fill(const T& value) const { for (int i = 0; i < length; i++) { data[i] = value; } }
    inline void Clear() const { for (int i = 0; i < length; i++) { data[i] = T{}; } }
    // Ported from dotnet/runtime src/libraries/System.Private.CoreLib/src/System/MemoryExtensions.cs
    // (MIT License, commit e750854de9ad6a2ced72feb9b8008e506f04608f): in-place two-pointer swap for Span<T>.Reverse.
    inline void Reverse() const {
        for (int i = 0, j = length - 1; i < j; i++, j--) {
            T tmp = data[i];
            data[i] = data[j];
            data[j] = tmp;
        }
    }
    // Ported from dotnet/runtime src/libraries/System.Private.CoreLib/src/System/Span.Fast.cs and Buffer.cs
    // (MIT License, commit e750854de9ad6a2ced72feb9b8008e506f04608f):
    // CopyTo / TryCopyTo check (uint)length <= (uint)destination.length and copy with memmove overlap semantics.
    template <typename U>
    inline bool TryCopyTo(GSharpArray<U> destination) const {
        if (static_cast<unsigned int>(length) > static_cast<unsigned int>(destination.length)) {
            return false;
        }
        const U* src = data;
        U* dst = destination.data;
        if (dst == src || length <= 0) {
            return true;
        }
        unsigned long long srcAddr = reinterpret_cast<unsigned long long>(src);
        unsigned long long dstAddr = reinterpret_cast<unsigned long long>(dst);
        unsigned long long byteCount = static_cast<unsigned long long>(length) * sizeof(U);
        if (dstAddr - srcAddr < byteCount) {
            for (int i = length - 1; i >= 0; i--) {
                dst[i] = src[i];
            }
        } else {
            for (int i = 0; i < length; i++) {
                dst[i] = src[i];
            }
        }
        return true;
    }
    template <typename U>
    inline void CopyTo(GSharpArray<U> destination) const {
        if (!TryCopyTo(destination)) {
            __gsharp_trap(GSharpErrorCode::Argument, "Destination is too short.");
        }
    }
    inline GSharpArray<T> AsSpan() const { return *this; }
    inline GSharpArray<T> AsSpan(int start) const { return Slice(start); }
    inline GSharpArray<T> AsSpan(int start, int count) const { return Slice(start, count); }
    inline bool IsEmpty() const { return length <= 0; }
    
    // Allows implicit conversion from GSharpArray<U> to GSharpArray<T> if U* is convertible to T* (e.g. T to const T)
    template <typename U>
    inline GSharpArray(const GSharpArray<U>& other) : data(other.data), length(other.length), flags(other.flags) {}
    inline T* begin() { return data; }
    inline T* end() { return data + length; }
    inline const T* begin() const { return data; }
    inline const T* end() const { return data + length; }
    inline int size() const { return length; }
    inline T& operator[](int index) { return data[index]; }
    inline const T& operator[](int index) const { return data[index]; }
    inline operator T*() { return data; }
    inline operator const T*() const { return data; }
};

// A stack array (T name[N]) viewed as a span, for a receiver or a template argument that deduces GSharpArray<T>
template <typename T, int N>
inline GSharpArray<T> __gsharp_stackalloc_span(T (&storage)[N]) { return GSharpArray<T>(storage, N); }
// The storage of a stackalloc outside a declaration, declared before the statement: zeroed where the stackalloc is
// evaluated (every evaluation, as C# allocates a new block each time), then viewed as a span
template <typename T, int N>
inline GSharpArray<T> __gsharp_stackalloc_zero(T (&storage)[N]) {
    for (int i = 0; i < N; i++) { storage[i] = T{}; }
    return GSharpArray<T>(storage, N);
}

template <typename R, typename... Args>
using System_Func = std::function<R(Args...)>;

template <typename... Args>
using System_Action = std::function<void(Args...)>;

template <typename T>
struct Kassyi_GSharp_Core_SharedMemory {
    T* data;
    inline T& operator[](int index) { return data[index]; }
    inline const T& operator[](int index) const { return data[index]; }
    inline operator T*() { return data; }
    inline operator const T*() const { return data; }
};
