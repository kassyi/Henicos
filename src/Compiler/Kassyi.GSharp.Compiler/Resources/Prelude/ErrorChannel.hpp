// --- Runtime Error Channel (CPU Dialect) ---
extern thread_local uint3 threadIdx;
extern thread_local uint3 blockIdx;
extern thread_local dim3 gridDim;

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
    DataRace = 9
};

struct GSharpErrorSlot {
    std::atomic<int32_t> code{0};
    uint32_t site_id{0};
    uint32_t thread_linear{0};
    uint32_t block_linear{0};
    uint32_t grid_dim_x{1};
    uint32_t grid_dim_y{1};
    const char* message{nullptr};
    // The first report's message, copied: a message formatted at run time (with the place and the values) does not
    // outlive the function that formats it. Reset keeps the text, so a message just consumed stays readable. The text is
    // UTF-8 (the host reads it so); a longer message is cut before the character that does not fit, never inside one.
    // 512 bytes hold the longest message of the prelude (a missing shuffle lane, with its place and hint)
    char text[512]{};

    static inline thread_local const GSharpErrorSlot* current_launch_slot = nullptr;

    inline GSharpErrorSlot() noexcept {
        current_launch_slot = this;
    }

    inline ~GSharpErrorSlot() noexcept {
        if (current_launch_slot == this) {
            current_launch_slot = nullptr;
        }
    }

    inline void Reset() noexcept {
        site_id = 0;
        thread_linear = 0;
        block_linear = 0;
        grid_dim_x = 1;
        grid_dim_y = 1;
        message = nullptr;
        current_launch_slot = nullptr;
        code.store(0, std::memory_order_release);
    }

    inline bool HasError() const noexcept {
        return code.load(std::memory_order_acquire) != 0;
    }

    inline void CaptureCoordinates() noexcept {
        thread_linear = (threadIdx.x & 0x3FFu) | ((threadIdx.y & 0x3FFu) << 10) | ((threadIdx.z & 0x3FFu) << 20);
        uint32_t gx = gridDim.x > 0u ? gridDim.x : 1u;
        uint32_t gy = gridDim.y > 0u ? gridDim.y : 1u;
        grid_dim_x = gx;
        grid_dim_y = gy;
        block_linear = (blockIdx.z * gy + blockIdx.y) * gx + blockIdx.x;
    }

    inline void Report(GSharpErrorCode errCode, const char* msg, uint32_t siteId = 0) noexcept {
        int32_t expected = 0;
        if (code.compare_exchange_strong(expected, static_cast<int32_t>(errCode),
                                         std::memory_order_acq_rel,
                                         std::memory_order_relaxed)) {
            if (current_launch_slot != nullptr && current_launch_slot != this && current_launch_slot->HasError()) {
                site_id = current_launch_slot->site_id;
                thread_linear = current_launch_slot->thread_linear;
                block_linear = current_launch_slot->block_linear;
                grid_dim_x = current_launch_slot->grid_dim_x;
                grid_dim_y = current_launch_slot->grid_dim_y;
            } else {
                site_id = siteId;
                CaptureCoordinates();
            }
            if (msg == nullptr) {
                message = nullptr;
                return;
            }
            size_t length = std::strlen(msg);
            if (length >= sizeof(text)) {
                length = sizeof(text) - 1;
                // msg[length] is the first byte left out; while it continues a UTF-8 character, leave that character out
                while (length > 0 && (static_cast<unsigned char>(msg[length]) & 0xC0u) == 0x80u) {
                    --length;
                }
            }
            std::memcpy(text, msg, length);
            text[length] = '\0';
            message = text;
        }
    }
};

inline thread_local GSharpErrorSlot __gsharp_host_last_error;
inline thread_local GSharpErrorSlot* __gsharp_active_error_slot = nullptr;

extern "C" __declspec(dllexport) int32_t __gsharp_consume_last_error(const char** outMessage) {
    int32_t c = __gsharp_host_last_error.code.load(std::memory_order_acquire);
    if (c != 0) {
        if (outMessage != nullptr) {
            *outMessage = __gsharp_host_last_error.message;
        }
        __gsharp_host_last_error.Reset();
    }
    return c;
}

extern "C" __declspec(dllexport) int32_t __gsharp_consume_last_error_details(
    const char** outMessage,
    uint32_t* outSiteId,
    uint32_t* outThreadLinear,
    uint32_t* outBlockLinear,
    uint32_t* outGridDimX,
    uint32_t* outGridDimY) {
    int32_t c = __gsharp_host_last_error.code.load(std::memory_order_acquire);
    if (c != 0) {
        if (outMessage != nullptr) {
            *outMessage = __gsharp_host_last_error.message;
        }
        if (outSiteId != nullptr) {
            *outSiteId = __gsharp_host_last_error.site_id;
        }
        if (outThreadLinear != nullptr) {
            *outThreadLinear = __gsharp_host_last_error.thread_linear;
        }
        if (outBlockLinear != nullptr) {
            *outBlockLinear = __gsharp_host_last_error.block_linear;
        }
        if (outGridDimX != nullptr) {
            *outGridDimX = __gsharp_host_last_error.grid_dim_x;
        }
        if (outGridDimY != nullptr) {
            *outGridDimY = __gsharp_host_last_error.grid_dim_y;
        }
        __gsharp_host_last_error.Reset();
    }
    return c;
}

// Records a runtime failure for constructs the CPU dialect cannot execute (e.g. Gpu.InlinePtx).
// First writer wins across OpenMP threads; the managed launch wrapper inspects the slot and throws.
inline void __gsharp_unsupported(const char* message)
{
    if (__gsharp_active_error_slot != nullptr) {
        __gsharp_active_error_slot->Report(GSharpErrorCode::NotSupported, message);
    } else {
        __gsharp_host_last_error.Report(GSharpErrorCode::NotSupported, message);
    }
}

// The depth of the comparisons of MemoryExtensions.Sort that the thread is running (Sort.hpp).
inline thread_local int __gsharp_sort_compare_depth = 0;

// Records a portable trap failure (e.g. empty sequence or out-of-range index in StackLinq).
// First writer wins across OpenMP threads; the managed launch wrapper inspects the slot and throws.
// [WHY] .NET wraps an exception thrown by the comparison of a sort: IndexOutOfRangeException becomes ArgumentException
// (the comparer is inconsistent) and any other InvalidOperationException (ArraySortHelper.Sort).
inline void __gsharp_trap(GSharpErrorCode code, const char* message)
{
    if (__gsharp_sort_compare_depth > 0)
    {
        if (code == GSharpErrorCode::IndexOutOfRange)
        {
            code = GSharpErrorCode::Argument;
            message = "Unable to sort because the IComparer.Compare() method returns inconsistent results.";
        }
        else
        {
            code = GSharpErrorCode::InvalidOperation;
            message = "Failed to compare two elements in the array.";
        }
    }

    if (__gsharp_active_error_slot != nullptr) {
        __gsharp_active_error_slot->Report(code, message);
    } else {
        __gsharp_host_last_error.Report(code, message);
    }
}

inline void __gsharp_trap_site(GSharpErrorCode code, uint32_t site_id)
{
    const char* message = nullptr;
    if (__gsharp_sort_compare_depth > 0)
    {
        if (code == GSharpErrorCode::IndexOutOfRange)
        {
            code = GSharpErrorCode::Argument;
            message = "Unable to sort because the IComparer.Compare() method returns inconsistent results.";
        }
        else
        {
            code = GSharpErrorCode::InvalidOperation;
            message = "Failed to compare two elements in the array.";
        }
    }

    if (__gsharp_active_error_slot != nullptr) {
        __gsharp_active_error_slot->Report(code, message, site_id);
    } else {
        __gsharp_host_last_error.Report(code, message, site_id);
    }
}

inline void __gsharp_trap(GSharpErrorCode code, uint32_t site_id)
{
    __gsharp_trap_site(code, site_id);
}

// Thrown by __gsharp_trap_abort to end the kernel step of the current thread; the launcher catches it around the kernel
// call, inside its OpenMP loop, and stops the launch through the error slot.
struct __gsharp_step_abort {};

// Records a failure after which the step cannot go on (a shared memory placement that fails: there is no memory to hand
// out), then ends the step. The launcher stops the launch, and the host throws the exception of the code.
[[noreturn]] inline void __gsharp_trap_abort(GSharpErrorCode code, const char* message)
{
    __gsharp_trap(code, message);
    throw __gsharp_step_abort{};
}

[[noreturn]] inline void __gsharp_trap_abort(GSharpErrorCode code, uint32_t site_id)
{
    __gsharp_trap_site(code, site_id);
    throw __gsharp_step_abort{};
}

// GSharpArray::flags bit 0: a null optional kernel argument, which reads as an empty array (#838)
#define __gsharp_array_null_reads_empty 1
// The array that an array-typed C# expression reads (Length, an element, foreach): a null array ends the step with
// NullReferenceException, as C# throws (#706), and a null optional kernel argument reads as an empty array. The step
// ends (__gsharp_trap_abort) because the read would dereference nullptr next. An rvalue is returned by value, so that a
// range-based for over it does not keep a reference to a destroyed temporary
template <typename A>
inline A __gsharp_array_ref(A&& array, const char* message)
{
    if (array.data == nullptr && (array.flags & __gsharp_array_null_reads_empty) == 0)
    {
        __gsharp_trap_abort(GSharpErrorCode::NullReference, message);
    }
    return static_cast<A&&>(array);
}

// The element array[index] as a location, with the check of __gsharp_array_ref. The array and the index are both
// evaluated before the check, as C# evaluates them before ldelem / stelem throws (#706)
template <typename A, typename I>
inline decltype(auto) __gsharp_array_at(A&& array, I index, const char* message)
{
    if (array.data == nullptr && (array.flags & __gsharp_array_null_reads_empty) == 0)
    {
        __gsharp_trap_abort(GSharpErrorCode::NullReference, message);
    }
    return array[index];
}

// Marks the comparison of a sort that the thread is running, while it lives (Sort.hpp)
struct __gsharp_sort_compare_scope
{
    inline __gsharp_sort_compare_scope() noexcept { __gsharp_sort_compare_depth++; }
    inline ~__gsharp_sort_compare_scope() { __gsharp_sort_compare_depth--; }
};
