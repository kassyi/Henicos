// Shared by the CUDA prelude (Prelude.cu) and the native CPU prelude (Types.hpp), so that the two define these types
// once and cannot differ. The CPU prelude defines __device__ and __host__ as empty (Platform.hpp).
template <typename T>
struct System_Nullable {
    bool HasValue;
    T Value;
    __device__ inline System_Nullable() : HasValue(false), Value() {}
    __device__ inline System_Nullable(decltype(nullptr)) : HasValue(false), Value() {}
    __device__ inline System_Nullable(T val) : HasValue(true), Value(val) {}
    template <typename U>
    __device__ inline System_Nullable(const System_Nullable<U>& other)
        : HasValue(other.HasValue), Value(static_cast<T>(other.Value)) {}
};

// The scope of a C# using: disposes the resource when control leaves the enclosing block (end, return, break, continue,
// goto). Guards of one block are destroyed in reverse order, as C# disposes nested usings.
template <typename T>
struct __gsharp_using_scope {
    T& resource;
    __device__ inline ~__gsharp_using_scope() { resource.__gsharp_dispose(); }
};

template <class T> __device__ __host__ inline const T* __gsharp_temp_address(const T& v) { return &v; }
