// --- Runtime Error Channel (CPU Dialect) ---
// GSharpErrorCode and __gsharp_trap are declared in Types.hpp so GSharpArray<T>::CopyTo can report Argument errors.

struct GSharpErrorSlot {
    std::atomic<int32_t> code{0};
    const char* message{nullptr};
    // The first report's message, copied: a message formatted at run time (with the place and the values) does not
    // outlive the function that formats it. Reset keeps the text, so a message just consumed stays readable. The text is
    // UTF-8 (the host reads it so); a longer message is cut before the character that does not fit, never inside one.
    // 512 bytes hold the longest message of the prelude (a missing shuffle lane, with its place and hint)
    char text[512]{};

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
