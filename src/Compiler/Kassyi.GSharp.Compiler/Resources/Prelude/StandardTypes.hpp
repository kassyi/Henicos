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

#if !defined(__CUDACC__)
// ErrorChannel.hpp
inline uint32_t __gsharp_failure_mark() noexcept;
template <typename T>
inline void __gsharp_dispose_unwinding(T& resource, uint32_t failureMark) noexcept;
#endif

// The scope of a C# using: disposes the resource when control leaves the enclosing block (end, return, break, continue,
// goto). Guards of one block are destroyed in reverse order, as C# disposes nested usings.
// [WHY] On the CPU a failed check ends the step with an exception (ErrorChannel.hpp), and C# disposes the resource as
// the exception leaves the using (a finally), so the guard disposes it while the exception unwinds too; a failure of
// that Dispose cannot leave a destructor during unwinding and is recorded there instead (__gsharp_dispose_unwinding).
// When the block ends normally, a failure of Dispose leaves the destructor like any failed check. On CUDA the failing
// thread exits and does not dispose.
template <typename T>
struct __gsharp_using_scope {
    T& resource;
#if defined(__CUDACC__)
    __device__ inline ~__gsharp_using_scope() { resource.__gsharp_dispose(); }
#else
    int unwinding = std::uncaught_exceptions();
    uint32_t failureMark = __gsharp_failure_mark();
    inline ~__gsharp_using_scope() noexcept(false) {
        if (std::uncaught_exceptions() == unwinding) {
            resource.__gsharp_dispose();
        } else {
            __gsharp_dispose_unwinding(resource, failureMark);
        }
    }
#endif
};

template <class T> __device__ __host__ inline const T* __gsharp_temp_address(const T& v) { return &v; }
