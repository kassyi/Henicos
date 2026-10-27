// --- Runtime Error Channel (CPU Dialect) ---
enum class GSharpErrorCode : int32_t {
    None = 0,
    NotSupported = 1,
    InvalidOperation = 2,
    IndexOutOfRange = 3,
    ArgumentOutOfRange = 4,
    Argument = 5
};

struct GSharpErrorSlot {
    std::atomic<int32_t> code{0};
    const char* message{nullptr};

    inline void Reset() noexcept {
        message = nullptr;
        code.store(0, std::memory_order_release);
    }

    inline bool HasError() const noexcept {
        return code.load(std::memory_order_acquire) != 0;
    }

    inline void Report(GSharpErrorCode errCode, const char* msg) noexcept {
        int32_t expected = 0;
        if (code.compare_exchange_strong(expected, static_cast<int32_t>(errCode),
                                         std::memory_order_acq_rel,
                                         std::memory_order_relaxed)) {
            message = msg;
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

// Marks the comparison of a sort that the thread is running, while it lives (Sort.hpp)
struct __gsharp_sort_compare_scope
{
    inline __gsharp_sort_compare_scope() noexcept { __gsharp_sort_compare_depth++; }
    inline ~__gsharp_sort_compare_scope() { __gsharp_sort_compare_depth--; }
};
