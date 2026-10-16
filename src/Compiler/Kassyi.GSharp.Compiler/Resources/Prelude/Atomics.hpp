// --- Atomics ---
template<typename T>
__forceinline__ T __gsharp_atomic_fetch_add(T* address, T val) {
    return reinterpret_cast<volatile std::atomic<T>*>(address)->fetch_add(val, std::memory_order_relaxed);
}

template<typename T>
__forceinline__ T __gsharp_atomic_max(T* address, T val) {
    volatile std::atomic<T>* atom = reinterpret_cast<volatile std::atomic<T>*>(address);
    T old = atom->load(std::memory_order_relaxed);
    while (old < val && !atom->compare_exchange_weak(old, val, std::memory_order_relaxed)) { }
    return old;
}

template<typename T>
__forceinline__ T __gsharp_atomic_min(T* address, T val) {
    volatile std::atomic<T>* atom = reinterpret_cast<volatile std::atomic<T>*>(address);
    T old = atom->load(std::memory_order_relaxed);
    while (val < old && !atom->compare_exchange_weak(old, val, std::memory_order_relaxed)) { }
    return old;
}

// Read-modify-write via CAS loop for operations without a native std::atomic member (float add, CUDA inc/dec wrap).
template<typename T, typename TUpdate>
__forceinline__ T __gsharp_atomic_update(T* address, TUpdate update) {
    volatile std::atomic<T>* atom = reinterpret_cast<volatile std::atomic<T>*>(address);
    T old = atom->load(std::memory_order_relaxed);
    while (!atom->compare_exchange_weak(old, update(old), std::memory_order_relaxed)) { }
    return old;
}

template<typename T>
__forceinline__ T __gsharp_atomic_fetch_sub(T* address, T val) {
    return reinterpret_cast<volatile std::atomic<T>*>(address)->fetch_sub(val, std::memory_order_relaxed);
}

template<typename T>
__forceinline__ T __gsharp_atomic_or(T* address, T val) {
    return reinterpret_cast<volatile std::atomic<T>*>(address)->fetch_or(val, std::memory_order_relaxed);
}

template<typename T>
__forceinline__ T __gsharp_atomic_and(T* address, T val) {
    return reinterpret_cast<volatile std::atomic<T>*>(address)->fetch_and(val, std::memory_order_relaxed);
}

template<typename T>
__forceinline__ T __gsharp_atomic_xor(T* address, T val) {
    return reinterpret_cast<volatile std::atomic<T>*>(address)->fetch_xor(val, std::memory_order_relaxed);
}

template<typename T>
__forceinline__ T __gsharp_atomic_cas(T* address, T compare, T val) {
    volatile std::atomic<T>* atom = reinterpret_cast<volatile std::atomic<T>*>(address);
    T expected = compare;
    atom->compare_exchange_strong(expected, val, std::memory_order_relaxed);
    return expected;
}

template<typename T>
__forceinline__ T __gsharp_atomic_exch(T* address, T val) {
    return reinterpret_cast<volatile std::atomic<T>*>(address)->exchange(val, std::memory_order_relaxed);
}

__forceinline__ int atomicAdd(int* address, int val) { return __gsharp_atomic_fetch_add(address, val); }
__forceinline__ unsigned int atomicAdd(unsigned int* address, unsigned int val) { return __gsharp_atomic_fetch_add(address, val); }
__forceinline__ unsigned long long atomicAdd(unsigned long long* address, unsigned long long val) { return __gsharp_atomic_fetch_add(address, val); }
__forceinline__ int atomicMax(int* address, int val) { return __gsharp_atomic_max(address, val); }
__forceinline__ unsigned int atomicMax(unsigned int* address, unsigned int val) { return __gsharp_atomic_max(address, val); }
__forceinline__ unsigned long long atomicMax(unsigned long long* address, unsigned long long val) { return __gsharp_atomic_max(address, val); }
__forceinline__ int atomicOr(int* address, int val) { return __gsharp_atomic_or(address, val); }
__forceinline__ unsigned int atomicOr(unsigned int* address, unsigned int val) { return __gsharp_atomic_or(address, val); }
__forceinline__ unsigned long long atomicOr(unsigned long long* address, unsigned long long val) { return __gsharp_atomic_or(address, val); }
__forceinline__ int atomicCAS(int* address, int compare, int val) { return __gsharp_atomic_cas(address, compare, val); }
__forceinline__ unsigned int atomicCAS(unsigned int* address, unsigned int compare, unsigned int val) { return __gsharp_atomic_cas(address, compare, val); }
__forceinline__ unsigned long long atomicCAS(unsigned long long* address, unsigned long long compare, unsigned long long val) { return __gsharp_atomic_cas(address, compare, val); }
__forceinline__ int atomicExch(int* address, int val) { return __gsharp_atomic_exch(address, val); }
__forceinline__ unsigned int atomicExch(unsigned int* address, unsigned int val) { return __gsharp_atomic_exch(address, val); }
__forceinline__ unsigned long long atomicExch(unsigned long long* address, unsigned long long val) { return __gsharp_atomic_exch(address, val); }
__forceinline__ float atomicExch(float* address, float val) { return __gsharp_atomic_exch(address, val); }
__forceinline__ float atomicAdd(float* address, float val) { return __gsharp_atomic_update(address, [val](float old) { return old + val; }); }
__forceinline__ int atomicSub(int* address, int val) { return __gsharp_atomic_fetch_sub(address, val); }
__forceinline__ unsigned int atomicSub(unsigned int* address, unsigned int val) { return __gsharp_atomic_fetch_sub(address, val); }
__forceinline__ int atomicMin(int* address, int val) { return __gsharp_atomic_min(address, val); }
__forceinline__ unsigned int atomicMin(unsigned int* address, unsigned int val) { return __gsharp_atomic_min(address, val); }
__forceinline__ unsigned long long atomicMin(unsigned long long* address, unsigned long long val) { return __gsharp_atomic_min(address, val); }
__forceinline__ int atomicAnd(int* address, int val) { return __gsharp_atomic_and(address, val); }
__forceinline__ unsigned int atomicAnd(unsigned int* address, unsigned int val) { return __gsharp_atomic_and(address, val); }
__forceinline__ unsigned long long atomicAnd(unsigned long long* address, unsigned long long val) { return __gsharp_atomic_and(address, val); }
__forceinline__ int atomicXor(int* address, int val) { return __gsharp_atomic_xor(address, val); }
__forceinline__ unsigned int atomicXor(unsigned int* address, unsigned int val) { return __gsharp_atomic_xor(address, val); }
__forceinline__ unsigned long long atomicXor(unsigned long long* address, unsigned long long val) { return __gsharp_atomic_xor(address, val); }
// CUDA semantics: inc wraps to 0 once old >= val; dec wraps to val when old == 0 or old > val.
__forceinline__ unsigned int atomicInc(unsigned int* address, unsigned int val) { return __gsharp_atomic_update(address, [val](unsigned int old) { return old >= val ? 0u : old + 1u; }); }
__forceinline__ unsigned int atomicDec(unsigned int* address, unsigned int val) { return __gsharp_atomic_update(address, [val](unsigned int old) { return (old == 0u || old > val) ? val : old - 1u; }); }

__forceinline__ int __gsharp_atomicCAS(int* address, int val, int compare) { return atomicCAS(address, compare, val); }
__forceinline__ unsigned int __gsharp_atomicCAS(unsigned int* address, unsigned int val, unsigned int compare) { return atomicCAS(address, compare, val); }
__forceinline__ unsigned long long __gsharp_atomicCAS(unsigned long long* address, unsigned long long val, unsigned long long compare) { return atomicCAS(address, compare, val); }

__forceinline__ int __gsharp_atomicAdd(int* address, int val) { return atomicAdd(address, val) + val; }
__forceinline__ unsigned int __gsharp_atomicAdd(unsigned int* address, unsigned int val) { return atomicAdd(address, val) + val; }
__forceinline__ unsigned long long __gsharp_atomicAdd(unsigned long long* address, unsigned long long val) { return atomicAdd(address, val) + val; }

__forceinline__ int __gsharp_atomicExch(int* address, int val) { return atomicExch(address, val); }
__forceinline__ unsigned int __gsharp_atomicExch(unsigned int* address, unsigned int val) { return atomicExch(address, val); }
__forceinline__ unsigned long long __gsharp_atomicExch(unsigned long long* address, unsigned long long val) { return atomicExch(address, val); }
