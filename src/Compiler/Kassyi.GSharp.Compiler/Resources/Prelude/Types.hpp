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

template <typename T>
struct GSharpArray {
    T* __restrict__ data;
    int length;

    inline GSharpArray() : data(nullptr), length(0) {}
    inline GSharpArray(T* d, int l) : data(d), length(l) {}
    
    // Allows implicit conversion from GSharpArray<U> to GSharpArray<T> if U* is convertible to T* (e.g. T to const T)
    template <typename U>
    inline GSharpArray(const GSharpArray<U>& other) : data(other.data), length(other.length) {}
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


