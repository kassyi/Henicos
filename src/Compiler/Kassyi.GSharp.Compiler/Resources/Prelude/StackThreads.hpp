// --- One stack per GPU thread (GSharpNativeBarrierModel=Stack) ---
// A kernel that reaches Gpu.SyncThreads() or a warp collective runs as it is written, each GPU thread of a block on a
// stack of its own, all on the OpenMP thread that runs the block. A thread leaves its stack only at Gpu.SyncThreads(), at
// a warp collective, where a thread-dependent branch ends in a method that reaches Warp.ActiveMask(), and when it ends;
// the launcher's rounds decide which thread runs next, as they do for a state machine.

// The address space reserved for each stack. Only the pages a thread touches are committed
#ifndef __GSHARP_STACK_RESERVE_BYTES
#define __GSHARP_STACK_RESERVE_BYTES (1024u * 1024u)
#endif

#if defined(__SANITIZE_ADDRESS__)
#define __GSHARP_STACK_ASAN 1
#elif defined(__has_feature)
#if __has_feature(address_sanitizer)
#define __GSHARP_STACK_ASAN 1
#endif
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
#include <excpt.h>
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
#include <atomic>
#include <cerrno>
#include <fcntl.h>
#include <mutex>
#include <new>
#include <signal.h>
#include <sys/mman.h>
#include <sys/syscall.h>
#include <ucontext.h>
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
    bool overflowed = false;                         // set by SEH / SIGSEGV handler on stack guard hit
    bool overflow_reported = false;                  // prevents duplicate failure recording in __gsharp_stack_resume
    __gsharp_stack_block_base* block = nullptr;
    void* stack = nullptr;                           // Windows: the fiber. Linux: the base of the mapping
    size_t reserve = 0;                              // reserved bytes of this stack
    void* sp = nullptr;                              // Linux: the stack pointer saved while the thread does not run
};

// A GPU thread of a kernel that reaches a warp collective: the members that the warp functions of WarpAndHints.hpp read
// from a state machine context, under the same names, so that the launcher's rounds and the meeting of the lanes are
// the ones of the state machine. Only a kernel that reaches Warp.ActiveMask() enters the reconvergence regions; for any
// other kernel they stay empty, and the warp functions answer as for a context without them
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

struct __gsharp_stack_pool;
inline thread_local const __gsharp_stack_pool* __gsharp_stack_owner = nullptr;
inline thread_local uint64_t __gsharp_stack_created_count = 0;

extern "C" __declspec(dllexport) GSHARP_EXPORT_INLINE uint64_t __gsharp_stack_get_created_count() noexcept {
    return __gsharp_stack_created_count;
}

struct __gsharp_stack_free_entry {
    void* stack = nullptr;
    size_t reserve = 0;
};

// The stacks of one OpenMP thread, kept between the blocks of one launch
struct __gsharp_stack_pool {
    std::vector<__gsharp_stack_free_entry> free;
    // Windows: the fiber of the OpenMP thread. Linux: its stack pointer while a GPU thread runs
    void* scheduler = nullptr;
    bool converted = false;      // Windows: this pool made the OpenMP thread a fiber
    __gsharp_stack_fp_state fp{};
    const __gsharp_stack_pool* previous_owner = nullptr;
    explicit __gsharp_stack_pool(__gsharp_stack_fp_state initialFp = __gsharp_stack_capture_fp()) noexcept
        : fp(initialFp), previous_owner(__gsharp_stack_owner) {
        __gsharp_stack_owner = this;
    }
    __gsharp_stack_pool(const __gsharp_stack_pool&) = delete;
    __gsharp_stack_pool& operator=(const __gsharp_stack_pool&) = delete;
    void* acquire(size_t reserveBytes);                     // a free stack or a new one; throws std::bad_alloc
    void release(void* stack, size_t reserveBytes) noexcept;  // back into free
    void destroy(void* stack, size_t reserveBytes) noexcept;  // DeleteFiber / munmap
    ~__gsharp_stack_pool() noexcept;     // destroys the free stacks; Windows: ConvertFiberToThread() when converted
};

// One block, apart from the type of its GPU threads
struct __gsharp_stack_block_base {
    __gsharp_stack_pool& pool;
    void (*run)(void*) = nullptr;        // runs the kernel body once, for the calling GPU thread
    void* argument = nullptr;
    const char* kernel = nullptr;
    std::exception_ptr pending;          // an exception of a GPU thread that is not a failed check
    __gsharp_stack_block_base(__gsharp_stack_pool& stacks, void (*body)(void*), void* bodyArgument,
                              const char* kernelName = nullptr) noexcept
        : pool(stacks), run(body), argument(bodyArgument), kernel(kernelName) {}
};

// The GPU threads of one block: __gsharp_stack_thread, or __gsharp_stack_warp_thread for a kernel that reaches a warp
// collective
template <typename TThread>
struct __gsharp_stack_block_of : __gsharp_stack_block_base {
    std::vector<TThread> threads;
    template <typename TBody>
    __gsharp_stack_block_of(__gsharp_stack_pool& stacks, uint32_t threadCount, TBody& body,
                            const char* kernelName = nullptr);
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

// The launches that have run on native stacks on the calling thread, read by the differential harness
inline thread_local uint64_t __gsharp_stack_launch_count = 0;

extern "C" __declspec(dllexport) GSHARP_EXPORT_INLINE uint64_t __gsharp_stack_get_launch_count() noexcept {
    return __gsharp_stack_launch_count;
}

// The whole life of one GPU thread on its stack. No exception leaves it
__noinline__ inline void __gsharp_stack_run_current() noexcept {
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
inline void __gsharp_stack_run_guarded(__gsharp_stack_thread* self) noexcept {
    __try {
        __gsharp_stack_run_current();
    } __except (GetExceptionCode() == 0xC00000FDu ? 1 : 0) {
        self->overflowed = true;
        self->__gsharp_state = -1;
        self->__gsharp_yielded_for_block_barrier = false;
        self->__gsharp_yielded_for_grid_sync = false;
    }
}

inline void __stdcall __gsharp_stack_fiber_main(void*) {
    for (;;) {
        __gsharp_stack_run_guarded(__gsharp_stack_current);
        SwitchToFiber(__gsharp_stack_current->block->pool.scheduler);
    }
}
#elif defined(__linux__) && defined(__x86_64__)
inline constexpr size_t __gsharp_stack_guard_bytes = 64u * 1024u;

inline long __gsharp_current_tid() noexcept {
    return static_cast<long>(::syscall(SYS_gettid));
}

struct __gsharp_altstack_node {
    std::atomic<long> tid{0};
    std::atomic<__gsharp_stack_thread*> current{nullptr};
    __gsharp_altstack_node* next{nullptr};
};

inline void __gsharp_stack_range_of(
    const __gsharp_stack_thread* thread,
    char** low,
    char** guardEnd,
    char** top) noexcept {
    char* base = static_cast<char*>(thread->stack);
    if (low != nullptr) {
        *low = base;
    }
    if (guardEnd != nullptr) {
        *guardEnd = base + __gsharp_stack_guard_bytes;
    }
    if (top != nullptr) {
        uintptr_t rawTop = reinterpret_cast<uintptr_t>(base + thread->reserve);
        *top = reinterpret_cast<char*>(rawTop & ~static_cast<uintptr_t>(15));
    }
}

#if !defined(__GSHARP_STACK_ASAN)
inline constexpr size_t __gsharp_altstack_bytes = 64u * 1024u;

// The signal stack this prelude gave an OS thread, and its entry in the list the handler reads
struct __gsharp_altstack_tls {
    bool installed = false;
    __gsharp_altstack_node* node = nullptr;
    void* alt = nullptr;
    stack_t previous{};
    bool release_registered = false; // __gsharp_tls_release is constructed, so its destructor runs at the thread end
};

inline thread_local __gsharp_altstack_tls __gsharp_tls_state{};

// At the end of the thread the entry is freed for another thread (tid 0) and the signal stack the thread had before
// is put back, and ours is unmapped. A launch from a thread_local destructor that runs after this one installs again.
// [WHY] The state is a trivially destructible object of its own: the stores of a destructor to the members of its own
// object are dead once that object ends, and the compiler drops them, so a later launch would find the state still
// installed while the handler no longer finds the entry
struct __gsharp_altstack_release {
    bool armed = false;
    ~__gsharp_altstack_release() noexcept {
        __gsharp_altstack_tls& state = __gsharp_tls_state;
        if (state.node != nullptr) {
            state.node->current.store(nullptr, std::memory_order_release);
            state.node->tid.store(0, std::memory_order_release);
            state.node = nullptr;
        }
        if (state.alt != nullptr) {
            stack_t now{};
            if (::sigaltstack(nullptr, &now) == 0 && now.ss_sp == state.alt && (now.ss_flags & SS_ONSTACK) == 0) {
                stack_t restore = state.previous;
                restore.ss_flags = (state.previous.ss_flags & SS_DISABLE) != 0 ? SS_DISABLE : 0;
                if (::sigaltstack(&restore, nullptr) == 0) {
                    ::munmap(state.alt, __gsharp_altstack_bytes);
                }
            }
            state.alt = nullptr;
        }
        state.installed = false;
    }
};

inline thread_local __gsharp_altstack_release __gsharp_tls_release{};

inline std::once_flag __gsharp_segv_once;
inline struct sigaction __gsharp_prev_sigsegv{};
inline std::atomic<__gsharp_altstack_node*> __gsharp_altstack_head{nullptr};

inline void __gsharp_forward_sigsegv(int sig, siginfo_t* info, void* uctx) noexcept;

inline void __gsharp_segv_handler(int sig, siginfo_t* info, void* uctx) noexcept {
    const long tid = __gsharp_current_tid();
    if (tid <= 0) {
        __gsharp_forward_sigsegv(sig, info, uctx);
        return;
    }
    __gsharp_stack_thread* self = nullptr;
    for (const __gsharp_altstack_node* node = __gsharp_altstack_head.load(std::memory_order_acquire);
         node != nullptr;
         node = node->next) {
        if (node->tid.load(std::memory_order_acquire) == tid) {
            __gsharp_stack_thread* candidate = node->current.load(std::memory_order_acquire);
            if (candidate != nullptr) {
                self = candidate;
                break;
            }
        }
    }
    if (self == nullptr || self->stack == nullptr || info == nullptr) {
        __gsharp_forward_sigsegv(sig, info, uctx);
        return;
    }
    char* low = nullptr;
    char* guardEnd = nullptr;
    __gsharp_stack_range_of(self, &low, &guardEnd, nullptr);
    const uintptr_t addr = reinterpret_cast<uintptr_t>(info->si_addr);
    if (addr < reinterpret_cast<uintptr_t>(low) || addr >= reinterpret_cast<uintptr_t>(guardEnd)) {
        __gsharp_forward_sigsegv(sig, info, uctx);
        return;
    }
    self->overflowed = true;
    self->__gsharp_state = -1;
    self->__gsharp_yielded_for_block_barrier = false;
    self->__gsharp_yielded_for_grid_sync = false;
    if (uctx != nullptr) {
        const auto* uc = static_cast<const ucontext_t*>(uctx);
        sigset_t restoreMask = uc->uc_sigmask;
        ::sigdelset(&restoreMask, SIGSEGV);
        ::pthread_sigmask(SIG_SETMASK, &restoreMask, nullptr);
    } else {
        sigset_t unblock{};
        ::sigemptyset(&unblock);
        ::sigaddset(&unblock, SIGSEGV);
        ::pthread_sigmask(SIG_UNBLOCK, &unblock, nullptr);
    }
    void* dummy = nullptr;
    __gsharp_stack_switch(&dummy, self->block->pool.scheduler);
    std::abort();
}

inline void __gsharp_forward_sigsegv(int sig, siginfo_t* info, void* uctx) noexcept {
    const struct sigaction prev = __gsharp_prev_sigsegv;
    if ((prev.sa_flags & SA_SIGINFO) != 0 &&
        prev.sa_sigaction != nullptr &&
        prev.sa_sigaction != &__gsharp_segv_handler) {
        prev.sa_sigaction(sig, info, uctx);
        return;
    }
    if ((prev.sa_flags & SA_SIGINFO) == 0 &&
        prev.sa_handler != nullptr &&
        prev.sa_handler != SIG_DFL &&
        prev.sa_handler != SIG_IGN) {
        prev.sa_handler(sig);
        return;
    }
    if ((prev.sa_flags & SA_SIGINFO) == 0 && prev.sa_handler == SIG_IGN) {
        return;
    }
    struct sigaction dfl{};
    dfl.sa_handler = SIG_DFL;
    sigemptyset(&dfl.sa_mask);
    ::sigaction(sig, &dfl, nullptr);
}
#endif

extern "C" inline void __gsharp_stack_entry(void*) {
    __gsharp_stack_run_current();
    __gsharp_stack_thread* self = __gsharp_stack_current;
    __gsharp_stack_switch(&self->sp, self->block->pool.scheduler);
    std::abort(); // an ended thread is never resumed
}
#endif

inline void __gsharp_stack_pool::destroy(void* stack, size_t reserveBytes) noexcept {
    if (stack == nullptr) {
        return;
    }
#if defined(_WIN32)
    (void)reserveBytes;
    DeleteFiber(stack);
#elif defined(__linux__) && defined(__x86_64__)
    ::munmap(stack, reserveBytes);
#endif
}

inline void __gsharp_stack_pool::release(void* stack, size_t reserveBytes) noexcept {
    if (stack == nullptr) {
        return;
    }
    try {
        free.push_back({stack, reserveBytes});
    } catch (...) {
        destroy(stack, reserveBytes);
    }
}

inline void* __gsharp_stack_pool::acquire(size_t reserveBytes) {
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
    for (size_t i = free.size(); i > 0; --i) {
        if (free[i - 1].reserve == reserveBytes) {
            void* stack = free[i - 1].stack;
            free[i - 1] = free.back();
            free.pop_back();
            return stack;
        }
    }
    void* stack = CreateFiberEx(4096, reserveBytes, 1, __gsharp_stack_fiber_main, nullptr);
    if (stack == nullptr) {
        throw std::bad_alloc();
    }
    ++__gsharp_stack_created_count;
    return stack;
#elif defined(__linux__) && defined(__x86_64__)
#if !defined(__GSHARP_STACK_ASAN)
    std::call_once(__gsharp_segv_once, []() {
        struct sigaction curAction{};
        if (::sigaction(SIGSEGV, nullptr, &curAction) != 0) {
            throw std::bad_alloc();
        }
        struct sigaction sa{};
        sa.sa_sigaction = &__gsharp_segv_handler;
        sa.sa_mask = curAction.sa_mask;
        sigdelset(&sa.sa_mask, SIGSEGV);
        sa.sa_flags = SA_SIGINFO | SA_ONSTACK | (curAction.sa_flags & SA_RESTART);
        if (::sigaction(SIGSEGV, &sa, &__gsharp_prev_sigsegv) != 0) {
            throw std::bad_alloc();
        }
    });
    // [WHY] A launch from a thread_local destructor that runs after ~__gsharp_altstack_release() installs again; that
    // signal stack stays mapped, as the destructor does not run a second time
    if (!__gsharp_tls_state.installed && __gsharp_tls_state.node == nullptr) {
        const long tid = __gsharp_current_tid();
        // An entry that an ended thread freed, else a new one. Entries are never freed, as the handler of another
        // thread may be reading them: the list is as long as the most OS threads that ran stacks at once
        __gsharp_altstack_node* node = nullptr;
        for (__gsharp_altstack_node* n = __gsharp_altstack_head.load(std::memory_order_acquire); n != nullptr;
             n = n->next) {
            long expected = 0;
            if (n->tid.compare_exchange_strong(expected, tid, std::memory_order_acq_rel)) {
                node = n;
                break;
            }
        }
        if (node == nullptr) {
            node = new __gsharp_altstack_node{};
            node->tid.store(tid, std::memory_order_relaxed);
            node->next = __gsharp_altstack_head.load(std::memory_order_relaxed);
            while (!__gsharp_altstack_head.compare_exchange_weak(
                node->next,
                node,
                std::memory_order_release,
                std::memory_order_relaxed)) {
            }
        }
        __gsharp_tls_state.node = node;
    }
    if (!__gsharp_tls_state.installed) {
        // [WHY] A thread that has a signal stack (CoreCLR gives one to each thread it runs) keeps it: the handler
        // reads nothing on it. Only a thread without one (an OpenMP worker) gets ours
        if (::sigaltstack(nullptr, &__gsharp_tls_state.previous) != 0) {
            throw std::bad_alloc();
        }
        if ((__gsharp_tls_state.previous.ss_flags & SS_DISABLE) != 0) {
            void* altBase = ::mmap(nullptr, __gsharp_altstack_bytes, PROT_READ | PROT_WRITE,
                                   MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
            if (altBase == MAP_FAILED) {
                throw std::bad_alloc();
            }
            stack_t ss{};
            ss.ss_sp = altBase;
            ss.ss_size = __gsharp_altstack_bytes;
            ss.ss_flags = 0;
            if (::sigaltstack(&ss, nullptr) != 0) {
                ::munmap(altBase, __gsharp_altstack_bytes);
                throw std::bad_alloc();
            }
            __gsharp_tls_state.alt = altBase;
        }
        __gsharp_tls_state.installed = true;
        // Registers the release at the end of the thread, once: a launch after the release touches it no more
        if (!__gsharp_tls_state.release_registered) {
            __gsharp_tls_state.release_registered = true;
            __gsharp_tls_release.armed = true;
        }
    }
#endif
    for (size_t i = free.size(); i > 0; --i) {
        if (free[i - 1].reserve == reserveBytes) {
            void* stack = free[i - 1].stack;
            free[i - 1] = free.back();
            free.pop_back();
            return stack;
        }
    }
    long page = ::sysconf(_SC_PAGESIZE);
    if (page <= 0 || __gsharp_stack_guard_bytes >= reserveBytes) {
        throw std::bad_alloc();
    }
    void* base = ::mmap(
        nullptr,
        reserveBytes,
        PROT_READ | PROT_WRITE,
        MAP_PRIVATE | MAP_ANONYMOUS | MAP_NORESERVE,
        -1,
        0);
    if (base == MAP_FAILED) {
        throw std::bad_alloc();
    }
    if (::mprotect(base, __gsharp_stack_guard_bytes, PROT_NONE) != 0) {
        ::munmap(base, reserveBytes);
        throw std::bad_alloc();
    }
    ++__gsharp_stack_created_count;
    return base;
#endif
}

inline __gsharp_stack_pool::~__gsharp_stack_pool() noexcept {
    for (const __gsharp_stack_free_entry& entry : free) {
        destroy(entry.stack, entry.reserve);
    }
#if defined(_WIN32)
    if (converted) {
        ConvertFiberToThread();
    }
#endif
    __gsharp_stack_owner = previous_owner;
}

template <typename TThread>
template <typename TBody>
inline __gsharp_stack_block_of<TThread>::__gsharp_stack_block_of(__gsharp_stack_pool& stacks, uint32_t threadCount,
                                                                 TBody& body, const char* kernelName)
    : __gsharp_stack_block_base(stacks, [](void* p) { (*static_cast<TBody*>(p))(); }, &body, kernelName) {
    threads.resize(threadCount);
    __gsharp_stack_bind_threads(threads);
    try {
        for (uint32_t i = 0; i < threadCount; i++) {
            TThread& thread = threads[i];
            thread.block = this;
            thread.reserve = __GSHARP_STACK_RESERVE_BYTES;
            thread.stack = pool.acquire(thread.reserve);
#if defined(__linux__) && defined(__x86_64__)
            char* topPtr = nullptr;
            __gsharp_stack_range_of(&thread, nullptr, nullptr, &topPtr);
            uintptr_t top = reinterpret_cast<uintptr_t>(topPtr);
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
                pool.release(thread.stack, thread.reserve);
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
        if (!thread.overflowed && (!thread.started || thread.__gsharp_state == -1)) {
            pool.release(thread.stack, thread.reserve);
        } else {
            pool.destroy(thread.stack, thread.reserve);
        }
#else
        char* low = nullptr;
        __gsharp_stack_range_of(&thread, &low, nullptr, nullptr);
        pool.release(low, thread.reserve);
#endif
        thread.stack = nullptr;
    }
}

// The launcher's step: runs the thread until it reaches Gpu.SyncThreads() or a warp collective, or ends
inline void __gsharp_stack_resume(__gsharp_stack_thread& thread) {
    __gsharp_stack_block_base* block = thread.block;
    if (&block->pool != __gsharp_stack_owner) {
        __gsharp_trap(
            GSharpErrorCode::InvalidOperation,
            "G#: internal error: a GPU thread of the stack model was resumed on another OS thread than the one "
            "that created its stack (please report this G# bug)");
    }
    thread.started = true;
    __gsharp_stack_current = &thread;
#if defined(__linux__) && defined(__x86_64__) && !defined(__GSHARP_STACK_ASAN)
    if (__gsharp_tls_state.node != nullptr) {
        __gsharp_tls_state.node->current.store(&thread, std::memory_order_release);
    }
    std::atomic_signal_fence(std::memory_order_acq_rel);
#endif
#if defined(_WIN32)
    SwitchToFiber(thread.stack);
#elif defined(__linux__) && defined(__x86_64__)
    __gsharp_stack_switch(&block->pool.scheduler, thread.sp);
#endif
#if defined(__linux__) && defined(__x86_64__) && !defined(__GSHARP_STACK_ASAN)
    std::atomic_signal_fence(std::memory_order_acq_rel);
    if (__gsharp_tls_state.node != nullptr) {
        __gsharp_tls_state.node->current.store(nullptr, std::memory_order_release);
    }
#endif
    __gsharp_stack_current = nullptr;
    if (thread.overflowed && !thread.overflow_reported) {
        thread.overflow_reported = true;
        char text[512];
        const char* kernelName = (block->kernel != nullptr && block->kernel[0] != '\0')
            ? block->kernel
            : "<unknown>";
        std::snprintf(
            text,
            sizeof(text),
            "G#: thread (%u,%u,%u) of block (%u,%u,%u) of %s overflowed its stack of %llu KiB on the native CPU "
            "path; raise GSharpNativeStackBytes (the GPU allows 512 KiB of local memory per thread)",
            threadIdx.x, threadIdx.y, threadIdx.z,
            blockIdx.x, blockIdx.y, blockIdx.z,
            kernelName,
            static_cast<unsigned long long>(thread.reserve / 1024u));
        __gsharp_record_failure(GSharpErrorCode::InsufficientExecutionStack, text, 0u);
    }
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

// Traps unless slot lies in the visit counts the kernel set up. The compiler fixes every offset and site
// (WarpFrameLayout), so a slot outside them is a G# bug: it is reported, never read or written
inline void __gsharp_stack_warp_check_slot(const __gsharp_stack_warp_thread* self, uint64_t slot, const char* what) {
    if (self->__gsharp_warp_visit_root == nullptr) {
        char text[320];
        std::snprintf(text, sizeof(text),
                      "G#: internal error: %s of the stack model runs before its kernel set up its visit counts "
                      "(please report this G# bug)",
                      what);
        __gsharp_trap(GSharpErrorCode::InvalidOperation, text);
    }
    uint32_t size = self->__gsharp_warp_visit_size;
    if (slot >= size) {
        char text[320];
        std::snprintf(text, sizeof(text),
                      "G#: internal error: %s of the stack model counts at %llu, outside the %u counts of its kernel "
                      "(please report this G# bug)",
                      what, (unsigned long long)slot, size);
        __gsharp_trap(GSharpErrorCode::InvalidOperation, text);
    }
}

// The counts of the frame at offset frame, whose collective site is about to count a visit
inline __gsharp_stack_warp_frame __gsharp_stack_warp_frame_at(__gsharp_stack_warp_thread* self, uint32_t frame,
                                                              uint32_t site) {
    __gsharp_stack_warp_check_slot(self, (uint64_t)frame + 1u + site, "a warp collective");
    return {self->__gsharp_warp_visit_root + frame + 1u, frame};
}

// The frame at offset frame for one of its reconvergence regions. A region number counts no visit, so only the frame's
// own slot is checked: a region is matched by the frame's path and its number, as in the state machine
inline __gsharp_stack_warp_frame __gsharp_stack_warp_region_frame(__gsharp_stack_warp_thread* self, uint32_t frame) {
    __gsharp_stack_warp_check_slot(self, frame, "a reconvergence region");
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

// Warp.ActiveMask(): the lane publishes the mask 0, which waits for no lane, and leaves its stack; in the next round it
// reads the lanes that stopped at the same call in the same round (#844)
inline uint32_t __gsharp_stack_warp_active_mask(uint32_t frame, uint32_t site, const char* location) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at = __gsharp_stack_warp_meet(self, frame, site, 0u, 0, "Warp.ActiveMask", location);
    return __gsharp_warp_active_mask_read(self, &at, site, location);
}

// The reconvergence regions of a method that reaches Warp.ActiveMask(), placed where the state machine places them
// (ReconvergenceInserter): the lane enters a thread-dependent branch or loop, or leaves it by a jump
inline void __gsharp_stack_warp_region_enter(uint32_t frame, uint32_t site) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at = __gsharp_stack_warp_region_frame(self, frame);
    __gsharp_warp_region_enter(self, &at, site);
}

inline void __gsharp_stack_warp_region_leave(uint32_t frame, uint32_t site) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at = __gsharp_stack_warp_region_frame(self, frame);
    __gsharp_warp_region_leave(self, &at, site);
}

// The lane arrives where the innermost branch it is inside ends and leaves its stack: the launcher resumes it once the
// lanes that entered the branch with it have arrived or left (__gsharp_warp_can_run); then it leaves the branch
inline void __gsharp_stack_warp_reconverge(uint32_t frame, uint32_t site, const char* location) {
    __gsharp_stack_warp_thread* self = __gsharp_stack_warp_self();
    __gsharp_stack_warp_frame at = __gsharp_stack_warp_region_frame(self, frame);
    __gsharp_warp_reconverge_arrive(self, &at, site, location);
    __gsharp_stack_yield(self);
    __gsharp_warp_reconverge_pass(self, &at, site);
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
