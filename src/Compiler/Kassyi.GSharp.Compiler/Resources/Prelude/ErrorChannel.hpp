// --- Runtime Error Channel (CPU Dialect) ---
extern thread_local uint3 threadIdx;
extern thread_local uint3 blockIdx;
extern thread_local dim3 gridDim;

// GSharpErrorCode and __gsharp_trap are declared in Types.hpp so GSharpArray<T>::CopyTo can report Argument errors.

struct GSharpErrorSlot {
    std::atomic<int32_t> code{0};
    uint32_t site_id{0};
    uint32_t thread_linear{0};
    uint32_t block_x{0};
    uint32_t block_yz{0};
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
        block_x = 0;
        block_yz = 0;
        message = nullptr;
        current_launch_slot = nullptr;
        code.store(0, std::memory_order_release);
    }

    inline bool HasError() const noexcept {
        return code.load(std::memory_order_acquire) != 0;
    }

    inline void CaptureCoordinates() noexcept {
        thread_linear = (threadIdx.x & 0x3FFu) | ((threadIdx.y & 0x3FFu) << 10) | ((threadIdx.z & 0x3FFu) << 20);
        block_x = blockIdx.x;
        block_yz = (blockIdx.y & 0xFFFFu) | ((blockIdx.z & 0xFFFFu) << 16);
    }

    // Records the error when the slot has none yet (the first report of a launch wins); true when it did
    inline bool Report(GSharpErrorCode errCode, const char* msg, uint32_t siteId = 0) noexcept {
        int32_t expected = 0;
        if (!code.compare_exchange_strong(expected, static_cast<int32_t>(errCode),
                                          std::memory_order_acq_rel,
                                          std::memory_order_relaxed)) {
            return false;
        }
        if (current_launch_slot != nullptr && current_launch_slot != this && current_launch_slot->HasError()) {
            site_id = current_launch_slot->site_id;
            thread_linear = current_launch_slot->thread_linear;
            block_x = current_launch_slot->block_x;
            block_yz = current_launch_slot->block_yz;
        } else {
            site_id = siteId;
            CaptureCoordinates();
        }
        CopyMessage(msg);
        return true;
    }

    // Replaces the error that the calling thread reported before: the failure of a Dispose that runs while an earlier
    // failure of the same thread leaves the using, as C# replaces the exception (__gsharp_dispose_unwinding)
    inline void Replace(GSharpErrorCode errCode, const char* msg, uint32_t siteId) noexcept {
        site_id = siteId;
        CaptureCoordinates();
        CopyMessage(msg);
        code.store(static_cast<int32_t>(errCode), std::memory_order_release);
    }

private:
    inline void CopyMessage(const char* msg) noexcept {
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
    uint32_t* outBlockX,
    uint32_t* outBlockYZ) {
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
        if (outBlockX != nullptr) {
            *outBlockX = __gsharp_host_last_error.block_x;
        }
        if (outBlockYZ != nullptr) {
            *outBlockYZ = __gsharp_host_last_error.block_yz;
        }
        __gsharp_host_last_error.Reset();
    }
    return c;
}

// Thrown by a failed check (__gsharp_trap and the functions below) to end the kernel step of the calling thread there,
// as the thread of the GPU exits: the launcher catches it around the step, and the thread does not run again while the
// other threads of the launch run on. The failure was recorded before the throw
struct __gsharp_step_abort {};

// The depth of the comparisons of MemoryExtensions.Sort that the thread is running (Sort.hpp).
inline thread_local int __gsharp_sort_compare_depth = 0;
// The failures the calling OpenMP thread has recorded, and whether the last one is the one its launch reports: a using
// that disposes its resource while a failure unwinds the step (__gsharp_dispose_unwinding) reads both
inline thread_local uint32_t __gsharp_failures = 0;
inline thread_local bool __gsharp_failure_reported = false;
// Set while such a Dispose runs after a failure of this thread that its launch reports: a failure of the Dispose then
// replaces it, as C# replaces the exception
inline thread_local bool __gsharp_failure_replaces = false;

inline uint32_t __gsharp_failure_mark() noexcept { return __gsharp_failures; }

// Records a failure of the calling thread into the slot of its launch (the host slot outside a launch). The first
// failure of a launch wins across OpenMP threads; the managed launch wrapper inspects the slot and throws.
inline void __gsharp_record_failure(GSharpErrorCode code, const char* message, uint32_t site_id) noexcept
{
    GSharpErrorSlot& slot =
        __gsharp_active_error_slot != nullptr ? *__gsharp_active_error_slot : __gsharp_host_last_error;
    __gsharp_failures++;
    if (__gsharp_failure_replaces) {
        slot.Replace(code, message, site_id);
        return;
    }
    __gsharp_failure_reported = slot.Report(code, message, site_id);
}

// [WHY] .NET wraps an exception thrown by the comparison of a sort: IndexOutOfRangeException becomes ArgumentException
// (the comparer is inconsistent) and any other InvalidOperationException (ArraySortHelper.Sort).
inline void __gsharp_translate_sort_failure(GSharpErrorCode& code, const char*& message) noexcept
{
    if (__gsharp_sort_compare_depth <= 0) {
        return;
    }
    if (code == GSharpErrorCode::IndexOutOfRange) {
        code = GSharpErrorCode::Argument;
        message = "Unable to sort because the IComparer.Compare() method returns inconsistent results.";
    } else {
        code = GSharpErrorCode::InvalidOperation;
        message = "Failed to compare two elements in the array.";
    }
}

// A construct the CPU dialect cannot execute (e.g. Gpu.InlinePtx): fails with NotSupportedException
[[noreturn]] __GSHARP_COLD void __gsharp_unsupported(const char* message)
{
    __gsharp_record_failure(GSharpErrorCode::NotSupported, message, 0u);
    throw __gsharp_step_abort{};
}

// A failed check (e.g. an empty sequence or an index out of range in StackLinq): records the failure and ends the step.
// Declared in Types.hpp, so that GSharpArray<T> can report its argument errors.
[[noreturn]] __GSHARP_COLD void __gsharp_trap(GSharpErrorCode code, const char* message)
{
    __gsharp_translate_sort_failure(code, message);
    __gsharp_record_failure(code, message, 0u);
    throw __gsharp_step_abort{};
}

// The same, with the place of the check in the kernel's error site table
[[noreturn]] __GSHARP_COLD void __gsharp_trap_site(GSharpErrorCode code, uint32_t site_id)
{
    const char* message = nullptr;
    __gsharp_translate_sort_failure(code, message);
    __gsharp_record_failure(code, message, site_id);
    throw __gsharp_step_abort{};
}

[[noreturn]] inline void __gsharp_trap(GSharpErrorCode code, uint32_t site_id)
{
    __gsharp_trap_site(code, site_id);
}

// A failure after which the step cannot go on (a shared memory placement that fails, no arm of a switch expression
// matches, a null array read). Every failed check ends the step, so this is __gsharp_trap under the name that the CUDA
// prelude shares
[[noreturn]] inline void __gsharp_trap_abort(GSharpErrorCode code, const char* message)
{
    __gsharp_trap(code, message);
}

[[noreturn]] inline void __gsharp_trap_abort(GSharpErrorCode code, uint32_t site_id)
{
    __gsharp_trap_site(code, site_id);
}

// Disposes the resource of a using while a failure of the thread leaves the using (StandardTypes.hpp), as C# runs
// Dispose in a finally. [WHY] A destructor that runs while an exception unwinds must not throw, so a failure of the
// Dispose ends here: when the failure being unwound began inside the using (failureMark) and is the one its launch
// reports, the new failure replaces it, as the exception of a finally replaces the one in flight in C#. The unwinding
// then goes on.
template <typename T>
inline void __gsharp_dispose_unwinding(T& resource, uint32_t failureMark) noexcept
{
    bool outer = __gsharp_failure_replaces;
    __gsharp_failure_replaces = __gsharp_failures != failureMark && __gsharp_failure_reported;
    try {
        resource.__gsharp_dispose();
    } catch (const __gsharp_step_abort&) {
        // Recorded (or replaced) before the throw
    } catch (const std::bad_alloc&) {
        __gsharp_record_failure(GSharpErrorCode::InvalidOperation,
                                "G#: the native CPU path could not allocate memory for a kernel step", 0u);
    }
    __gsharp_failure_replaces = outer;
}

// GSharpArray::flags bit 0: a null optional kernel argument, which reads as an empty array (#838)
#define __gsharp_array_null_reads_empty 1
// The array that an array-typed C# expression reads (Length, an element, foreach): a null array ends the step with
// NullReferenceException, as C# throws (#706), and a null optional kernel argument reads as an empty array. The step
// ends (__gsharp_trap_abort) because the read would dereference nullptr next. An rvalue is returned by value, so that a
// range-based for over it does not keep a reference to a destroyed temporary. The place is the site of the kernel's
// error site table (uint32_t) or, in a device function, a message that names it (const char*)
template <typename A, typename S>
inline A __gsharp_array_ref(A&& array, S site)
{
    if (array.data == nullptr && (array.flags & __gsharp_array_null_reads_empty) == 0)
    {
        __gsharp_trap_abort(GSharpErrorCode::NullReference, site);
    }
    return static_cast<A&&>(array);
}

// The element array[index] as a location, with the check of __gsharp_array_ref. The array and the index are both
// evaluated before the check, as C# evaluates them before ldelem / stelem throws (#706)
template <typename A, typename I, typename S>
inline decltype(auto) __gsharp_array_at(A&& array, I index, S site)
{
    if (array.data == nullptr && (array.flags & __gsharp_array_null_reads_empty) == 0)
    {
        __gsharp_trap_abort(GSharpErrorCode::NullReference, site);
    }
    return array[index];
}

// The array of Gpu.NotNull(array): a null array ends the step with NullReferenceException, as the C# method throws.
// Unlike __gsharp_array_ref, a null optional kernel argument fails too: it reads as an empty array, but it is null
template <typename A, typename S>
inline A __gsharp_array_not_null(A&& array, S site)
{
    if (array.data == nullptr)
    {
        __gsharp_trap_abort(GSharpErrorCode::NullReference, site);
    }
    return static_cast<A&&>(array);
}

// A by-value struct argument of a device function whose parameter reaches non-nullable array fields (#1409): each field
// that a getter names is checked as __gsharp_array_ref checks an array, at the call, and the struct is passed on
template <typename T, typename S, typename... Get>
inline T __gsharp_struct_ref(T&& value, S site, Get... get)
{
    (static_cast<void>(__gsharp_array_ref(get(value), site)), ...);
    return static_cast<T&&>(value);
}

// Marks the comparison of a sort that the thread is running, while it lives (Sort.hpp)
struct __gsharp_sort_compare_scope
{
    inline __gsharp_sort_compare_scope() noexcept { __gsharp_sort_compare_depth++; }
    inline ~__gsharp_sort_compare_scope() { __gsharp_sort_compare_depth--; }
};
