// --- One stack per GPU thread (GSharpNativeBarrierModel=Stack) ---
// A kernel that reaches Gpu.SyncThreads() or a warp collective runs as it is written, each GPU thread of a block on a
// stack of its own, all on the OpenMP thread that runs the block. A thread leaves its stack only at Gpu.SyncThreads(), at
// a warp collective and when it ends; the launcher's rounds decide which thread runs next, as they do for a state
// machine.

// The address space reserved for each stack. Only the pages a thread touches are committed
#ifndef __GSHARP_STACK_RESERVE_BYTES
#define __GSHARP_STACK_RESERVE_BYTES (256u * 1024u)
#endif

#if defined(__SANITIZE_ADDRESS__)
#error "GSharpNativeBarrierModel=Stack does not support AddressSanitizer yet"
#elif defined(__has_feature)
#if __has_feature(address_sanitizer)
#error "GSharpNativeBarrierModel=Stack does not support AddressSanitizer yet"
#endif
#endif

#ifdef _OPENMP
#include <omp.h>
#endif

#if defined(_WIN32)
extern "C" {
__declspec(dllimport) void* __stdcall CreateFiberEx(size_t commitSize, size_t reserveSize, unsigned long flags,
    void (__stdcall* start)(void*), void* parameter);
__declspec(dllimport) void __stdcall SwitchToFiber(void* fiber);
__declspec(dllimport) void __stdcall DeleteFiber(void* fiber);
__declspec(dllimport) void* __stdcall ConvertThreadToFiberEx(void* parameter, unsigned long flags);
__declspec(dllimport) int __stdcall ConvertFiberToThread(void);
__declspec(dllimport) int __stdcall IsThreadAFiber(void);
}
#elif defined(__linux__) && defined(__x86_64__)
#include <cerrno>
#include <fcntl.h>
#include <sys/mman.h>
#include <unistd.h>
extern "C" void __gsharp_stack_switch(void** save, void* load);
extern "C" void __gsharp_stack_trampoline();
// System V x86-64: callee-saved rbx rbp r12-r15, mxcsr and the x87 control word. Every translation unit of a split
// module includes this header, so both functions sit in one COMDAT group, which the linker keeps once, as it does for
// an inline function.
__asm__(
    ".pushsection .text.__gsharp_stack_switch,\"axG\",@progbits,__gsharp_stack_switch,comdat\n"
    ".globl __gsharp_stack_switch\n.hidden __gsharp_stack_switch\n"
    ".type __gsharp_stack_switch,@function\n__gsharp_stack_switch:\n"
    "  pushq %rbp\n  pushq %rbx\n  pushq %r12\n  pushq %r13\n  pushq %r14\n  pushq %r15\n"
    "  subq $8, %rsp\n  stmxcsr (%rsp)\n  fnstcw 4(%rsp)\n"
    "  movq %rsp, (%rdi)\n  movq %rsi, %rsp\n"
    "  ldmxcsr (%rsp)\n  fldcw 4(%rsp)\n  addq $8, %rsp\n"
    "  popq %r15\n  popq %r14\n  popq %r13\n  popq %r12\n  popq %rbx\n  popq %rbp\n  ret\n"
    ".size __gsharp_stack_switch,.-__gsharp_stack_switch\n"
    ".globl __gsharp_stack_trampoline\n.hidden __gsharp_stack_trampoline\n"
    ".type __gsharp_stack_trampoline,@function\n__gsharp_stack_trampoline:\n"
    "  movq %r13, %rdi\n  callq *%r12\n  ud2\n.size __gsharp_stack_trampoline,.-__gsharp_stack_trampoline\n"
    ".popsection\n");
#else
#error "GSharpNativeBarrierModel=Stack supports Windows and x86-64 Linux only"
#endif

struct __gsharp_stack_block_base;

// One GPU thread. The first three members are the ones the launcher's rounds read from a state machine context
struct __gsharp_stack_thread {
    int __gsharp_state = 0;                          // -1 once the thread has ended
    bool __gsharp_yielded_for_grid_sync = false;     // never set by this model
    bool __gsharp_yielded_for_block_barrier = false; // waits at Gpu.SyncThreads() until the launcher releases the block
    bool started = false;
    bool has_warp_state = false;                     // a __gsharp_stack_warp_thread
    __gsharp_stack_block_base* block = nullptr;
    void* stack = nullptr;                           // Windows: the fiber. Linux: the base of the mapping
    void* sp = nullptr;                              // Linux: the stack pointer saved while the thread does not run
};

// A GPU thread of a kernel that reaches a warp collective: the members that the warp functions of WarpAndHints.hpp read
// from a state machine context, under the same names, so that the launcher's rounds and the meeting of the lanes are
// the ones of the state machine. The members of the reconvergence regions stay unused until a kernel that reaches
// Warp.ActiveMask() runs on stacks; the warp functions then answer as for a context without them
struct __gsharp_stack_warp_thread : __gsharp_stack_thread {
    __gsharp_stack_warp_thread* __gsharp_all_contexts = nullptr;
    __gsharp_warp_record __gsharp_warp_records[2] = {};
    uint32_t __gsharp_warp_record_next = 0;
    uint32_t __gsharp_warp_round = 0;
    bool __gsharp_warp_waiting = false;
    __gsharp_warp_instance __gsharp_warp_regions[16] = {};
    uint32_t __gsharp_warp_region_depth = 0;
    bool __gsharp_warp_reconverging = false;
    uint32_t __gsharp_warp_reconverge_round = 0;
    const char* __gsharp_warp_reconverge_location = nullptr;
    uint32_t __gsharp_warp_decided_round = 0;
    bool __gsharp_warp_may_pass = false;
    // The visit counts of the collectives of the kernel, a local array on this thread's stack, and their number
    uint32_t* __gsharp_warp_visit_root = nullptr;
    uint32_t __gsharp_warp_visit_size = 0;
};

// Floating-point control registers captured on the calling thread before entering OpenMP
struct __gsharp_stack_fp_state {
    uint32_t mxcsr = 0;
    uint16_t fcw = 0;
};

inline __gsharp_stack_fp_state __gsharp_stack_capture_fp() noexcept {
    __gsharp_stack_fp_state state{};
#if defined(__linux__) && defined(__x86_64__)
    __asm__ __volatile__("stmxcsr %0\n\tfnstcw %1" : "=m"(state.mxcsr), "=m"(state.fcw));
#endif
    return state;
}

// The stacks of one OpenMP thread, kept between the blocks of one launch
struct __gsharp_stack_pool {
    std::vector<void*> free;
    // Windows: the fiber of the OpenMP thread. Linux: its stack pointer while a GPU thread runs
    void* scheduler = nullptr;
    bool converted = false;      // Windows: this pool made the OpenMP thread a fiber
    __gsharp_stack_fp_state fp{};
    explicit __gsharp_stack_pool(__gsharp_stack_fp_state initialFp = __gsharp_stack_capture_fp()) noexcept
        : fp(initialFp) {}
    __gsharp_stack_pool(const __gsharp_stack_pool&) = delete;
    __gsharp_stack_pool& operator=(const __gsharp_stack_pool&) = delete;
    void* acquire();                     // a free stack or a new one; throws std::bad_alloc
    void release(void* stack) noexcept;  // back into free
    void destroy(void* stack) noexcept;  // DeleteFiber / munmap
    ~__gsharp_stack_pool() noexcept;     // destroys the free stacks; Windows: ConvertFiberToThread() when converted
};

// One block, apart from the type of its GPU threads
struct __gsharp_stack_block_base {
    __gsharp_stack_pool& pool;
    void (*run)(void*) = nullptr;        // runs the kernel body once, for the calling GPU thread
    void* argument = nullptr;
    std::exception_ptr pending;          // an exception of a GPU thread that is not a failed check
    __gsharp_stack_block_base(__gsharp_stack_pool& stacks, void (*body)(void*), void* bodyArgument) noexcept
        : pool(stacks), run(body), argument(bodyArgument) {}
};

// The GPU threads of one block: __gsharp_stack_thread, or __gsharp_stack_warp_thread for a kernel that reaches a warp
// collective
template <typename TThread>
struct __gsharp_stack_block_of : __gsharp_stack_block_base {
    std::vector<TThread> threads;
    template <typename TBody>
    __gsharp_stack_block_of(__gsharp_stack_pool& stacks, uint32_t threadCount, TBody& body);
    ~__gsharp_stack_block_of() noexcept;
};

using __gsharp_stack_block = __gsharp_stack_block_of<__gsharp_stack_thread>;
using __gsharp_stack_warp_block = __gsharp_stack_block_of<__gsharp_stack_warp_thread>;

// The threads of a block see each other's warp state, as the contexts of a state machine do
inline void __gsharp_stack_bind_threads(std::vector<__gsharp_stack_thread>&) noexcept {}

inline void __gsharp_stack_bind_threads(std::vector<__gsharp_stack_warp_thread>& threads) noexcept {
    for (__gsharp_stack_warp_thread& thread : threads) {
        thread.has_warp_state = true;
        thread.__gsharp_all_contexts = threads.data();
    }
}

// The GPU thread that runs on the calling OpenMP thread, or nullptr while the launcher runs
inline thread_local __gsharp_stack_thread* __gsharp_stack_current = nullptr;

// The whole life of one GPU thread on its stack. No exception leaves it
inline void __gsharp_stack_run_current() noexcept {
    __gsharp_stack_thread* self = __gsharp_stack_current;
    try {
        self->block->run(self->block->argument);
    } catch (const __gsharp_step_abort&) {
        // The failure was recorded before the throw: the thread has ended, the others run on
    } catch (...) {
        self->block->pending = std::current_exception();
    }
    self->__gsharp_state = -1;
    self->__gsharp_yielded_for_block_barrier = false;
    self->__gsharp_yielded_for_grid_sync = false;
}

#if defined(_WIN32)
inline void __stdcall __gsharp_stack_fiber_main(void*) {
    for (;;) {
        __gsharp_stack_run_current();
        SwitchToFiber(__gsharp_stack_current->block->pool.scheduler);
    }
}
#elif defined(__linux__) && defined(__x86_64__)
extern "C" inline void __gsharp_stack_entry(void*) {
    __gsharp_stack_run_current();
    __gsharp_stack_thread* self = __gsharp_stack_current;
    __gsharp_stack_switch(&self->sp, self->block->pool.scheduler);
    std::abort(); // an ended thread is never resumed
}
#endif

inline void __gsharp_stack_pool::destroy(void* stack) noexcept {
    if (stack == nullptr) {
        return;
    }
#if defined(_WIN32)
    DeleteFiber(stack);
#elif defined(__linux__) && defined(__x86_64__)
    ::munmap(stack, __GSHARP_STACK_RESERVE_BYTES);
#endif
}

inline void __gsharp_stack_pool::release(void* stack) noexcept {
    if (stack == nullptr) {
        return;
    }
    try {
        free.push_back(stack);
    } catch (...) {
        destroy(stack);
    }
}

inline void* __gsharp_stack_pool::acquire() {
#if defined(_WIN32)
    if (scheduler == nullptr) {
        if (IsThreadAFiber()) {
            __gsharp_trap(
                GSharpErrorCode::InvalidOperation,
                "G#: GSharpNativeBarrierModel=Stack cannot run on a thread that is already a fiber");
        }
        scheduler = ConvertThreadToFiberEx(nullptr, 1);
        if (scheduler == nullptr) {
            throw std::bad_alloc();
        }
        converted = true;
    }
    if (!free.empty()) {
        void* stack = free.back();
        free.pop_back();
        return stack;
    }
    void* stack = CreateFiberEx(4096, __GSHARP_STACK_RESERVE_BYTES, 1, __gsharp_stack_fiber_main, nullptr);
    if (stack == nullptr) {
        throw std::bad_alloc();
    }
    return stack;
#elif defined(__linux__) && defined(__x86_64__)
    if (!free.empty()) {
        void* stack = free.back();
        free.pop_back();
        return stack;
    }
    void* base = ::mmap(
        nullptr,
        __GSHARP_STACK_RESERVE_BYTES,
        PROT_READ | PROT_WRITE,
        MAP_PRIVATE | MAP_ANONYMOUS,
        -1,
        0);
    if (base == MAP_FAILED) {
        throw std::bad_alloc();
    }
    long page = ::sysconf(_SC_PAGESIZE);
    if (page <= 0 ||
        static_cast<size_t>(page) >= __GSHARP_STACK_RESERVE_BYTES ||
        ::mprotect(base, static_cast<size_t>(page), PROT_NONE) != 0) {
        ::munmap(base, __GSHARP_STACK_RESERVE_BYTES);
        throw std::bad_alloc();
    }
    return base;
#endif
}

inline __gsharp_stack_pool::~__gsharp_stack_pool() noexcept {
    for (void* stack : free) {
        destroy(stack);
    }
#if defined(_WIN32)
    if (converted) {
        ConvertFiberToThread();
    }
#endif
}

template <typename TThread>
template <typename TBody>
inline __gsharp_stack_block_of<TThread>::__gsharp_stack_block_of(__gsharp_stack_pool& stacks, uint32_t threadCount,
                                                                 TBody& body)
    : __gsharp_stack_block_base(stacks, [](void* p) { (*static_cast<TBody*>(p))(); }, &body) {
    threads.resize(threadCount);
    __gsharp_stack_bind_threads(threads);
    try {
        for (uint32_t i = 0; i < threadCount; i++) {
            TThread& thread = threads[i];
            thread.block = this;
            thread.stack = pool.acquire();
#if defined(__linux__) && defined(__x86_64__)
            uintptr_t top = (reinterpret_cast<uintptr_t>(thread.stack) + __GSHARP_STACK_RESERVE_BYTES) &
                ~static_cast<uintptr_t>(15);
            uint64_t* sp = reinterpret_cast<uint64_t*>(top - 64);
            sp[0] = 0;
            std::memcpy(sp, &pool.fp.mxcsr, 4);
            std::memcpy(reinterpret_cast<char*>(sp) + 4, &pool.fp.fcw, 2);
            sp[1] = 0;
            sp[2] = 0;
            sp[3] = 0;
            sp[4] = reinterpret_cast<uint64_t>(&__gsharp_stack_entry);
            sp[5] = 0;
            sp[6] = 0;
            sp[7] = reinterpret_cast<uint64_t>(&__gsharp_stack_trampoline);
            thread.sp = sp;
#endif
        }
    } catch (...) {
        for (TThread& thread : threads) {
            if (thread.stack != nullptr) {
                pool.release(thread.stack);
                thread.stack = nullptr;
            }
        }
        throw;
    }
}

template <typename TThread>
inline __gsharp_stack_block_of<TThread>::~__gsharp_stack_block_of() noexcept {
    for (TThread& thread : threads) {
        if (thread.stack == nullptr) {
            continue;
        }
#if defined(_WIN32)
        if (!thread.started || thread.__gsharp_state == -1) {
            pool.release(thread.stack);
        } else {
            pool.destroy(thread.stack);
        }
#else
        pool.release(thread.stack);
#endif
        thread.stack = nullptr;
    }
}

// The launcher's step: runs the thread until it reaches Gpu.SyncThreads() or a warp collective, or ends
inline void __gsharp_stack_resume(__gsharp_stack_thread& thread) {
    __gsharp_stack_block_base* block = thread.block;
    thread.started = true;
    __gsharp_stack_current = &thread;
#if defined(_WIN32)
    SwitchToFiber(thread.stack);
#elif defined(__linux__) && defined(__x86_64__)
    __gsharp_stack_switch(&block->pool.scheduler, thread.sp);
#endif
    __gsharp_stack_current = nullptr;
    if (block->pending) {
        std::exception_ptr ex = std::move(block->pending);
        block->pending = nullptr;
        std::rethrow_exception(ex);
    }
}

// The calling GPU thread leaves its stack for the launcher, which resumes it in a later round
__noinline__ inline void __gsharp_stack_yield(__gsharp_stack_thread* self) {
#if defined(_WIN32)
    SwitchToFiber(self->block->pool.scheduler);
#elif defined(__linux__) && defined(__x86_64__)
    __gsharp_stack_switch(&self->sp, self->block->pool.scheduler);
#endif
}

// Gpu.SyncThreads(): the calling GPU thread waits until the launcher releases the block
__noinline__ inline void __gsharp_stack_barrier() {
    __gsharp_stack_thread* self = __gsharp_stack_current;
    self->__gsharp_yielded_for_block_barrier = true;
    __gsharp_stack_yield(self);
}

// A frame of the stack model: the visit counts of its collectives, a view into the counts of the kernel, and its offset
// there, which is its static call path and the same in every lane (WarpFrameLayout). The overload of
// __gsharp_warp_path_of below is more specialized than the one of WarpAndHints.hpp, which finds it by argument-dependent
// lookup when it is instantiated, so the warp functions there take the path of a frame from it.
struct __gsharp_stack_warp_frame {
    uint32_t* __gsharp_warp_visits;  // the kernel's counts + the frame's offset + 1, indexed by site
    uint32_t path;
};

template<typename TRoot>
inline uint32_t __gsharp_warp_path_of(TRoot*, __gsharp_stack_warp_frame* __gsharp_ctx) {
    return __gsharp_ctx->path;
}

// The calling GPU thread, which runs a kernel that reaches a warp collective. Any other thread is a G# bug: the compiler
// gives such a kernel a launcher of __gsharp_stack_warp_thread
inline __gsharp_stack_warp_thread* __gsharp_stack_warp_self() {
    __gsharp_stack_thread* self = __gsharp_stack_current;
    if (self == nullptr || !self->has_warp_state) {
        __gsharp_trap(GSharpErrorCode::InvalidOperation,
                      "G#: internal error: a warp collective of the stack model runs outside a launch that reaches one "
                      "(please report this G# bug)");
    }
    return static_cast<__gsharp_stack_warp_thread*>(self);
}

// The kernel's visit counts, a local array of size counts on the GPU thread's own stack (WarpFrameLayout)
inline void __gsharp_stack_warp_begin(uint32_t* visits, uint32_t size) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    self->__gsharp_warp_visit_root = visits;
    self->__gsharp_warp_visit_size = size;
}

// The counts of the frame at offset frame, whose collective site is about to count a visit. The compiler fixes every
// offset and site (WarpFrameLayout), so a count outside the kernel's counts is a G# bug: it is reported, never written
inline __gsharp_stack_warp_frame __gsharp_stack_warp_frame_at(__gsharp_stack_warp_thread* self, uint32_t frame,
                                                              uint32_t site) {
    if (self->__gsharp_warp_visit_root == nullptr) {
        __gsharp_trap(GSharpErrorCode::InvalidOperation,
                      "G#: internal error: a warp collective of the stack model runs before its kernel set up its visit "
                      "counts (please report this G# bug)");
    }
    uint32_t size = self->__gsharp_warp_visit_size;
    if ((uint64_t)frame + 1u + site >= size) {
        char text[320];
        std::snprintf(text, sizeof(text),
                      "G#: internal error: a warp collective of the stack model counts at %llu, outside the %u counts "
                      "of its kernel (please report this G# bug)",
                      (unsigned long long)frame + 1u + site, size);
        __gsharp_trap(GSharpErrorCode::InvalidOperation, text);
    }
    return {self->__gsharp_warp_visit_root + frame + 1u, frame};
}

// The lane publishes its value for this instance of the call and leaves its stack: the launcher resumes it in a later
// round, once __gsharp_warp_can_run finds the lanes of its mask met there. It always leaves, also when no lane is
// missing: a lane publishes at most once per round (the two record slots rely on it), and Warp.ActiveMask() returns the
// lanes that published in the same round
template<typename T>
inline __gsharp_stack_warp_frame __gsharp_stack_warp_meet(__gsharp_stack_warp_thread* self, uint32_t frame,
                                                         uint32_t site, uint32_t mask, T value, const char* name,
                                                         const char* location) {
    __gsharp_stack_warp_frame at = __gsharp_stack_warp_frame_at(self, frame, site);
    __gsharp_warp_publish(self, &at, site, mask, value, name, location);
    __gsharp_stack_yield(self);
    return at;
}

// One function per warp collective: the frame, the site and the place of the call, then the arguments of the C# call
// in the order of its parameters. Each publishes and reads as the state machine does (WarpShuffleSyntax)
template<typename T>
inline T __gsharp_stack_warp_shuffle_sync(uint32_t frame, uint32_t site, const char* location, uint32_t mask, T var,
                                          int srcLane, int width = 32) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at = __gsharp_stack_warp_meet(self, frame, site, mask, var, "Warp.ShuffleSync", location);
    return __gsharp_warp_shuffle_read_sync(self, &at, site, mask, var, (int32_t)srcLane, width, location);
}

template<typename T>
inline T __gsharp_stack_warp_shuffle_up_sync(uint32_t frame, uint32_t site, const char* location, uint32_t mask, T var,
                                             unsigned int delta, int width = 32) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at =
        __gsharp_stack_warp_meet(self, frame, site, mask, var, "Warp.ShuffleUpSync", location);
    return __gsharp_warp_shuffle_read_up_sync(self, &at, site, mask, var, (uint32_t)delta, width, location);
}

template<typename T>
inline T __gsharp_stack_warp_shuffle_down_sync(uint32_t frame, uint32_t site, const char* location, uint32_t mask,
                                               T var, unsigned int delta, int width = 32) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at =
        __gsharp_stack_warp_meet(self, frame, site, mask, var, "Warp.ShuffleDownSync", location);
    return __gsharp_warp_shuffle_read_down_sync(self, &at, site, mask, var, (uint32_t)delta, width, location);
}

template<typename T>
inline T __gsharp_stack_warp_shuffle_xor_sync(uint32_t frame, uint32_t site, const char* location, uint32_t mask, T var,
                                              int laneMask, int width = 32) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at =
        __gsharp_stack_warp_meet(self, frame, site, mask, var, "Warp.ShuffleXorSync", location);
    return __gsharp_warp_shuffle_read_xor_sync(self, &at, site, mask, var, (uint32_t)laneMask, width, location);
}

inline uint32_t __gsharp_stack_warp_ballot_sync(uint32_t frame, uint32_t site, const char* location, uint32_t mask,
                                                bool predicate) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at =
        __gsharp_stack_warp_meet(self, frame, site, mask, predicate, "Warp.BallotSync", location);
    return __gsharp_warp_ballot_read_sync(self, &at, site, mask, predicate, location);
}

inline bool __gsharp_stack_warp_all_sync(uint32_t frame, uint32_t site, const char* location, uint32_t mask,
                                         bool predicate) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at =
        __gsharp_stack_warp_meet(self, frame, site, mask, predicate, "Warp.AllSync", location);
    return __gsharp_warp_all_read_sync(self, &at, site, mask, predicate, location);
}

inline bool __gsharp_stack_warp_any_sync(uint32_t frame, uint32_t site, const char* location, uint32_t mask,
                                         bool predicate) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at =
        __gsharp_stack_warp_meet(self, frame, site, mask, predicate, "Warp.AnySync", location);
    return __gsharp_warp_any_read_sync(self, &at, site, mask, predicate, location);
}

template<typename T>
inline T __gsharp_stack_warp_reduce_add_sync(uint32_t frame, uint32_t site, const char* location, uint32_t mask,
                                             T val) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at =
        __gsharp_stack_warp_meet(self, frame, site, mask, val, "Warp.ReduceAddSync", location);
    return __gsharp_warp_reduce_add_read_sync(self, &at, site, mask, val, location);
}

template<typename T>
inline T __gsharp_stack_warp_reduce_min_sync(uint32_t frame, uint32_t site, const char* location, uint32_t mask,
                                             T val) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at =
        __gsharp_stack_warp_meet(self, frame, site, mask, val, "Warp.ReduceMinSync", location);
    return __gsharp_warp_reduce_min_read_sync(self, &at, site, mask, val, location);
}

template<typename T>
inline T __gsharp_stack_warp_reduce_max_sync(uint32_t frame, uint32_t site, const char* location, uint32_t mask,
                                             T val) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at =
        __gsharp_stack_warp_meet(self, frame, site, mask, val, "Warp.ReduceMaxSync", location);
    return __gsharp_warp_reduce_max_read_sync(self, &at, site, mask, val, location);
}

template<typename T>
inline T __gsharp_stack_warp_reduce_and_sync(uint32_t frame, uint32_t site, const char* location, uint32_t mask,
                                             T val) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at =
        __gsharp_stack_warp_meet(self, frame, site, mask, val, "Warp.ReduceAndSync", location);
    return __gsharp_warp_reduce_and_read_sync(self, &at, site, mask, val, location);
}

template<typename T>
inline T __gsharp_stack_warp_reduce_or_sync(uint32_t frame, uint32_t site, const char* location, uint32_t mask,
                                            T val) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at =
        __gsharp_stack_warp_meet(self, frame, site, mask, val, "Warp.ReduceOrSync", location);
    return __gsharp_warp_reduce_or_read_sync(self, &at, site, mask, val, location);
}

template<typename T>
inline T __gsharp_stack_warp_reduce_xor_sync(uint32_t frame, uint32_t site, const char* location, uint32_t mask,
                                             T val) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at =
        __gsharp_stack_warp_meet(self, frame, site, mask, val, "Warp.ReduceXorSync", location);
    return __gsharp_warp_reduce_xor_read_sync(self, &at, site, mask, val, location);
}

template<typename T>
inline uint32_t __gsharp_stack_warp_match_any_sync(uint32_t frame, uint32_t site, const char* location, uint32_t mask,
                                                   T value) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at =
        __gsharp_stack_warp_meet(self, frame, site, mask, value, "Gpu.MatchAnySync", location);
    return __gsharp_warp_match_any_read_sync(self, &at, site, mask, value, location);
}

template<typename T>
inline uint32_t __gsharp_stack_warp_match_all_sync(uint32_t frame, uint32_t site, const char* location, uint32_t mask,
                                                   T value, int* pred) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at =
        __gsharp_stack_warp_meet(self, frame, site, mask, value, "Gpu.MatchAllSync", location);
    return __gsharp_warp_match_all_read_sync(self, &at, site, mask, value, pred, location);
}

// Gpu.SyncWarp(mask): the lanes of the mask meet without a value
inline void __gsharp_stack_warp_syncwarp(uint32_t frame, uint32_t site, const char* location,
                                         uint32_t mask = 0xFFFFFFFFu) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at = __gsharp_stack_warp_meet(self, frame, site, mask, 0, "Gpu.SyncWarp", location);
    __gsharp_warp_sync_read(self, &at, site, mask, location);
}

#if defined(__linux__) && defined(__x86_64__)
// Reads /proc/sys/vm/max_map_count without heap allocation; falls back to Linux's default 65530 on any error
inline size_t __gsharp_read_max_map_count() noexcept {
    constexpr size_t fallback = 65530;
    int fd = ::open("/proc/sys/vm/max_map_count", O_RDONLY | O_CLOEXEC);
    if (fd < 0) {
        return fallback;
    }
    char buf[64];
    size_t pos = 0;
    while (pos < sizeof(buf) - 1) {
        ssize_t n = ::read(fd, buf + pos, (sizeof(buf) - 1) - pos);
        if (n > 0) {
            pos += static_cast<size_t>(n);
        } else if (n == 0) {
            break;
        } else if (errno != EINTR) {
            ::close(fd);
            return fallback;
        }
    }
    ::close(fd);
    if (pos == 0) {
        return fallback;
    }
    buf[pos] = '\0';
    errno = 0;
    char* end = nullptr;
    unsigned long long val = std::strtoull(buf, &end, 10);
    if (end == buf || errno == ERANGE || val == 0) {
        return fallback;
    }
    if (val > static_cast<unsigned long long>((std::numeric_limits<size_t>::max)())) {
        return (std::numeric_limits<size_t>::max)();
    }
    return static_cast<size_t>(val);
}

// Counts active VMAs in /proc/self/maps without heap allocation
inline size_t __gsharp_count_proc_maps() noexcept {
    int fd = ::open("/proc/self/maps", O_RDONLY | O_CLOEXEC);
    if (fd < 0) {
        return 0;
    }
    char buf[4096];
    size_t lines = 0;
    bool hasBytes = false;
    char lastByte = '\n';
    for (;;) {
        ssize_t n = ::read(fd, buf, sizeof(buf));
        if (n > 0) {
            hasBytes = true;
            for (ssize_t i = 0; i < n; ++i) {
                if (buf[i] == '\n') {
                    ++lines;
                }
            }
            lastByte = buf[n - 1];
        } else if (n == 0) {
            break;
        } else if (errno != EINTR) {
            ::close(fd);
            return 0;
        }
    }
    ::close(fd);
    if (hasBytes && lastByte != '\n') {
        ++lines;
    }
    return lines;
}

// Remaining VMA budget for stack allocations after keeping a 1024-VMA reserve margin for runtime/OpenMP mappings
inline size_t __gsharp_linux_available_vmas() noexcept {
    constexpr size_t reserveMargin = 1024;
    size_t maxMaps = __gsharp_read_max_map_count();
    size_t usedMaps = __gsharp_count_proc_maps();
    size_t usedPlusMargin = usedMaps <= (std::numeric_limits<size_t>::max)() - reserveMargin
        ? usedMaps + reserveMargin
        : (std::numeric_limits<size_t>::max)();
    return maxMaps > usedPlusMargin ? maxMaps - usedPlusMargin : 0;
}
#endif

// Caps OpenMP concurrency per launch so active stacks stay within Linux's VMA budget (2 VMAs per stack)
inline int __gsharp_stack_thread_limit(
    uint64_t threadsPerBlock,
    int64_t totalBlocks = 0,
    size_t vmaBudgetOverride = 0) noexcept {
    int limit = 1;
#ifdef _OPENMP
    limit = omp_get_max_threads();
    if (limit < 1) {
        limit = 1;
    }
#endif
    if (totalBlocks > 0 && static_cast<uint64_t>(limit) > static_cast<uint64_t>(totalBlocks)) {
        limit = static_cast<int>(totalBlocks);
    }
#if defined(__linux__) && defined(__x86_64__)
    size_t budget = vmaBudgetOverride > 0 ? vmaBudgetOverride : __gsharp_linux_available_vmas();
    size_t stackBudget = budget / 2;
    uint64_t tpb = threadsPerBlock > 0 ? threadsPerBlock : 1;
    uint64_t allowed = static_cast<uint64_t>(stackBudget) / tpb;
    if (allowed < 1) {
        allowed = 1;
    }
    if (static_cast<uint64_t>(limit) > allowed) {
        limit = static_cast<int>(allowed);
    }
#else
    (void)threadsPerBlock;
    (void)vmaBudgetOverride;
#endif
    return limit >= 1 ? limit : 1;
}
