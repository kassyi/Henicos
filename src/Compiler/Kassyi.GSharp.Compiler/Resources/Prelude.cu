#include <cooperative_groups.h>
#include <mma.h>
#include <math_constants.h>
namespace cg = cooperative_groups;

// NaN and infinity: a C# constant such as float.NaN has no C++ literal (ConstexprLiteral)
#define __gsharp_nan_f CUDART_NAN_F
#define __gsharp_inf_f CUDART_INF_F
#define __gsharp_nan_d CUDART_NAN
#define __gsharp_inf_d CUDART_INF

// --- Standard Types ---
#include "Prelude/StandardTypes.hpp"
#include "Prelude/IntegerSemantics.hpp"

// --- Runtime Error / Trap ---
enum class GSharpErrorCode : int {
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
    SwitchExpression = 10
};

// One device error record (GSharpDeviceErrorSlot of the runtime). code and epoch are one 8-byte word, which a failing
// thread claims with a single CAS, so a thread that loses it reads which launch holds the record.
struct __align__(8) GSharpDeviceErrorSlot {
    unsigned int code;
    unsigned int epoch;
    unsigned int site_id;
    unsigned int module_id;
    unsigned int thread_linear;
    unsigned int block_x;
    unsigned int block_yz;
};

// The records of the launches, indexed by epoch % __GSHARP_LAUNCH_RECORDS, in pinned host-mapped memory
// (LaunchErrorRecords), which the host reads after a launch
#define __GSHARP_LAUNCH_RECORDS 256u
struct GSharpLaunchErrorRing {
    GSharpDeviceErrorSlot records[__GSHARP_LAUNCH_RECORDS];
};

// The record of the launches captured into a CUDA graph (epoch 0, or a capture epoch), of the launches that find their
// own record held by another launch, and of every launch when the host has no ring
__device__ GSharpDeviceErrorSlot* __gsharp_error_slot = nullptr;
__device__ GSharpLaunchErrorRing* __gsharp_launch_errors = nullptr;
__device__ unsigned int __gsharp_error_module_id = 0u;
// The abort words of the launches of this module, indexed by epoch % __GSHARP_LAUNCH_ABORT_WORDS: the epoch of the last
// launch with that index whose thread failed. [WHY] In device memory of the module: the grid barrier reads the word at
// every arrival, and a read of host-mapped memory there made a barrier of 288 blocks take 150 us instead of 1.3 us.
// Only the module's own launches on its own device share the words, and two resident launches 4096 epochs apart, which
// would overwrite each other's word, do not fit on one device (at most 128 resident grids). The module loads them as
// zeros.
#define __GSHARP_LAUNCH_ABORT_WORDS 4096u
__device__ unsigned int __gsharp_launch_abort[__GSHARP_LAUNCH_ABORT_WORDS];
// The epochs of the launches captured into a CUDA graph whose kernel waits at a barrier that counts a fixed number of
// arrivals have this bit set (LaunchErrorRecords): the launcher took one from a range of its own and put a launch of
// __gsharp_clear_capture_abort in front of the kernel node, which clears the launch's word in __gsharp_capture_abort,
// so every replay of the graph starts with a clear word. [WHY] An array of its own: the clear of a replay never clears
// the word of a launch that is not captured. The word holds the full epoch, so a launch leaves only on its own failure;
// two live graphs whose capture epochs are a multiple of 4096 apart share a word, and a replay of one can clear the
// word of a failed replay of the other when both run at once. A launch that is not captured takes an epoch of the same
// range when the host has no free record for it; it records into the shared record like a captured launch, and its
// launcher clears its word on the stream before it the same way. The module loads the words as zeros.
#define __GSHARP_CAPTURE_EPOCH 0x80000000u
#define __GSHARP_CAPTURE_ABORT_WORDS 4096u
__device__ unsigned int __gsharp_capture_abort[__GSHARP_CAPTURE_ABORT_WORDS];

// Clears the abort word of a captured launch (one thread); the launcher captures it in front of the kernel. [WHY] A
// kernel node, not a memset node: in a graph a 4-byte memset node in front of a kernel added 7 to 15 us per replay, a
// one-thread kernel node about 0.8 us (RTX 3070 Ti, driver 621.14)
extern "C" __global__ void __gsharp_clear_capture_abort(unsigned int epoch) {
    __gsharp_capture_abort[epoch % __GSHARP_CAPTURE_ABORT_WORDS] = 0u;
}
// The epoch of the running launch, which every thread of a kernel stores from its hidden last parameter
// (__gsharp_epoch) before anything else. [WHY] A device function that records an error or waits at a barrier cannot see
// the kernel's parameters; every thread writes the same value and reads only after its own write, so no barrier is
// needed.
__shared__ unsigned int __gsharp_launch_epoch;

// The record that a failing thread of the running launch claims: its epoch's in the ring, or the shared record for an
// epoch with bit 31 set or epoch 0 (a captured launch, which the synchronization after cuGraphLaunch reads, or a launch
// without a free record)
__device__ __forceinline__ GSharpDeviceErrorSlot* __gsharp_launch_record(unsigned int epoch) {
    GSharpLaunchErrorRing* ring = __gsharp_launch_errors;
    return epoch != 0u && (epoch & __GSHARP_CAPTURE_EPOCH) == 0u && ring != nullptr
        ? &ring->records[epoch % __GSHARP_LAUNCH_RECORDS]
        : __gsharp_error_slot;
}

// The abort word of the running launch. None for epoch 0, which only a captured launch of a kernel that waits at no
// barrier counting a fixed number of arrivals has
__device__ __forceinline__ unsigned int* __gsharp_launch_abort_word(unsigned int epoch) {
    if (epoch == 0u) { return nullptr; }
    return (epoch & __GSHARP_CAPTURE_EPOCH) != 0u
        ? &__gsharp_capture_abort[epoch % __GSHARP_CAPTURE_ABORT_WORDS]
        : &__gsharp_launch_abort[epoch % __GSHARP_LAUNCH_ABORT_WORDS];
}

// Claims a record for the error of the running launch and writes it (first writer wins). A launch that records into
// the shared record (__gsharp_error_slot) itself, a launch captured into a CUDA graph or every launch when the host has
// no ring, keeps the error that is already there when another such launch (epoch 0 or bit 31) holds it, as the
// launches of one graph that fail in one replay do: the host throws the first error, and this launch only leaves.
// [WHY] No trap there: a trap ends the context, and the first error already makes the synchronization throw (the
// launchers of these launches throw any record, LaunchErrorRecords.ThrowIfLaunchError). When the holder is a launch
// that owns a ring epoch and overflowed into the shared record, no synchronization takes that record for this launch,
// so the thread traps as below. When another launch still holds the launch's own record in the ring
// (an unconsumed record of the launch __GSHARP_LAUNCH_RECORDS epochs before, which may still run), the error goes to
// the shared record, tagged with this epoch. When that one is held by another launch as well, nothing can record the
// error, and the thread traps rather than letting a launcher that reads only its own record return partial results
// with no exception (as without a record, __gsharp_trap_site). Returns the record that holds an error.
__device__ __forceinline__ GSharpDeviceErrorSlot* __gsharp_record_error(GSharpDeviceErrorSlot* record,
    unsigned int epoch, unsigned int code, unsigned int site_id) {
    const unsigned long long claim = (static_cast<unsigned long long>(epoch) << 32) | code;
    unsigned long long held = atomicCAS(reinterpret_cast<unsigned long long*>(&record->code), 0ull, claim);
    if (held != 0ull && static_cast<unsigned int>(held >> 32) != epoch) {
        GSharpDeviceErrorSlot* shared = __gsharp_error_slot;
        const unsigned int holder = static_cast<unsigned int>(held >> 32);
        if (shared == record && (holder == 0u || (holder & __GSHARP_CAPTURE_EPOCH) != 0u)) {
            return record;
        }
        if (shared != nullptr) {
            record = shared;
            held = atomicCAS(reinterpret_cast<unsigned long long*>(&record->code), 0ull, claim);
        }
        if (held != 0ull && static_cast<unsigned int>(held >> 32) != epoch) {
            asm volatile("trap;");
            __builtin_unreachable();
        }
    }
    if (held == 0ull) {
        // Orders the payload after the claim by a fence, not only by the dependency on the CAS result
        __threadfence();
        record->site_id = site_id;
        record->module_id = __gsharp_error_module_id;
        record->thread_linear = (threadIdx.x & 0x3FFu) | ((threadIdx.y & 0x3FFu) << 10)
            | ((threadIdx.z & 0x3FFu) << 20);
        record->block_x = blockIdx.x;
        record->block_yz = (blockIdx.y & 0xFFFFu) | ((blockIdx.z & 0xFFFFu) << 16);
        __threadfence_system();
        atomicExch(&record->code, code | 0x10000u);
        __threadfence_system();
    }
    return record;
}

// Tells the waiters of the running launch at a barrier that counts a fixed number of arrivals that one of its threads
// failed (__gsharp_wait_check, __gsharp_grid_sync). [WHY] A host synchronization never clears the word: it holds an
// epoch, so it tells only the launch with that epoch to leave, and a synchronization that consumes the record meanwhile
// cannot make a waiter miss it. The host clears the words of every module only when the counter of epochs wraps around
// (LaunchErrorRecords); the word of a captured launch is cleared by the node in front of it in the graph. The
// waiters run on the same device, so a fence of device scope orders the store.
__device__ __forceinline__ void __gsharp_signal_abort(unsigned int epoch) {
    unsigned int* word = __gsharp_launch_abort_word(epoch);
    if (word != nullptr) {
        *reinterpret_cast<volatile unsigned int*>(word) = epoch;
        __threadfence();
    }
}

__device__ __host__ constexpr unsigned int __gsharp_builtin_message_id(const char* message) {
    if (message == nullptr) { return 0x80000000u; }
    unsigned int hash = 2166136261u;
    for (const char* p = message; *p != 0; ++p) {
        hash ^= static_cast<unsigned char>(*p);
        hash *= 16777619u;
    }
    unsigned int low = hash & 0x7FFFFFFFu;
    return 0x80000000u | (low == 0u ? 1u : low);
}

// How a failing thread ends after it recorded its error. [WHY] From sm_70 on, __syncthreads (bar.sync) waits only for
// the threads that have not exited, and the warp intrinsics only for the non-exited threads of their mask, so a thread
// that leaves with exit blocks no barrier of the threads that go on: the kernel completes, the context stays usable
// and the host throws the recorded error at its next synchronization (DeviceErrorSlot). The barriers that count a fixed
// number of arrivals, the G# grid barrier (__gsharp_grid_sync) and an mbarrier, look at the abort word of their launch
// while they wait (__gsharp_wait_check). Before sm_70 a barrier counts warps, not threads, so the thread traps,
// which aborts the whole launch.
#if defined(__CUDA_ARCH__) && __CUDA_ARCH__ >= 700
#define __GSHARP_TRAP_EXITS 1
#endif

// Ends the thread after its launch failed: exit (sm_70+), or trap before sm_70
[[noreturn]] __device__ __forceinline__ void __gsharp_exit_thread() {
#ifdef __GSHARP_TRAP_EXITS
#if __CUDA_ARCH__ >= 800
    // No asynchronous copy of this thread may land in shared memory after it has exited
    asm volatile("cp.async.wait_all;" ::: "memory");
#endif
    asm volatile("exit;");
#else
    asm volatile("trap;");
#endif
    __builtin_unreachable();
}

[[noreturn]] __device__ __noinline__ void __gsharp_trap_site(GSharpErrorCode code, unsigned int site_id) {
    const unsigned int epoch = __gsharp_launch_epoch;
    GSharpDeviceErrorSlot* record = __gsharp_launch_record(epoch);
    // [WHY] Without a record (the host could not allocate it) nothing records the error, and a thread that exited would
    // let the launch complete with partial results and no exception, so the thread traps
    if (record == nullptr) {
        asm volatile("trap;");
        __builtin_unreachable();
    }
    {
        unsigned int active = __activemask();
        unsigned int lane;
        asm volatile("mov.u32 %0, %%laneid;" : "=r"(lane));
        unsigned int leader = __ffs(active) - 1u;
        if (lane == leader) {
            const unsigned int c = static_cast<unsigned int>(code) & 0xFFFFu;
            GSharpDeviceErrorSlot* held = __gsharp_record_error(record, epoch, c, site_id);
            __gsharp_signal_abort(epoch);
#ifndef __GSHARP_TRAP_EXITS
            // trap aborts every thread of the launch, so a thread that lost the record waits until the winner has
            // written it; a thread that exits stops only itself, and the winner finishes its record before exiting
            for (int spin = 0; spin < 100000; ++spin) {
                if ((*(volatile unsigned int*)&held->code & 0x10000u) != 0u) { break; }
                __nanosleep(64);
            }
#else
            (void)held;
#endif
        }
        __syncwarp(active);
    }
    __gsharp_exit_thread();
}

// The value of the elected rank of the grid barrier that tells the block its launch failed: no rank of a block of at
// most 1024 threads
#define __GSHARP_WAIT_LEAVE 0xFFFFFFFEu

// Called every 1024 polls by a thread that waits at a barrier counting a fixed number of arrivals (the G# grid barrier,
// an mbarrier); the arrival it waits for may belong to a thread that failed and exited. When the abort word of its
// launch holds the launch's epoch, a thread of this launch failed and recorded its error, so the waiter leaves: the
// arriving thread of the grid barrier writes __GSHARP_WAIT_LEAVE into its block's elected rank (leader) and returns,
// and the barrier lets the whole block exit (__gsharp_grid_barrier); an mbarrier waiter (leader is null) exits like the
// failing thread. The launch completes and the host throws the record. The arriving thread of the grid barrier first
// takes back its block's arrival (old, the counter before it; __gsharp_grid_unarrive). [WHY] The mbarrier waiter exits
// here, the grid barrier tests the elected rank only after this call returns, and the arrival is taken back here, not
// after the return: a branch to an exit in the inlined wait made ptxas allocate the cooperative megakernels of GpuMath
// far more registers (InverseSqrt 64 to 145), and a call that took the arrival back after the return
// ReciprocalNewtonRaphsonMega 48 to 70 (no longer a cooperative launch of its grid). Every launch of a kernel with such
// a barrier has a word, a launch captured into a CUDA graph too (a capture epoch), and a host synchronization never
// clears it, so a synchronization that consumes the record cannot keep a waiter waiting.
#if defined(__CUDA_ARCH__) && __CUDA_ARCH__ >= 700
__device__ __noinline__ void __gsharp_grid_unarrive(unsigned int old);
#endif
__device__ __noinline__ void __gsharp_wait_check(unsigned int* leader, unsigned int old) {
    const unsigned int epoch = __gsharp_launch_epoch;
    const unsigned int* word = __gsharp_launch_abort_word(epoch);
    if (word == nullptr || *reinterpret_cast<const volatile unsigned int*>(word) != epoch) {
        return;
    }
    if (leader != nullptr) {
#if defined(__CUDA_ARCH__) && __CUDA_ARCH__ >= 700
        __gsharp_grid_unarrive(old);
#endif
        *reinterpret_cast<volatile unsigned int*>(leader) = __GSHARP_WAIT_LEAVE;
        return;
    }
    (void)old;
    __gsharp_exit_thread();
}

[[noreturn]] __device__ __forceinline__ void __gsharp_trap(GSharpErrorCode code, const char* message) {
    __gsharp_trap_site(code, __gsharp_builtin_message_id(message));
}

[[noreturn]] __device__ __forceinline__ void __gsharp_trap(GSharpErrorCode code, unsigned int site_id) {
    __gsharp_trap_site(code, site_id);
}

// A failure after which the thread cannot go on (no arm of a switch expression matches). The thread ends there like
// after any __gsharp_trap, so the call never returns and a form that must yield a value needs none after it
// (ErrorChannel.hpp ends the step instead)
[[noreturn]] __device__ __forceinline__ void __gsharp_trap_abort(GSharpErrorCode code, const char* message) {
    __gsharp_trap_site(code, __gsharp_builtin_message_id(message));
    __builtin_unreachable();
}

[[noreturn]] __device__ __forceinline__ void __gsharp_trap_abort(GSharpErrorCode code, unsigned int site_id) {
    __gsharp_trap_site(code, site_id);
    __builtin_unreachable();
}

template <GSharpErrorCode ErrorCode>
__device__ inline const char* __gsharp_range_message() {
    if (ErrorCode == GSharpErrorCode::IndexOutOfRange) {
        return "Index was outside the bounds of the array.";
    }
    if (ErrorCode == GSharpErrorCode::Argument) {
        return "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.";
    }
    return "Index was out of range.";
}

// A C# array or span: data is nullptr for a null array (and the default span), and never nullptr for an array that
// exists, also an empty one (#706). flags uses the padding after length (16 bytes as before): bit 0
// (__gsharp_array_null_reads_empty) marks a null optional kernel argument, which reads as an empty array
template <typename T>
struct GSharpArray {
    // Not __restrict__: in C# two arrays (arguments, struct fields, a ref into one of them) may be one object, and the
    // launcher then gives them one device region (#552, #707)
    T* data;
    int length;
    int flags;

    __device__ inline GSharpArray() : data(nullptr), length(0), flags(0) {}
    __device__ inline GSharpArray(T* d, int l) : data(d), length(l), flags(0) {}
    // A stack array (T name[N]) viewed as a span or array argument; T may be const for a ReadOnlySpan. G# reports the
    // uses that would keep the view beyond the array's scope (GSH0091)
    template <int N>
    __device__ inline GSharpArray(T (&storage)[N]) : data(storage), length(N), flags(0) {}

    // The span members that C# calls on a span or an array (Span<T>.Slice, Span<T>.Fill, MemoryExtensions.AsSpan)
    template <GSharpErrorCode ErrorCode = GSharpErrorCode::ArgumentOutOfRange>
    __device__ inline GSharpArray<T> Slice(int start, const char* message = nullptr) const {
#ifdef GSHARP_CHECKED
        if (static_cast<unsigned int>(start) > static_cast<unsigned int>(length)) {
            __gsharp_trap(ErrorCode, message != nullptr ? message : __gsharp_range_message<ErrorCode>());
            return GSharpArray<T>();
        }
#endif
        return GSharpArray<T>(data + start, length - start);
    }
    template <GSharpErrorCode ErrorCode = GSharpErrorCode::ArgumentOutOfRange>
    __device__ inline GSharpArray<T> Slice(int start, int count, const char* message = nullptr) const {
#ifdef GSHARP_CHECKED
        if (static_cast<unsigned int>(start) > static_cast<unsigned int>(length) ||
            static_cast<unsigned int>(count) > static_cast<unsigned int>(length - start)) {
            __gsharp_trap(ErrorCode, message != nullptr ? message : __gsharp_range_message<ErrorCode>());
            return GSharpArray<T>();
        }
#endif
        return GSharpArray<T>(data + start, count);
    }
    __device__ inline void Fill(const T& value) const { for (int i = 0; i < length; i++) { data[i] = value; } }
    __device__ inline void Clear() const { for (int i = 0; i < length; i++) { data[i] = T{}; } }
    // Ported from dotnet/runtime src/libraries/System.Private.CoreLib/src/System/MemoryExtensions.cs
    // (MIT License, commit e750854de9ad6a2ced72feb9b8008e506f04608f): in-place two-pointer swap for Span<T>.Reverse.
    __device__ inline void Reverse() const {
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
    __device__ inline bool TryCopyTo(GSharpArray<U> destination) const {
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
    __device__ inline void CopyTo(GSharpArray<U> destination) const {
        if (!TryCopyTo(destination)) {
            __gsharp_trap(GSharpErrorCode::Argument, "Destination is too short.");
        }
    }
    __device__ inline GSharpArray<T> AsSpan() const { return *this; }
    template <GSharpErrorCode ErrorCode = GSharpErrorCode::ArgumentOutOfRange>
    __device__ inline GSharpArray<T> AsSpan(int start, const char* message = nullptr) const { return Slice<ErrorCode>(start, message); }
    template <GSharpErrorCode ErrorCode = GSharpErrorCode::ArgumentOutOfRange>
    __device__ inline GSharpArray<T> AsSpan(int start, int count, const char* message = nullptr) const { return Slice<ErrorCode>(start, count, message); }
    __device__ inline bool IsEmpty() const { return length <= 0; }
    
    // Allows implicit conversion from GSharpArray<U> to GSharpArray<T> if U* is convertible to T* (e.g. T to const T)
    template <typename U>
    __device__ inline GSharpArray(const GSharpArray<U>& other) : data(other.data), length(other.length), flags(other.flags) {}

    __device__ inline T* begin() { return data; }
    __device__ inline T* end() { return data + length; }
    __device__ inline const T* begin() const { return data; }
    __device__ inline const T* end() const { return data + length; }
    __device__ inline int size() const { return length; }
    __device__ inline T& operator[](int index) { return data[index]; }
    __device__ inline const T& operator[](int index) const { return data[index]; }
    __device__ inline operator T*() { return data; }
    __device__ inline operator const T*() const { return data; }
};

// A stack array (T name[N]) viewed as a span, for a receiver or a template argument that deduces GSharpArray<T>
template <typename T, int N>
__device__ inline GSharpArray<T> __gsharp_stackalloc_span(T (&storage)[N]) { return GSharpArray<T>(storage, N); }
// The storage of a stackalloc outside a declaration, declared before the statement: zeroed where the stackalloc is
// evaluated (every evaluation, as C# allocates a new block each time), then viewed as a span
template <typename T, int N>
__device__ inline GSharpArray<T> __gsharp_stackalloc_zero(T (&storage)[N]) {
    for (int i = 0; i < N; i++) { storage[i] = T{}; }
    return GSharpArray<T>(storage, N);
}



// --- Arithmetic & SIMD ---
__device__ __forceinline__ unsigned int __gsharp_add_cc(unsigned int a, unsigned int b, unsigned long long* carry) {
    unsigned int res;
    unsigned int c_in = (unsigned int)*carry;
    unsigned int c_out;
    asm volatile(
        "add.cc.u32 %0, %2, -1;\n\t"
        "addc.cc.u32 %0, %3, %4;\n\t"
        "addc.u32 %1, 0, 0;"
        : "=r"(res), "=r"(c_out)
        : "r"(c_in), "r"(a), "r"(b)
    );
    *carry = c_out;
    return res;
}

__device__ __forceinline__ unsigned int __gsharp_sub_cc(unsigned int a, unsigned int b, long long* borrow) {
    unsigned int res;
    [[maybe_unused]] unsigned int dummy;
    unsigned int b_in = (unsigned int)*borrow;
    unsigned int b_out;
    asm volatile(
        "sub.cc.u32 %1, 0, %3;\n\t"
        "subc.cc.u32 %0, %4, %5;\n\t"
        "subc.u32 %2, 0, 0;"
        : "=r"(res), "=r"(dummy), "=r"(b_out)
        : "r"(b_in), "r"(a), "r"(b)
    );
    (void)dummy;
    *borrow = b_out & 1;
    return res;
}

__device__ __forceinline__ unsigned long long __gsharp_mul_wide(unsigned int a, unsigned int b) {
    unsigned long long res;
    asm volatile("mul.wide.u32 %0, %1, %2;" : "=l"(res) : "r"(a), "r"(b));
    return res;
}

__device__ __forceinline__ unsigned long long __gsharp_mad_wide(unsigned int a, unsigned int b, unsigned long long c) {
    unsigned long long res;
    asm volatile("mad.wide.u32 %0, %1, %2, %3;" : "=l"(res) : "r"(a), "r"(b), "l"(c));
    return res;
}

__device__ __forceinline__ long long __gsharp_mad_wide_s(int a, int b, long long c) {
    long long res;
    asm volatile("mad.wide.s32 %0, %1, %2, %3;" : "=l"(res) : "r"(a), "r"(b), "l"(c));
    return res;
}

// (a * b) + c + carry-in; *carry holds 0 or 1 on entry and the carry-out of the 64-bit sum on exit.
// The product is at most 2^64 - 2^33 + 1, so the two additions cannot both wrap.
__device__ __forceinline__ unsigned long long __gsharp_madc_cc(unsigned int a, unsigned int b, unsigned long long c, unsigned long long* carry) {
    unsigned long long product = __gsharp_mad_wide(a, b, 0ull);
    unsigned long long partial = product + c;
    unsigned long long carryOut = partial < product ? 1ull : 0ull;
    unsigned long long res = partial + (*carry & 1ull);
    carryOut |= res < partial ? 1ull : 0ull;
    *carry = carryOut;
    return res;
}

// mbarrier (sm_80+). The object lives in shared memory; the generic pointer is converted to a shared address.
__device__ __forceinline__ void __gsharp_mbarrier_init(unsigned long long* barrier, unsigned int expectedCount) {
    unsigned int addr = (unsigned int)__cvta_generic_to_shared(barrier);
    asm volatile("mbarrier.init.shared.b64 [%0], %1;" :: "r"(addr), "r"(expectedCount) : "memory");
}

__device__ __forceinline__ void __gsharp_mbarrier_arrive(unsigned long long* barrier) {
    unsigned int addr = (unsigned int)__cvta_generic_to_shared(barrier);
    [[maybe_unused]] unsigned long long state;
    asm volatile("mbarrier.arrive.shared.b64 %0, [%1];" : "=l"(state) : "r"(addr) : "memory");
    (void)state;
}

// Spins on mbarrier.test_wait.parity until the phase with the given parity has completed. An mbarrier waits for a fixed
// number of arrivals, which a thread that failed and exited never makes, so every 1024 incomplete tests the waiter looks
// at the abort word of its launch (__gsharp_wait_check) and exits when a thread of the launch failed
__device__ __forceinline__ void __gsharp_mbarrier_wait(unsigned long long* barrier, unsigned int phase) {
    unsigned int addr = (unsigned int)__cvta_generic_to_shared(barrier);
    unsigned int done = 0;
    unsigned int tests = 0;
    while (!done) {
        asm volatile(
            "{\n\t"
            ".reg .pred p;\n\t"
            "mbarrier.test_wait.parity.shared.b64 p, [%1], %2;\n\t"
            "selp.u32 %0, 1, 0, p;\n\t"
            "}"
            : "=r"(done) : "r"(addr), "r"(phase) : "memory");
        if (!done && (++tests & 1023u) == 0u) {
            __gsharp_wait_check(nullptr, 0u);
        }
    }
}

// cp.async (sm_80+; falls back to synchronous copy on earlier architectures).
template <typename T>
__device__ __forceinline__ void __gsharp_cp_async(T* dst, const T* src) {
    static_assert(sizeof(T) == 4 || sizeof(T) == 8 || sizeof(T) == 16, "cp.async copies 4, 8 or 16 bytes");
#if __CUDA_ARCH__ >= 800
    unsigned int addr = (unsigned int)__cvta_generic_to_shared(dst);
    asm volatile("cp.async.ca.shared.global [%0], [%1], %2;\n"
                 :: "r"(addr), "l"(src), "n"((int)sizeof(T)) : "memory");
#else
    *dst = *src;
#endif
}

__device__ __forceinline__ void __gsharp_cp_async_commit_group() {
#if __CUDA_ARCH__ >= 800
    asm volatile("cp.async.commit_group;\n" ::: "memory");
#endif
}

__device__ __forceinline__ void __gsharp_cp_async_wait_group(int n) {
#if __CUDA_ARCH__ >= 800
    switch (n) {
        case 0: asm volatile("cp.async.wait_group 0;\n" ::: "memory"); break;
        case 1: asm volatile("cp.async.wait_group 1;\n" ::: "memory"); break;
        case 2: asm volatile("cp.async.wait_group 2;\n" ::: "memory"); break;
        case 3: asm volatile("cp.async.wait_group 3;\n" ::: "memory"); break;
        case 4: asm volatile("cp.async.wait_group 4;\n" ::: "memory"); break;
        case 5: asm volatile("cp.async.wait_group 5;\n" ::: "memory"); break;
        case 6: asm volatile("cp.async.wait_group 6;\n" ::: "memory"); break;
        default: asm volatile("cp.async.wait_group 7;\n" ::: "memory"); break;
    }
#else
    (void)n;
#endif
}

__device__ __forceinline__ void __gsharp_cp_async_wait_all() {
#if __CUDA_ARCH__ >= 800
    asm volatile("cp.async.wait_all;\n" ::: "memory");
#endif
}

// --- Tensor Core (WMMA) ---
template <int M, int N, int K> struct __gsharp_wmma_shape {};
#if !defined(__CUDA_ARCH__) || __CUDA_ARCH__ >= 700
using __gsharp_wmma_matrix_a = nvcuda::wmma::matrix_a;
using __gsharp_wmma_matrix_b = nvcuda::wmma::matrix_b;
using __gsharp_wmma_accumulator = nvcuda::wmma::accumulator;
using __gsharp_wmma_row_major = nvcuda::wmma::row_major;
using __gsharp_wmma_col_major = nvcuda::wmma::col_major;
template <class Use, int M, int N, int K, class T, class Layout = void>
using __gsharp_wmma_fragment = nvcuda::wmma::fragment<Use, M, N, K, T, Layout>;

template <class Shape, class T, class Layout = void, class Frag>
__device__ __forceinline__ void __gsharp_wmma_fill(Frag* frag, T value) {
    nvcuda::wmma::fill_fragment(*frag, value);
}

template <class Shape, class T, class Layout, class Frag>
__device__ __forceinline__ void __gsharp_wmma_load(Frag* frag, const T* src, unsigned int ldm) {
    nvcuda::wmma::load_matrix_sync(*frag, src, ldm);
}

template <class Shape, class T, class Frag>
__device__ __forceinline__ void __gsharp_wmma_load(Frag* frag, const T* src, unsigned int ldm, int layout) {
    nvcuda::wmma::load_matrix_sync(*frag, src, ldm, layout == 0 ? nvcuda::wmma::mem_row_major : nvcuda::wmma::mem_col_major);
}

template <class Shape, class T, class Frag>
__device__ __forceinline__ void __gsharp_wmma_store(T* dst, const Frag* frag, unsigned int ldm, int layout) {
    nvcuda::wmma::store_matrix_sync(dst, *frag, ldm, layout == 0 ? nvcuda::wmma::mem_row_major : nvcuda::wmma::mem_col_major);
}

template <class Shape, class TIn, class LayoutA, class LayoutB, class TAcc, class FragD, class FragA, class FragB, class FragC>
__device__ __forceinline__ void __gsharp_wmma_mma(FragD* d, const FragA* a, const FragB* b, const FragC* c) {
    nvcuda::wmma::mma_sync(*d, *a, *b, *c);
}
#else
struct __gsharp_wmma_matrix_a {};
struct __gsharp_wmma_matrix_b {};
struct __gsharp_wmma_accumulator {};
struct __gsharp_wmma_row_major {};
struct __gsharp_wmma_col_major {};
template <class Use, int M, int N, int K, class T, class Layout = void>
struct __gsharp_wmma_fragment {};
template <class Shape, class T, class Layout = void, class Frag>
__device__ __forceinline__ void __gsharp_wmma_fill(Frag*, T) {
    static_assert(sizeof(Frag) == 0, "WMMA requires compute capability 7.0 or higher.");
}
template <class Shape, class T, class Layout, class Frag>
__device__ __forceinline__ void __gsharp_wmma_load(Frag*, const T*, unsigned int) {
    static_assert(sizeof(Frag) == 0, "WMMA requires compute capability 7.0 or higher.");
}
template <class Shape, class T, class Frag>
__device__ __forceinline__ void __gsharp_wmma_load(Frag*, const T*, unsigned int, int) {
    static_assert(sizeof(Frag) == 0, "WMMA requires compute capability 7.0 or higher.");
}
template <class Shape, class T, class Frag>
__device__ __forceinline__ void __gsharp_wmma_store(T*, const Frag*, unsigned int, int) {
    static_assert(sizeof(Frag) == 0, "WMMA requires compute capability 7.0 or higher.");
}
template <class Shape, class TIn, class LayoutA, class LayoutB, class TAcc, class FragD, class FragA, class FragB, class FragC>
__device__ __forceinline__ void __gsharp_wmma_mma(FragD*, const FragA*, const FragB*, const FragC*) {
    static_assert(sizeof(FragD) == 0, "WMMA requires compute capability 7.0 or higher.");
}
#endif

__device__ __forceinline__ unsigned int __gsharp_vabsdiff4(unsigned int a, unsigned int b, unsigned int c) {
    unsigned int res;
    asm("vabsdiff4.u32.u32.u32.add %0, %1, %2, %3;" : "=r"(res) : "r"(a), "r"(b), "r"(c));
    return res;
}

// --- Math & Range ---
__forceinline__ __device__ int __gsharp_clamp(int value, int min_val, int max_val) { return value < min_val ? min_val : (value > max_val ? max_val : value); }
__forceinline__ __device__ unsigned int __gsharp_clamp(unsigned int value, unsigned int min_val, unsigned int max_val) { return value < min_val ? min_val : (value > max_val ? max_val : value); }
__forceinline__ __device__ float __gsharp_clamp(float value, float min_val, float max_val) { return value < min_val ? min_val : (value > max_val ? max_val : value); }
__forceinline__ __device__ double __gsharp_clamp(double value, double min_val, double max_val) { return value < min_val ? min_val : (value > max_val ? max_val : value); }
// Math.Max / Math.Min (IEEE 754:2019 maximum / minimum, as .NET): NaN if either input is NaN, +0 > -0.
// PTX max.NaN.f32 / min.NaN.f32 are exactly that (sm_80+); max / min .f32 and .f64 already order +0 > -0 (PTX ISA, max).
// a + b is NaN when either is NaN. The asm is not volatile, so common subexpressions still fold.
// One name per type, as fmaxf / fmax: a call passes its C# arguments unconverted (Math.Max(double, float)), which
// an overload set would find ambiguous.
__device__ __forceinline__ float __gsharp_max_f32(float a, float b) {
#if __CUDA_ARCH__ >= 800
    float r;
    asm("max.NaN.f32 %0, %1, %2;" : "=f"(r) : "f"(a), "f"(b));
    return r;
#else
    return (isnan(a) || isnan(b)) ? a + b : fmaxf(a, b);
#endif
}
__device__ __forceinline__ float __gsharp_min_f32(float a, float b) {
#if __CUDA_ARCH__ >= 800
    float r;
    asm("min.NaN.f32 %0, %1, %2;" : "=f"(r) : "f"(a), "f"(b));
    return r;
#else
    return (isnan(a) || isnan(b)) ? a + b : fminf(a, b);
#endif
}
__device__ __forceinline__ double __gsharp_max_f64(double a, double b) { return (isnan(a) || isnan(b)) ? a + b : fmax(a, b); }
__device__ __forceinline__ double __gsharp_min_f64(double a, double b) { return (isnan(a) || isnan(b)) ? a + b : fmin(a, b); }
// float / double MaxNumber / MinNumber (IEEE 754:2019 maximumNumber / minimumNumber): NaN only if both are NaN, +0 > -0,
// which is PTX max / min .f32 / .f64, the instructions fmaxf / fmax compile to.
__device__ __forceinline__ float __gsharp_max_number_f32(float a, float b) { return fmaxf(a, b); }
__device__ __forceinline__ float __gsharp_min_number_f32(float a, float b) { return fminf(a, b); }
__device__ __forceinline__ double __gsharp_max_number_f64(double a, double b) { return fmax(a, b); }
__device__ __forceinline__ double __gsharp_min_number_f64(double a, double b) { return fmin(a, b); }

// --- Gpu.GridStrideLoop ---
// The first index and the step over the linear rank of the whole launch, in the order of cooperative_groups
// grid::thread_rank() / num_threads() (x fastest, blocks in the same order). Both saturate at 2^31: no int index
// reaches it, so index + step never wraps around in 32 bits. The block rank is 64-bit before any product: it stays
// below 2^63 (65535 * 65535 * (2^31 - 1)), as in num_blocks().
__device__ __forceinline__ unsigned int __gsharp_grid_stride_start() {
    unsigned long long block = ((unsigned long long)blockIdx.z * gridDim.y + blockIdx.y) * gridDim.x + blockIdx.x;
    if (block >= 0x80000000ull) { return 0x80000000u; }
    unsigned long long rank = block * (blockDim.x * blockDim.y * blockDim.z)
        + (threadIdx.z * blockDim.y + threadIdx.y) * blockDim.x + threadIdx.x;
    return rank >= 0x80000000ull ? 0x80000000u : (unsigned int)rank;
}
__device__ __forceinline__ unsigned int __gsharp_grid_stride_step() {
    unsigned long long blocks = (unsigned long long)gridDim.x * (gridDim.y * gridDim.z);
    if (blocks >= 0x80000000ull) { return 0x80000000u; }
    unsigned long long threads = blocks * (blockDim.x * blockDim.y * blockDim.z);
    return threads >= 0x80000000ull ? 0x80000000u : (unsigned int)threads;
}
// The count of Gpu.GridStrideLoop, evaluated once; a negative count runs no iteration, as i < n does in C#
__device__ __forceinline__ unsigned int __gsharp_grid_stride_count(int n) { return n < 0 ? 0u : (unsigned int)n; }

// --- Gpu.LaneId / Gpu.WarpId ---
// A warp holds 32 consecutive linear thread indices of the block (x fastest), so the lane is the index modulo 32 (the
// %laneid register) and the warp its quotient. %warpid is not used: it names a hardware slot that may change while the
// thread runs.
__device__ __forceinline__ int __gsharp_lane_id() {
    return (int)(((threadIdx.z * blockDim.y + threadIdx.y) * blockDim.x + threadIdx.x) & 31u);
}
__device__ __forceinline__ int __gsharp_warp_id() {
    return (int)(((threadIdx.z * blockDim.y + threadIdx.y) * blockDim.x + threadIdx.x) >> 5);
}

// --- Bit Manipulation ---
__device__ __forceinline__ unsigned int __gsharp_rotl32(unsigned int value, int offset) {
    offset &= 31;
    return __funnelshift_l(value, value, offset);
}

__device__ __forceinline__ unsigned int __gsharp_rotr32(unsigned int value, int offset) {
    offset &= 31;
    return __funnelshift_r(value, value, offset);
}

__device__ __forceinline__ unsigned long long __gsharp_rotl64(unsigned long long value, int offset) {
    offset &= 63;
    return offset ? (value << offset) | (value >> (64 - offset)) : value;
}

__device__ __forceinline__ unsigned long long __gsharp_rotr64(unsigned long long value, int offset) {
    offset &= 63;
    return offset ? (value >> offset) | (value << (64 - offset)) : value;
}

__device__ __forceinline__ int __gsharp_tzc_32(unsigned int value) {
    return value ? __ffs(value) - 1 : 32;
}

__device__ __forceinline__ int __gsharp_tzc_64(unsigned long long value) {
    return value ? __ffsll(value) - 1 : 64;
}

__device__ __forceinline__ int __gsharp_log2_32(unsigned int value) {
    return value ? 31 - __clz(value) : 0;
}

__device__ __forceinline__ int __gsharp_log2_64(unsigned long long value) {
    return value ? 63 - __clzll(value) : 0;
}

__device__ __forceinline__ int __gsharp_lzc_32(unsigned int value) {
    return value ? __clz(value) : 32;
}

__device__ __forceinline__ int __gsharp_lzc_64(unsigned long long value) {
    return value ? __clzll(value) : 64;
}

__device__ __forceinline__ unsigned int __gsharp_bfe(unsigned int a, int pos, int len) {
    unsigned int res;
    asm("bfe.u32 %0, %1, %2, %3;" : "=r"(res) : "r"(a), "r"(pos), "r"(len));
    return res;
}

__device__ __forceinline__ unsigned long long __gsharp_bfe64(unsigned long long a, int pos, int len) {
    unsigned long long res;
    asm("bfe.u64 %0, %1, %2, %3;" : "=l"(res) : "l"(a), "r"(pos), "r"(len));
    return res;
}

__device__ __forceinline__ unsigned int __gsharp_bfi(unsigned int a, unsigned int b, int pos, int len) {
    unsigned int res;
    asm("bfi.b32 %0, %1, %2, %3, %4;" : "=r"(res) : "r"(a), "r"(b), "r"(pos), "r"(len));
    return res;
}

__device__ __forceinline__ unsigned long long __gsharp_bfi64(unsigned long long a, unsigned long long b, int pos, int len) {
    unsigned long long res;
    asm("bfi.b64 %0, %1, %2, %3, %4;" : "=l"(res) : "l"(a), "l"(b), "r"(pos), "r"(len));
    return res;
}

#define __gsharp_lop3(a, b, c, immLut) ([&]() -> unsigned int { \
    unsigned int __res; \
    asm("lop3.b32 %0, %1, %2, %3, %4;" : "=r"(__res) : "r"(a), "r"(b), "r"(c), "n"(immLut)); \
    return __res; \
}())

// --- Atomics ---
__forceinline__ __device__ int __gsharp_atomicCAS(int* address, int val, int compare) { return atomicCAS(address, compare, val); }
__forceinline__ __device__ unsigned int __gsharp_atomicCAS(unsigned int* address, unsigned int val, unsigned int compare) { return atomicCAS(address, compare, val); }
__forceinline__ __device__ unsigned long long __gsharp_atomicCAS(unsigned long long* address, unsigned long long val, unsigned long long compare) { return atomicCAS(address, compare, val); }
__forceinline__ __device__ long long __gsharp_atomicCAS(long long* address, long long val, long long compare) { return (long long)atomicCAS((unsigned long long*)address, (unsigned long long)compare, (unsigned long long)val); }

// Interlocked.Add returns the new value and wraps (unchecked), and the long overloads return long. C++ leaves signed
// overflow undefined and the atomics have no long long overloads, so the sum is computed in the unsigned type and the
// result of the unsigned atomic is converted back (modulo 2^n, as in IntegerSemantics.hpp).
__forceinline__ __device__ int __gsharp_atomicAdd(int* address, int val) { return (int)((unsigned int)atomicAdd(address, val) + (unsigned int)val); }
__forceinline__ __device__ unsigned int __gsharp_atomicAdd(unsigned int* address, unsigned int val) { return atomicAdd(address, val) + val; }
__forceinline__ __device__ unsigned long long __gsharp_atomicAdd(unsigned long long* address, unsigned long long val) { return atomicAdd(address, val) + val; }
__forceinline__ __device__ long long __gsharp_atomicAdd(long long* address, long long val) { return (long long)(atomicAdd((unsigned long long*)address, (unsigned long long)val) + (unsigned long long)val); }

__forceinline__ __device__ long long __gsharp_atomicExch(long long* address, long long val) { return (long long)atomicExch((unsigned long long*)address, (unsigned long long)val); }

// --- Runtime Error / Trap ---
// GSharpErrorCode and __gsharp_trap are defined above GSharpArray<T>.

// The site ID of a null array read: a site of the kernel's error site table, or the builtin ID of a message
__device__ __forceinline__ unsigned int __gsharp_null_read_site(const char* message) { return __gsharp_builtin_message_id(message); }
__device__ __forceinline__ unsigned int __gsharp_null_read_site(unsigned int site_id) { return site_id; }

// The failure of a null array read, written inline: the record of __gsharp_trap_site without the warp leader election,
// then the end of __gsharp_trap_site: the thread exits (sm_70+), or traps before sm_70 and without a record.
// [WHY] A call to __gsharp_trap_site (__noinline__) makes ptxas keep the caller's live registers across the call: the
// cooperative ReciprocalNewtonRaphsonMegaKernel of GpuMath went from 60 registers to 255 with a 336-byte stack and no
// longer launched with 1024 threads per block (CUDA_ERROR_COOPERATIVE_LAUNCH_TOO_LARGE). Written inline it takes 64
[[noreturn]] __device__ __forceinline__ void __gsharp_null_read_trap(unsigned int site_id) {
    const unsigned int epoch = __gsharp_launch_epoch;
    GSharpDeviceErrorSlot* record = __gsharp_launch_record(epoch);
    if (record == nullptr) {
        // Nothing would record the error, so the thread must not exit with no exception (see __gsharp_trap_site)
        asm volatile("trap;");
        __builtin_unreachable();
    }
    constexpr unsigned int code = static_cast<unsigned int>(GSharpErrorCode::NullReference);
    GSharpDeviceErrorSlot* held = __gsharp_record_error(record, epoch, code, site_id);
    __gsharp_signal_abort(epoch);
#ifdef __GSHARP_TRAP_EXITS
    // A thread that exits stops only itself, so a thread that lost the record does not wait for the winner's record
    (void)held;
#else
    // trap stops the whole kernel, so a thread that lost the record waits until the winner has written it; the
    // writer sets the bit a few stores after its claim, so the wait ends. A bounded wait (a counter) costs the
    // cooperative InverseSqrtNewtonRaphsonMegaKernel 38 more registers and a stack
    while ((*(volatile unsigned int*)&held->code & 0x10000u) == 0u) { }
#endif
    __gsharp_exit_thread();
}

// GSharpArray::flags bit 0: a null optional kernel argument, which reads as an empty array (#838)
#define __gsharp_array_null_reads_empty 1
// The array that an array-typed C# expression reads (Length, an element, foreach): a null array fails as C# throws
// NullReferenceException (#706), a null optional kernel argument reads as an empty array. Every build tests it, as
// C# defines the result and the GPU does not (#1409); the compiler leaves the check out where the array cannot be null.
// The place is the site of the kernel's error site table (unsigned int) or, in a device function, a message that names
// it (const char*). An rvalue is returned by value, so that a range-based for over it does not keep a reference to a
// destroyed temporary
template <typename A, typename S>
__device__ __forceinline__ A __gsharp_array_ref(A&& array, S site)
{
    if (array.data == nullptr && (array.flags & __gsharp_array_null_reads_empty) == 0)
    {
        __gsharp_null_read_trap(__gsharp_null_read_site(site));
    }
    return static_cast<A&&>(array);
}
// The element array[index] as a location, with the check of __gsharp_array_ref. The array and the index are both
// evaluated before the check, as C# evaluates them before ldelem / stelem throws (#706)
template <typename A, typename I, typename S>
__device__ __forceinline__ decltype(auto) __gsharp_array_at(A&& array, I index, S site)
{
    if (array.data == nullptr && (array.flags & __gsharp_array_null_reads_empty) == 0)
    {
        __gsharp_null_read_trap(__gsharp_null_read_site(site));
    }
    return array[index];
}
// The array of Gpu.NotNull(array): a null array fails as the C# method throws NullReferenceException. Unlike
// __gsharp_array_ref, a null optional kernel argument fails too: it reads as an empty array, but it is null
template <typename A, typename S>
__device__ __forceinline__ A __gsharp_array_not_null(A&& array, S site)
{
    if (array.data == nullptr)
    {
        __gsharp_null_read_trap(__gsharp_null_read_site(site));
    }
    return static_cast<A&&>(array);
}
// A by-value struct argument of a device function whose parameter reaches non-nullable array fields (#1409): each field
// that a getter names is checked as __gsharp_array_ref checks an array, at the call, and the struct is passed on
template <typename T, typename S, typename... Get>
__device__ __forceinline__ T __gsharp_struct_ref(T&& value, S site, Get... get)
{
    (static_cast<void>(__gsharp_array_ref(get(value), site)), ...);
    return static_cast<T&&>(value);
}
// [WHY] A trap stops the kernel, so a trap in the comparison of a sort needs no translation (ErrorChannel.hpp does it on the CPU)
struct __gsharp_sort_compare_scope { };

// --- AsParallel() aggregates (ParallelAggregatePlanner, Block strategy) ---
// The linear rank and the number of the threads of the block (all three dimensions)
__device__ __forceinline__ unsigned int __gsharp_block_rank() { return threadIdx.x + blockDim.x * (threadIdx.y + blockDim.y * threadIdx.z); }
__device__ __forceinline__ unsigned int __gsharp_block_size() { return blockDim.x * blockDim.y * blockDim.z; }

// [WHY] The signed sums are added as unsigned (the bits of a wrapping sum, as StackLinq's Sum wraps), so that combining
// partial sums is not a signed overflow, which C++ leaves undefined
struct __gsharp_plus
{
    template <typename T>
    __device__ __forceinline__ T operator()(T a, T b) const { return a + b; }
    __device__ __forceinline__ int operator()(int a, int b) const { return (int)((unsigned int)a + (unsigned int)b); }
    __device__ __forceinline__ long long operator()(long long a, long long b) const { return (long long)((unsigned long long)a + (unsigned long long)b); }
};

// [WHY] The last warp of a block whose size is not a multiple of 32 is partial: the shuffle mask names only the lanes that
// exist, and a value shuffled from past the last valid lane is not combined (CUB WarpReduceShfl::ReduceStep).
template <typename T, typename Op>
__device__ __forceinline__ T __gsharp_warp_reduce(T value, unsigned int lane, unsigned int lanes, unsigned int valid, Op op)
{
    const unsigned int mask = lanes == 32u ? 0xffffffffu : ((1u << lanes) - 1u);
    #pragma unroll
    for (unsigned int offset = 16u; offset > 0u; offset >>= 1)
    {
        const T other = __shfl_down_sync(mask, value, offset);
        if (lane + offset < valid) { value = op(value, other); }
    }
    return value;
}

// Every thread of the block receives the reduction of the values of all the threads of the block, combined in a fixed
// order. The slots are reused by the next call: it writes them after the second barrier of this one.
template <typename T, typename Op>
__device__ T __gsharp_block_reduce(T value, Op op)
{
    __shared__ T slots[32];
    __shared__ T total;
    const unsigned int size = __gsharp_block_size();
    const unsigned int rank = __gsharp_block_rank();
    const unsigned int lane = rank & 31u;
    const unsigned int warp = rank >> 5;
    const unsigned int warps = (size + 31u) >> 5;
    const unsigned int lanes = min(32u, size - (warp << 5));
    value = __gsharp_warp_reduce(value, lane, lanes, lanes, op);
    if (lane == 0u) { slots[warp] = value; }
    __syncthreads();
    if (warp == 0u)
    {
        T v = __gsharp_warp_reduce(lane < warps ? slots[lane] : value, lane, lanes, warps, op);
        if (lane == 0u) { total = v; }
    }
    __syncthreads();
    return total;
}

// --- Grid barrier (Gpu.SyncGrid, cooperative kernels) ---
// [WHY] cooperative_groups' grid sync lets thread (0,0,0) of each block arrive for its block, so a block whose thread
// (0,0,0) failed and exited never arrives and the grid waits forever. Here the threads of the block that have not
// exited elect the arriving thread (the lowest linear index among them); __syncthreads waits only for those threads
// (sm_70+). A block whose threads all exited never arrives either, so the arriving thread looks at the abort word of its
// launch while it waits (__gsharp_wait_check), and once more when it reaches the barrier. When it sees that a thread of
// the launch failed, it marks the block (its elected rank becomes __GSHARP_WAIT_LEAVE) and joins the last
// __syncthreads with the other threads, which orders the mark before their read, and every thread of the block exits.
// After a failure the other threads run to their next grid barrier, mbarrier wait or the end of the kernel.
// Gpu.SyncGrid(GridSyncMode) picks where the arriving thread reads the word when it reaches the barrier:
// - Deterministic (__gsharp_grid_sync, also the syncs of a Gpu.SyncGrid(body)): before it arrives. A block that reaches
//   the barrier after the failure does not arrive, so the barrier does not complete and every block that waits at it
//   leaves too. (A block that arrived just before the abort word was set can complete the barrier for the waiting
//   blocks.) The release add waits for the load: 1.38 us per barrier against 1.05 us for Fast (288 blocks of 256
//   threads, RTX 3070 Ti).
// - Fast (__gsharp_grid_sync_fast): after it arrives, so the load does not delay the add. The arrival of a block of the
//   failing launch can complete the barrier, and the other blocks then run the code after it up to their next grid
//   barrier, mbarrier wait or the end of the kernel, where they leave.
// Both modes leave no block waiting: a waiter leaves within 1024 polls of the failure, and a block that leaves a
// barrier that has not completed takes its arrival back (__gsharp_grid_unarrive).
// The arrival counter is the one cooperative_groups uses: the barrier word of the grid workspace that the driver gives
// every cooperative launch, starting at 0. Each barrier adds exactly 2^31 to it (block 0 adds 2^31 - (blocks - 1),
// every other block 1), so a barrier is complete when bit 31 has flipped since the thread's own arrival, and no state
// has to be reset between barriers. One global atomic per block and barrier. The release add after __syncthreads and
// the acquire load before the last __syncthreads order the writes of a block before the barrier with the reads of every
// block after it (the same order as cooperative_groups' sync_grids on sm_70+, which has no fence either).
// cg::details::get_grid_workspace is an internal function of the CUDA headers (the envreg1/envreg2 address of the
// driver's grid workspace, unchanged since CUDA 11); the static_assert stops the build if its layout changes.
#if defined(__CUDA_ARCH__) && __CUDA_ARCH__ >= 700
static_assert(sizeof(cg::details::grid_workspace) == 8u && __builtin_offsetof(cg::details::grid_workspace, barrier) == 4u,
    "G#: the layout of cooperative_groups' grid workspace changed; __gsharp_grid_barrier uses its barrier word");

// Takes back the arrival of a block that leaves a grid barrier, unless the barrier completed meanwhile; old is the
// counter before the block's add. [WHY] The counter must be a multiple of 2^31 when the launch ends: a launch captured
// into a CUDA graph keeps its grid workspace from one replay to the next (measured with driver 621.14, not documented
// by NVIDIA: the arrivals of a failed replay made a block of the next replay pass its first barrier before the others
// arrived), so an arrival left in a barrier that never completed would break every barrier of the later replays. The
// take-back is also right with a driver that clears the workspace for every replay. The compare-and-swap takes the add
// back only while the barrier has not completed: a completed barrier already counted it, and a barrier without it
// cannot complete, as this block no longer arrives. A block that already passed this barrier never comes back to it,
// so it cannot complete later either.
__device__ __noinline__ void __gsharp_grid_unarrive(unsigned int old) {
    unsigned int* arrived = &cg::details::get_grid_workspace()->barrier;
    const unsigned int blocks = gridDim.x * gridDim.y * gridDim.z;
    const unsigned int add = blockIdx.x + blockIdx.y + blockIdx.z == 0u ? 0x80000000u - (blocks - 1u) : 1u;
    unsigned int current = *reinterpret_cast<volatile unsigned int*>(arrived);
    while (((old ^ current) & 0x80000000u) == 0u) {
        const unsigned int seen = atomicCAS(arrived, current, current - add);
        if (seen == current) {
            return;
        }
        current = seen;
    }
}

// The grid barrier of both modes: the election, the arrival, the wait and the leave. The modes differ only in the
// order of the add and the read of the abort word: Deterministic (arriveFirst false) reads the word first and does not
// arrive after a failure; Fast (arriveFirst true) adds first. [WHY] One function with a constant argument, and the
// add written out in both places: passing the arrival as a lambda raised the registers of the cooperative GpuMath
// megakernels (ReciprocalNewtonRaphsonMega from 48 to 68, which no longer fits a cooperative launch of its grid), and
// a helper function for the add raised those of kernels with a grid barrier by up to 2.
__device__ __forceinline__ void __gsharp_grid_barrier(const bool arriveFirst) {
    // The elected rank, or __GSHARP_WAIT_LEAVE once the arriving thread saw that the launch failed
    __shared__ unsigned int leader;
    const unsigned int rank = threadIdx.x + blockDim.x * (threadIdx.y + blockDim.y * threadIdx.z);
    // Every thread writes the same value, and no thread reads it before the barrier below
    leader = 0xFFFFFFFFu;
    __syncthreads();
    atomicMin(&leader, rank);
    __syncthreads();
    if (rank == leader) {
        unsigned int* arrived = &cg::details::get_grid_workspace()->barrier;
        // Block 0 adds 2^31 - (blocks - 1), every other block 1
        const unsigned int blocks = gridDim.x * gridDim.y * gridDim.z;
        const unsigned int add = blockIdx.x + blockIdx.y + blockIdx.z == 0u ? 0x80000000u - (blocks - 1u) : 1u;
        unsigned int old = 0u;
        if (arriveFirst) {
            asm volatile("atom.add.release.gpu.u32 %0,[%1],%2;" : "=r"(old) : "l"(arrived), "r"(add) : "memory");
        }
        // Deterministic: a block whose launch failed does not arrive (the release add would wait for this load anyway).
        // Fast: the block has arrived and leaves without waiting, taking its arrival back
        const unsigned int epoch = __gsharp_launch_epoch;
        const unsigned int* abortWord = __gsharp_launch_abort_word(epoch);
        const unsigned int aborted =
            abortWord != nullptr ? *reinterpret_cast<const volatile unsigned int*>(abortWord) : 0u;
        if (aborted != 0u && aborted == epoch) {
            if (arriveFirst) {
                __gsharp_grid_unarrive(old);
            }
            *reinterpret_cast<volatile unsigned int*>(&leader) = __GSHARP_WAIT_LEAVE;
        } else {
            if (!arriveFirst) {
                asm volatile("atom.add.release.gpu.u32 %0,[%1],%2;" : "=r"(old) : "l"(arrived), "r"(add) : "memory");
            }
            unsigned int current;
            unsigned int polls = 0u;
            for (;;) {
                asm volatile("ld.acquire.gpu.u32 %0,[%1];" : "=r"(current) : "l"(arrived) : "memory");
                if (((old ^ current) & 0x80000000u) != 0u) {
                    break;
                }
                if ((++polls & 1023u) == 0u) {
                    __gsharp_wait_check(&leader, old);
                    if (*reinterpret_cast<volatile unsigned int*>(&leader) == __GSHARP_WAIT_LEAVE) {
                        break;
                    }
                }
            }
        }
    }
    // The arriving thread joins this __syncthreads also when it marked the block, so the barrier orders its store
    // before the read below
    __syncthreads();
    // [WHY] A thread that is already at the next barrier may have written 0xFFFFFFFF before a slower one reads here,
    // which is not the leave value either; a block that leaves never writes leader again. The read is volatile so that
    // it is not taken from before the barrier
    if (*reinterpret_cast<volatile unsigned int*>(&leader) == __GSHARP_WAIT_LEAVE) {
        __gsharp_exit_thread();
    }
}

// Gpu.SyncGrid() and Gpu.SyncGrid(GridSyncMode.Deterministic)
__device__ __forceinline__ void __gsharp_grid_sync() { __gsharp_grid_barrier(false); }

// Gpu.SyncGrid(GridSyncMode.Fast)
__device__ __forceinline__ void __gsharp_grid_sync_fast() { __gsharp_grid_barrier(true); }
#else
__device__ __forceinline__ void __gsharp_grid_sync() { cg::this_grid().sync(); }
__device__ __forceinline__ void __gsharp_grid_sync_fast() { cg::this_grid().sync(); }
#endif

// --- AsParallel() aggregates (Grid strategy, cooperative kernels) ---
// The linear rank and the number of the blocks of the grid (all three dimensions)
__device__ __forceinline__ unsigned int __gsharp_grid_block_rank() { return blockIdx.x + gridDim.x * (blockIdx.y + gridDim.y * blockIdx.z); }
__device__ __forceinline__ unsigned int __gsharp_grid_block_count() { return gridDim.x * gridDim.y * gridDim.z; }

// Every thread of the grid receives the reduction of the values of all the threads of the grid. The scratch holds one
// 16-byte slot per block (its partial result) and one for the total. [WHY] A grid sync orders global memory (release
// before, acquire after: __gsharp_grid_sync), so block 0 reads every partial after the first sync, and every
// thread reads the total after the second. The next call writes the slots only after both syncs of this one, so one
// scratch serves every call of the kernel.
template <typename T, typename Op>
__device__ T __gsharp_grid_reduce(T value, Op op, unsigned char* scratch)
{
    const T blockTotal = __gsharp_block_reduce(value, op);
    const unsigned int blocks = __gsharp_grid_block_count();
    if (__gsharp_block_rank() == 0u)
    {
        *reinterpret_cast<T*>(scratch + 16u * __gsharp_grid_block_rank()) = blockTotal;
    }
    __gsharp_grid_sync();
    if (__gsharp_grid_block_rank() == 0u && __gsharp_block_rank() == 0u)
    {
        T total = *reinterpret_cast<T*>(scratch);
        for (unsigned int b = 1u; b < blocks; b++)
        {
            total = op(total, *reinterpret_cast<T*>(scratch + 16u * b));
        }
        *reinterpret_cast<T*>(scratch + 16u * blocks) = total;
    }
    __gsharp_grid_sync();
    return *reinterpret_cast<T*>(scratch + 16u * blocks);
}

// --- Dynamic shared memory ---
// [WHY] Every dynamic SharedMemory<T> declaration is one region per block (static storage, like __shared__). Regions are
// carved out of the one dynamic buffer in the order a thread first reaches them; each thread keeps its own table.
extern __shared__ __align__(16) unsigned char __gsharp_dynamic_smem[];
#define __GSHARP_DSMEM_UNSET 0xFFFFFFFFu
#define __GSHARP_DSMEM_ALIGN(T) ((alignof(T) > 16) ? alignof(T) : 16)

// [WHY] Not volatile: %dynamic_smem_size is fixed for a launch, so the compiler may hoist the read out of a loop.
__device__ __forceinline__ unsigned int __gsharp_dynamic_smem_size() {
    unsigned int size;
    asm("mov.u32 %0, %%dynamic_smem_size;" : "=r"(size));
    return size;
}

template <typename TDsmem>
__device__ __forceinline__ void __gsharp_dsmem_init(TDsmem* s) {
    s->cursor = 0;
    #pragma unroll
    for (int i = 0; i < TDsmem::Slots; i++) { s->offset[i] = __GSHARP_DSMEM_UNSET; s->size[i] = 0; }
}

template <int Slot, typename TDsmem>
__device__ __forceinline__ unsigned char* __gsharp_dsmem_place(TDsmem* s, unsigned long long bytes, unsigned int align) {
    static_assert(Slot >= 0 && Slot < TDsmem::Slots, "G#: shared memory declaration has no slot in this kernel");
    unsigned int offset = s->offset[Slot];
    if (offset == __GSHARP_DSMEM_UNSET) {
        offset = (s->cursor + align - 1) & ~(align - 1);
        s->offset[Slot] = offset;
        s->size[Slot] = 0;
        // An empty region ends at its own offset and stays the last one until another region is placed
        s->cursor = offset;
    } else if (bytes > s->size[Slot] && offset + s->size[Slot] != s->cursor) {
        __gsharp_trap(GSharpErrorCode::InvalidOperation, "G#: a shared memory declaration grew after a later declaration");
    }
    if (bytes > s->size[Slot]) {
        // [WHY] bytes is checked alone first: a negative count arrives as at least 2^63 bytes, and adding the offset to
        // it would wrap past 2^64 to a small end
        unsigned long long limit = __gsharp_dynamic_smem_size();
        if (bytes > limit || (unsigned long long)offset + bytes > limit) {
            __gsharp_trap(GSharpErrorCode::ArgumentOutOfRange, "G#: dynamic shared memory exceeds sharedMemBytes");
        }
        s->size[Slot] = (unsigned int)bytes;
        s->cursor = offset + (unsigned int)bytes;
    }
    return __gsharp_dynamic_smem + offset;
}

#include "Prelude/VectorConstructors.hpp"
#include "Prelude/Sort.hpp"
// --- ZeroAlloc.Linq (StackLinq) ---
#include "StackLinq.cuh"
