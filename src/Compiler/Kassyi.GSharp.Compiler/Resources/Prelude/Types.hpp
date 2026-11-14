// --- Standard Types & Structures ---
struct float2 { float x, y; };
struct float3 { float x, y, z; };
struct float4 { float x, y, z, w; };
struct uint3 { uint32_t x, y, z; };
struct dim3 { uint32_t x, y, z; };

#include "Prelude/StandardTypes.hpp"
enum class GSharpErrorCode : int32_t {
    None = 0,
    NotSupported = 1,
    InvalidOperation = 2,
    IndexOutOfRange = 3,
    ArgumentOutOfRange = 4,
    Argument = 5,
    NullReference = 6,
    DivideByZero = 7,
    Overflow = 8,
    DataRace = 9,
    SwitchExpression = 10,
    ArgumentNull = 11
};
// A failed C# check: records the error and ends the thread's kernel step (ErrorChannel.hpp). [[noreturn]] belongs on
// the first declaration, so that every check below already knows that the call does not return. inline and the cold
// attributes are on the definition only: GCC warns when a declaration that is not a definition combines inline and
// noinline
[[noreturn]] void __gsharp_trap(GSharpErrorCode code, const char* message);

template <GSharpErrorCode ErrorCode>
inline const char* __gsharp_range_message() {
    if (ErrorCode == GSharpErrorCode::IndexOutOfRange) {
        return "Index was outside the bounds of the array.";
    }
    if (ErrorCode == GSharpErrorCode::Argument) {
        return "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.";
    }
    return "Index was out of range.";
}

// The message of a view without an array (CheckedSlice, CheckedCopyTo), as .NET words it
template <GSharpErrorCode ErrorCode>
inline const char* __gsharp_view_message() {
    if (ErrorCode == GSharpErrorCode::InvalidOperation) {
        return "The underlying array is null.";
    }
    if (ErrorCode == GSharpErrorCode::ArgumentNull) {
        return "Value cannot be null. (Parameter 'destinationArray')";
    }
    return "Object reference not set to an instance of an object.";
}

// text[index] of a constant C# string, written as a UTF-16 literal (u"..."): the code unit C# reads, and
// IndexOutOfRangeException outside the string. N counts the terminating zero, which C# does not have
template <int N>
inline char16_t __gsharp_string_char(const char16_t (&text)[N], int index) {
    if (static_cast<unsigned int>(index) >= static_cast<unsigned int>(N - 1)) {
        __gsharp_trap(GSharpErrorCode::IndexOutOfRange, "Index was outside the bounds of the array.");
        return 0;
    }
    return text[index];
}

// A C# array or span: data is nullptr for a null array (and the default span), and never nullptr for an array that
// exists, also an empty one (#706). flags is the reserved padding after length (16 bytes), always 0
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
    template <GSharpErrorCode ErrorCode = GSharpErrorCode::ArgumentOutOfRange>
    inline GSharpArray<T> Slice(int start, const char* message = nullptr) const {
        if (static_cast<unsigned int>(start) > static_cast<unsigned int>(length)) {
            __gsharp_trap(ErrorCode, message != nullptr ? message : __gsharp_range_message<ErrorCode>());
        }
        return GSharpArray<T>(data + start, length - start);
    }
    template <GSharpErrorCode ErrorCode = GSharpErrorCode::ArgumentOutOfRange>
    inline GSharpArray<T> Slice(int start, int count, const char* message = nullptr, const char* countMessage = nullptr) const {
        if (static_cast<unsigned int>(start) > static_cast<unsigned int>(length)) {
            __gsharp_trap(ErrorCode, message != nullptr ? message : __gsharp_range_message<ErrorCode>());
        }
        if (static_cast<unsigned int>(count) > static_cast<unsigned int>(length - start)) {
            __gsharp_trap(ErrorCode, countMessage != nullptr ? countMessage : message != nullptr ? message : __gsharp_range_message<ErrorCode>());
        }
        return GSharpArray<T>(data + start, count);
    }
    // The same with error sites (ErrorSiteTable) that carry the message and the parameter name C# gives each failure: a
    // start outside the array reports startSite, a count past its end countSite (startSite when countSite is 0)
    template <GSharpErrorCode ErrorCode = GSharpErrorCode::ArgumentOutOfRange>
    inline GSharpArray<T> Slice(int start, int count, uint32_t startSite, uint32_t countSite = 0u) const {
        if (static_cast<unsigned int>(start) > static_cast<unsigned int>(length)) {
            __gsharp_trap(ErrorCode, startSite);
            return GSharpArray<T>();
        }
        if (static_cast<unsigned int>(count) > static_cast<unsigned int>(length - start)) {
            __gsharp_trap(ErrorCode, countSite != 0u ? countSite : startSite);
            return GSharpArray<T>();
        }
        return GSharpArray<T>(data + start, count);
    }
    // One side of Array.Copy, in the order .NET checks it: a negative length is ArgumentOutOfRange (length), a negative
    // index ArgumentOutOfRange (sourceIndex or destinationIndex), a range past the end Argument (sourceArray or
    // destinationArray). Each failure is an error site (uint32_t) or a message (const char*)
    template <typename S>
    inline GSharpArray<T> CopySlice(int start, int count, S lengthSite, S indexSite, S longSite) const {
        if (count < 0) {
            __gsharp_trap(GSharpErrorCode::ArgumentOutOfRange, lengthSite);
            return GSharpArray<T>();
        }
        if (start < 0) {
            __gsharp_trap(GSharpErrorCode::ArgumentOutOfRange, indexSite);
            return GSharpArray<T>();
        }
        if (start > length || count > length - start) {
            __gsharp_trap(GSharpErrorCode::Argument, longSite);
            return GSharpArray<T>();
        }
        return GSharpArray<T>(data + start, count);
    }
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
            __gsharp_trap(GSharpErrorCode::Argument, "Destination is too short. (Parameter 'destination')");
        }
    }
    // Slice and CopyTo of an ArraySegment<T> or ImmutableArray<T> (ArrayViewMethods.GetChecks): a default value has no
    // array (data is nullptr; an empty one has an array), which C# reports first (IfDefault); CopyTo then reports a
    // destination without an array (IfDestinationNull, None for a span) before it compares the lengths
    template <GSharpErrorCode IfDefault>
    inline GSharpArray<T> CheckedSlice(int start) const {
        if (data == nullptr) {
            __gsharp_trap(IfDefault, __gsharp_view_message<IfDefault>());
            return GSharpArray<T>();
        }
        return Slice(start);
    }
    template <GSharpErrorCode IfDefault>
    inline GSharpArray<T> CheckedSlice(int start, int count) const {
        if (data == nullptr) {
            __gsharp_trap(IfDefault, __gsharp_view_message<IfDefault>());
            return GSharpArray<T>();
        }
        return Slice(start, count);
    }
    template <GSharpErrorCode IfDefault, GSharpErrorCode IfDestinationNull, typename U>
    inline void CheckedCopyTo(GSharpArray<U> destination) const {
        if (IfDefault != GSharpErrorCode::None && data == nullptr) {
            __gsharp_trap(IfDefault, __gsharp_view_message<IfDefault>());
            return;
        }
        if (IfDestinationNull != GSharpErrorCode::None && destination.data == nullptr) {
            __gsharp_trap(IfDestinationNull, __gsharp_view_message<IfDestinationNull>());
            return;
        }
        if (IfDestinationNull == GSharpErrorCode::ArgumentNull) {
            // The destination is an array, which C# copies into through Array.Copy and names destinationArray
            if (!TryCopyTo(destination)) {
                __gsharp_trap(GSharpErrorCode::Argument, "Destination array was not long enough. Check the destination index, length, and the array's lower bounds. (Parameter 'destinationArray')");
            }
            return;
        }
        CopyTo(destination);
    }
    inline GSharpArray<T> AsSpan() const { return *this; }
    template <GSharpErrorCode ErrorCode = GSharpErrorCode::ArgumentOutOfRange>
    inline GSharpArray<T> AsSpan(int start, const char* message = nullptr) const { return Slice<ErrorCode>(start, message); }
    template <GSharpErrorCode ErrorCode = GSharpErrorCode::ArgumentOutOfRange>
    inline GSharpArray<T> AsSpan(int start, int count, const char* message = nullptr, const char* countMessage = nullptr) const { return Slice<ErrorCode>(start, count, message, countMessage); }
    template <GSharpErrorCode ErrorCode = GSharpErrorCode::ArgumentOutOfRange>
    inline GSharpArray<T> AsSpan(int start, int count, uint32_t startSite, uint32_t countSite = 0u) const { return Slice<ErrorCode>(start, count, startSite, countSite); }
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
// The array operand of a System.Array method (Array.Fill, Clear, Reverse): ArgumentNullException for a null array,
// before any other check of the call, as .NET throws it
template <typename T>
inline GSharpArray<T> __gsharp_array_argument(GSharpArray<T> array) {
    if (array.data == nullptr) {
        __gsharp_trap(GSharpErrorCode::ArgumentNull, "Value cannot be null. (Parameter 'array')");
        return GSharpArray<T>();
    }
    return array;
}
// The source operand of Array.Copy, checked with its destination first: ArgumentNullException for a null source, then
// for a null destination, before the lengths, as .NET checks them
template <typename T, typename U>
inline GSharpArray<T> __gsharp_copy_arguments(GSharpArray<T> source, GSharpArray<U> destination) {
    if (source.data == nullptr) {
        __gsharp_trap(GSharpErrorCode::ArgumentNull, "Value cannot be null. (Parameter 'sourceArray')");
        return GSharpArray<T>();
    }
    if (destination.data == nullptr) {
        __gsharp_trap(GSharpErrorCode::ArgumentNull, "Value cannot be null. (Parameter 'destinationArray')");
        return GSharpArray<T>();
    }
    return source;
}
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
