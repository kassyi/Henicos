#pragma once

// The count of a Skip / Take / SkipLast / TakeLast stage clamped to [0, remaining], as C# clamps it (the sum cannot overflow)
__device__ __forceinline__ int __gsharp_clamp_count(int count, int remaining) { return count < 0 ? 0 : (count > remaining ? remaining : count); }

template <typename T>
__device__ __forceinline__ bool __gsharp_linq_equals(const T& a, const T& b) { return a == b; }
__device__ __forceinline__ bool __gsharp_linq_equals(float a, float b) { return a == b || (a != a && b != b); }
__device__ __forceinline__ bool __gsharp_linq_equals(double a, double b) { return a == b || (a != a && b != b); }

// The result of an integer Sum: the exact sum, range-checked once. A Sum that does not fit throws OverflowException on
// every path (StackLinq contract); the sum does not depend on the order of the elements, so a sequence whose running
// sum leaves the range and comes back gives its value
[[noreturn]] __device__ __forceinline__ void __gsharp_sum_overflow() {
    __gsharp_trap(GSharpErrorCode::Overflow, "Arithmetic operation resulted in an overflow.");
}
__device__ __forceinline__ int __gsharp_sum_i32(long long sum) {
    if (sum < -2147483647ll - 1 || sum > 2147483647ll) { __gsharp_sum_overflow(); }
    return (int)sum;
}
__device__ __forceinline__ unsigned int __gsharp_sum_u32(unsigned long long sum) {
    if (sum > 4294967295ull) { __gsharp_sum_overflow(); }
    return (unsigned int)sum;
}
// The range checks of the wide total of a grid-wide int or uint sum
__device__ __forceinline__ int __gsharp_sum_i32(__gsharp_wide_sum sum) {
    const bool fits = (sum.hi == 0 && sum.lo <= 2147483647ull) ||
                      (sum.hi == -1 && sum.lo >= 18446744071562067968ull);
    if (!fits) { __gsharp_sum_overflow(); }
    return (int)(long long)sum.lo;
}
__device__ __forceinline__ unsigned int __gsharp_sum_u32(__gsharp_wide_sum sum) {
    if (sum.hi != 0 || sum.lo > 4294967295ull) { __gsharp_sum_overflow(); }
    return (unsigned int)sum.lo;
}
__device__ __forceinline__ long long __gsharp_sum_i64(__gsharp_wide_sum sum) {
    const bool fits = (sum.hi == 0 && sum.lo <= 9223372036854775807ull) ||
                      (sum.hi == -1 && sum.lo >= 9223372036854775808ull);
    if (!fits) { __gsharp_sum_overflow(); }
    return (long long)sum.lo;
}
__device__ __forceinline__ unsigned long long __gsharp_sum_u64(__gsharp_wide_sum sum) {
    if (sum.hi != 0) { __gsharp_sum_overflow(); }
    return sum.lo;
}

// How the C++ Sum adds a TElement: float adds in double and rounds once at the end, as System.Linq does; an integer adds
// exactly (a 64-bit accumulator for int and uint, 128 bits for long and ulong) and is range-checked once at the end
template <typename T>
struct __gsharp_linq_sum_ops {
    typedef T acc;
    __device__ static inline acc Zero() { return 0; }
    __device__ static inline acc Add(acc a, T v) { return a + v; }
    __device__ static inline T Result(acc a) { return a; }
};
template <>
struct __gsharp_linq_sum_ops<float> {
    typedef double acc;
    __device__ static inline acc Zero() { return 0; }
    __device__ static inline acc Add(acc a, float v) { return a + v; }
    __device__ static inline float Result(acc a) { return (float)a; }
};
template <>
struct __gsharp_linq_sum_ops<int> {
    typedef long long acc;
    __device__ static inline acc Zero() { return 0; }
    __device__ static inline acc Add(acc a, int v) { return a + v; }
    __device__ static inline int Result(acc a) { return __gsharp_sum_i32(a); }
};
template <>
struct __gsharp_linq_sum_ops<unsigned int> {
    typedef unsigned long long acc;
    __device__ static inline acc Zero() { return 0; }
    __device__ static inline acc Add(acc a, unsigned int v) { return a + v; }
    __device__ static inline unsigned int Result(acc a) { return __gsharp_sum_u32(a); }
};
template <>
struct __gsharp_linq_sum_ops<long long> {
    typedef __gsharp_wide_sum acc;
    __device__ static inline acc Zero() { acc s; s.lo = 0; s.hi = 0; return s; }
    __device__ static inline acc Add(acc a, long long v) { return __gsharp_wide_add_i64(a, v); }
    __device__ static inline long long Result(acc a) { return __gsharp_sum_i64(a); }
};
template <>
struct __gsharp_linq_sum_ops<unsigned long long> {
    typedef __gsharp_wide_sum acc;
    __device__ static inline acc Zero() { acc s; s.lo = 0; s.hi = 0; return s; }
    __device__ static inline acc Add(acc a, unsigned long long v) { return __gsharp_wide_add_u64(a, v); }
    __device__ static inline unsigned long long Result(acc a) { return __gsharp_sum_u64(a); }
};

// Whether a Max / Min element is NaN, which System.Linq places independently of its position; false for every
// other type
template <typename T>
__device__ __forceinline__ bool __gsharp_linq_is_nan(const T&) { return false; }
__device__ __forceinline__ bool __gsharp_linq_is_nan(float a) { return a != a; }
__device__ __forceinline__ bool __gsharp_linq_is_nan(double a) { return a != a; }

namespace Kassyi { namespace ZeroAlloc { namespace Linq {
    
    // The elements [index + 1, end) of an array or span; TakeLast / SkipLast narrow the range, as the span overloads of C#
    template <typename T>
    struct ArrayEnumerator {
        GSharpArray<T> source;
        int index;
        int end;

        __device__ inline ArrayEnumerator(GSharpArray<T> src) : source(src), index(-1), end(src.length) {}
        __device__ inline ArrayEnumerator(GSharpArray<T> src, int first, int last) : source(src), index(first - 1), end(last) {}

        __device__ inline int Remaining() const { return end - (index + 1); }

        __device__ inline bool MoveNext() {
            if (index + 1 < end) {
                index++;
                return true;
            }
            return false;
        }
        __device__ inline T Current() const { return source[index]; }
    };

    template <typename TSource, typename TPredicate>
    struct WhereEnumerator {
        TSource source;
        TPredicate predicate;
        
        __device__ inline WhereEnumerator(TSource src, TPredicate pred) : source(src), predicate(pred) {}
        
        __device__ inline bool MoveNext() {
            while (source.MoveNext()) {
                if (predicate(source.Current())) {
                    return true;
                }
            }
            return false;
        }
        __device__ inline auto Current() const { return source.Current(); }
    };

    template <typename TSource, typename TSelector>
    struct SelectEnumerator {
        TSource source;
        TSelector selector;
        decltype(selector(source.Current())) current{};
        
        __device__ inline SelectEnumerator(TSource src, TSelector sel) : source(src), selector(sel), current{} {}
        
        __device__ inline bool MoveNext() {
            if (source.MoveNext()) {
                current = selector(source.Current());
                return true;
            }
            return false;
        }
        __device__ inline auto Current() const { return current; }
    };

    template <typename TSource, typename TPredicate>
    struct WhereAtEnumerator {
        TSource source;
        TPredicate predicate;
        int index;
        
        __device__ inline WhereAtEnumerator(TSource src, TPredicate pred) : source(src), predicate(pred), index(0) {}
        
        __device__ inline bool MoveNext() {
            while (source.MoveNext()) {
                if (predicate(source.Current(), index++)) {
                    return true;
                }
            }
            return false;
        }
        __device__ inline auto Current() const { return source.Current(); }
    };

    template <typename TSource, typename TSelector>
    struct SelectAtEnumerator {
        TSource source;
        TSelector selector;
        int index;
        decltype(selector(source.Current(), 0)) current{};
        
        __device__ inline SelectAtEnumerator(TSource src, TSelector sel) : source(src), selector(sel), index(-1), current{} {}
        
        __device__ inline bool MoveNext() {
            if (source.MoveNext()) {
                index++;
                current = selector(source.Current(), index);
                return true;
            }
            return false;
        }
        __device__ inline auto Current() const { return current; }
    };

    template <typename TSource>
    struct TakeEnumerator {
        TSource source;
        int remaining;
        
        __device__ inline TakeEnumerator(TSource src, int count) : source(src), remaining(count > 0 ? count : 0) {}
        
        __device__ inline bool MoveNext() {
            if (remaining > 0 && source.MoveNext()) {
                remaining--;
                return true;
            }
            return false;
        }
        __device__ inline auto Current() const { return source.Current(); }
    };

    template <typename TSource>
    struct SkipEnumerator {
        TSource source;
        int remaining;
        
        __device__ inline SkipEnumerator(TSource src, int count) : source(src), remaining(count > 0 ? count : 0) {}
        
        __device__ inline bool MoveNext() {
            while (remaining > 0) {
                if (source.MoveNext()) remaining--;
                else return false;
            }
            return source.MoveNext();
        }
        __device__ inline auto Current() const { return source.Current(); }
    };

    template <typename TSource, typename TPredicate>
    struct TakeWhileEnumerator {
        TSource source;
        TPredicate predicate;
        
        __device__ inline TakeWhileEnumerator(TSource src, TPredicate pred) : source(src), predicate(pred) {}
        
        __device__ inline bool MoveNext() {
            if (source.MoveNext()) {
                if (predicate(source.Current())) {
                    return true;
                }
            }
            return false;
        }
        __device__ inline auto Current() const { return source.Current(); }
    };

    template <typename TSource, typename TPredicate>
    struct TakeWhileAtEnumerator {
        TSource source;
        TPredicate predicate;
        int index;
        
        __device__ inline TakeWhileAtEnumerator(TSource src, TPredicate pred) : source(src), predicate(pred), index(0) {}
        
        __device__ inline bool MoveNext() {
            if (source.MoveNext()) {
                if (predicate(source.Current(), index++)) {
                    return true;
                }
            }
            return false;
        }
        __device__ inline auto Current() const { return source.Current(); }
    };

    template <typename TSource, typename TPredicate>
    struct SkipWhileEnumerator {
        TSource source;
        TPredicate predicate;
        bool yielding;
        
        __device__ inline SkipWhileEnumerator(TSource src, TPredicate pred) : source(src), predicate(pred), yielding(false) {}
        
        __device__ inline bool MoveNext() {
            while (source.MoveNext()) {
                if (!yielding && predicate(source.Current())) {
                    continue;
                }
                yielding = true;
                return true;
            }
            return false;
        }
        __device__ inline auto Current() const { return source.Current(); }
    };

    template <typename TSource, typename TPredicate>
    struct SkipWhileAtEnumerator {
        TSource source;
        TPredicate predicate;
        bool yielding;
        int index;
        
        __device__ inline SkipWhileAtEnumerator(TSource src, TPredicate pred) : source(src), predicate(pred), yielding(false), index(0) {}
        
        __device__ inline bool MoveNext() {
            while (source.MoveNext()) {
                if (!yielding && predicate(source.Current(), index++)) {
                    continue;
                }
                yielding = true;
                return true;
            }
            return false;
        }
        __device__ inline auto Current() const { return source.Current(); }
    };

    struct StackLinq {
        template <typename T>
        __device__ static inline auto AsStackEnumerable(GSharpArray<T> source) {
            return ArrayEnumerator<T>(source);
        }

        // A query that is not fused runs in the calling thread, which gives each thread the aggregate of the whole
        // sequence, as AsParallel() promises
        template <typename TSource>
        __device__ static inline TSource AsParallel(TSource source) {
            return source;
        }

        template <typename T>
        __device__ static inline auto TakeLast(ArrayEnumerator<T> source, int count) {
            int n = __gsharp_clamp_count(count, source.Remaining());
            return ArrayEnumerator<T>(source.source, source.end - n, source.end);
        }

        template <typename T>
        __device__ static inline auto SkipLast(ArrayEnumerator<T> source, int count) {
            int n = __gsharp_clamp_count(count, source.Remaining());
            return ArrayEnumerator<T>(source.source, source.index + 1, source.end - n);
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline auto Where(TSource source, TPredicate predicate) {
            return WhereEnumerator<TSource, TPredicate>(source, predicate);
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline auto WhereAt(TSource source, TPredicate predicate) {
            return WhereAtEnumerator<TSource, TPredicate>(source, predicate);
        }
        
        template <typename TSource, typename TSelector>
        __device__ static inline auto Select(TSource source, TSelector selector) {
            return SelectEnumerator<TSource, TSelector>(source, selector);
        }

        template <typename TSource, typename TSelector>
        __device__ static inline auto SelectAt(TSource source, TSelector selector) {
            return SelectAtEnumerator<TSource, TSelector>(source, selector);
        }

        template <typename TSource>
        __device__ static inline auto Take(TSource source, int count) {
            return TakeEnumerator<TSource>(source, count);
        }

        template <typename TSource>
        __device__ static inline auto Skip(TSource source, int count) {
            return SkipEnumerator<TSource>(source, count);
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline auto TakeWhile(TSource source, TPredicate predicate) {
            return TakeWhileEnumerator<TSource, TPredicate>(source, predicate);
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline auto TakeWhileAt(TSource source, TPredicate predicate) {
            return TakeWhileAtEnumerator<TSource, TPredicate>(source, predicate);
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline auto SkipWhile(TSource source, TPredicate predicate) {
            return SkipWhileEnumerator<TSource, TPredicate>(source, predicate);
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline auto SkipWhileAt(TSource source, TPredicate predicate) {
            return SkipWhileAtEnumerator<TSource, TPredicate>(source, predicate);
        }
        
        template <typename TSource>
        __device__ static inline int Count(TSource source) {
            int count = 0;
            while (source.MoveNext()) count++;
            return count;
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline int Count(TSource source, TPredicate predicate) {
            int count = 0;
            while (source.MoveNext()) {
                if (predicate(source.Current())) count++;
            }
            return count;
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline int CountAt(TSource source, TPredicate predicate) {
            int count = 0;
            int index = 0;
            while (source.MoveNext()) {
                if (predicate(source.Current(), index++)) count++;
            }
            return count;
        }

        template <typename TSource>
        __device__ static inline bool Any(TSource source) {
            return source.MoveNext();
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline bool Any(TSource source, TPredicate predicate) {
            while (source.MoveNext()) {
                if (predicate(source.Current())) return true;
            }
            return false;
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline bool All(TSource source, TPredicate predicate) {
            while (source.MoveNext()) {
                if (!predicate(source.Current())) return false;
            }
            return true;
        }

        template <typename TSource, typename TValue>
        __device__ static inline bool Contains(TSource source, TValue value) {
            using TElement = decltype(source.Current());
            TElement target = value;
            while (source.MoveNext()) {
                if (__gsharp_linq_equals(source.Current(), target)) return true;
            }
            return false;
        }

        template <typename TSource>
        __device__ static inline auto ElementAt(TSource source, int index, uint32_t site_id = 0) {
            if (index >= 0) {
                int count = 0;
                while (source.MoveNext()) {
                    if (count == index) return source.Current();
                    count++;
                }
            }
            if (site_id != 0) __gsharp_trap(GSharpErrorCode::ArgumentOutOfRange, site_id);
            else __gsharp_trap(GSharpErrorCode::ArgumentOutOfRange, "Index was out of range.");
            return decltype(source.Current())();
        }

        template <typename TSource>
        __device__ static inline auto ElementAtOrDefault(TSource source, int index) {
            if (index >= 0) {
                int count = 0;
                while (source.MoveNext()) {
                    if (count == index) return source.Current();
                    count++;
                }
            }
            return decltype(source.Current())();
        }

        template <typename TSource>
        __device__ static inline auto First(TSource source, uint32_t site_id = 0) {
            if (source.MoveNext()) return source.Current();
            if (site_id != 0) __gsharp_trap(GSharpErrorCode::InvalidOperation, site_id);
            else __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains no elements");
            return decltype(source.Current())();
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline auto First(TSource source, TPredicate predicate, uint32_t site_id = 0) {
            while (source.MoveNext()) {
                auto val = source.Current();
                if (predicate(val)) return val;
            }
            if (site_id != 0) __gsharp_trap(GSharpErrorCode::InvalidOperation, site_id);
            else __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains no matching element");
            return decltype(source.Current())();
        }

        template <typename TSource>
        __device__ static inline auto FirstOrDefault(TSource source) {
            if (source.MoveNext()) return source.Current();
            return decltype(source.Current())();
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline auto FirstOrDefault(TSource source, TPredicate predicate) {
            while (source.MoveNext()) {
                auto val = source.Current();
                if (predicate(val)) return val;
            }
            return decltype(source.Current())();
        }

        template <typename TSource>
        __device__ static inline auto Single(TSource source, uint32_t site_empty = 0, uint32_t site_more = 0) {
            if (!source.MoveNext()) {
                if (site_empty != 0) __gsharp_trap(GSharpErrorCode::InvalidOperation, site_empty);
                else __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains no elements");
                return decltype(source.Current())();
            }
            auto result = source.Current();
            if (source.MoveNext()) {
                if (site_more != 0) __gsharp_trap(GSharpErrorCode::InvalidOperation, site_more);
                else __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains more than one element");
            }
            return result;
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline auto Single(TSource source, TPredicate predicate, uint32_t site_more = 0, uint32_t site_no_match = 0) {
            auto result = decltype(source.Current())();
            bool found = false;
            while (source.MoveNext()) {
                auto val = source.Current();
                if (predicate(val)) {
                    if (found) {
                        if (site_more != 0) __gsharp_trap(GSharpErrorCode::InvalidOperation, site_more);
                        else __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains more than one matching element");
                        return result;
                    }
                    result = val;
                    found = true;
                }
            }
            if (!found) {
                if (site_no_match != 0) __gsharp_trap(GSharpErrorCode::InvalidOperation, site_no_match);
                else __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains no matching element");
            }
            return result;
        }

        template <typename TSource>
        __device__ static inline auto SingleOrDefault(TSource source, uint32_t site_more = 0) {
            if (!source.MoveNext()) return decltype(source.Current())();
            auto result = source.Current();
            if (source.MoveNext()) {
                if (site_more != 0) __gsharp_trap(GSharpErrorCode::InvalidOperation, site_more);
                else __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains more than one element");
            }
            return result;
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline auto SingleOrDefault(TSource source, TPredicate predicate, uint32_t site_more = 0) {
            auto result = decltype(source.Current())();
            bool found = false;
            while (source.MoveNext()) {
                auto val = source.Current();
                if (predicate(val)) {
                    if (found) {
                        if (site_more != 0) __gsharp_trap(GSharpErrorCode::InvalidOperation, site_more);
                        else __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains more than one matching element");
                        return result;
                    }
                    result = val;
                    found = true;
                }
            }
            return result;
        }

        template <typename TSource>
        __device__ static inline auto Last(TSource source, uint32_t site_id = 0) {
            auto result = decltype(source.Current())();
            bool found = false;
            while (source.MoveNext()) {
                result = source.Current();
                found = true;
            }
            if (!found) {
                if (site_id != 0) __gsharp_trap(GSharpErrorCode::InvalidOperation, site_id);
                else __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains no elements");
            }
            return result;
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline auto Last(TSource source, TPredicate predicate, uint32_t site_id = 0) {
            auto result = decltype(source.Current())();
            bool found = false;
            while (source.MoveNext()) {
                auto val = source.Current();
                if (predicate(val)) {
                    result = val;
                    found = true;
                }
            }
            if (!found) {
                if (site_id != 0) __gsharp_trap(GSharpErrorCode::InvalidOperation, site_id);
                else __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains no matching element");
            }
            return result;
        }

        template <typename TSource>
        __device__ static inline auto LastOrDefault(TSource source) {
            auto result = decltype(source.Current())();
            while (source.MoveNext()) {
                result = source.Current();
            }
            return result;
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline auto LastOrDefault(TSource source, TPredicate predicate) {
            auto result = decltype(source.Current())();
            while (source.MoveNext()) {
                auto val = source.Current();
                if (predicate(val)) result = val;
            }
            return result;
        }

        template <typename TSource>
        __device__ static inline auto Sum(TSource source) {
            using TElement = decltype(source.Current());
            typedef __gsharp_linq_sum_ops<TElement> Ops;
            typename Ops::acc sum = Ops::Zero();
            while (source.MoveNext()) {
                sum = Ops::Add(sum, source.Current());
            }
            return Ops::Result(sum);
        }

        // Sum(selector) is Select(selector).Sum(), as in C#
        template <typename TSource, typename TSelector>
        __device__ static inline auto Sum(TSource source, TSelector selector) {
            return Sum(SelectEnumerator<TSource, TSelector>(source, selector));
        }

        // SumSequential adds in the order of the elements, which is how Sum adds in one thread
        template <typename TSource>
        __device__ static inline auto SumSequential(TSource source) {
            return Sum(source);
        }

        template <typename TSource, typename TSelector>
        __device__ static inline auto SumSequential(TSource source, TSelector selector) {
            return Sum(SelectEnumerator<TSource, TSelector>(source, selector));
        }

        template <typename TSource, typename TFunc>
        __device__ static inline auto Aggregate(TSource source, TFunc func, uint32_t site_id = 0) {
            if (!source.MoveNext()) {
                if (site_id != 0) __gsharp_trap(GSharpErrorCode::InvalidOperation, site_id);
                else __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains no elements");
                return decltype(source.Current())();
            }
            auto result = source.Current();
            while (source.MoveNext()) {
                result = func(result, source.Current());
            }
            return result;
        }

        template <typename TSource, typename TAccumulate, typename TFunc>
        __device__ static inline TAccumulate Aggregate(TSource source, TAccumulate seed, TFunc func) {
            TAccumulate result = seed;
            while (source.MoveNext()) {
                result = func(result, source.Current());
            }
            return result;
        }

        template <typename TSource>
        __device__ static inline auto Max(TSource source, uint32_t site_id = 0) {
            if (!source.MoveNext()) {
                if (site_id != 0) __gsharp_trap(GSharpErrorCode::InvalidOperation, site_id);
                else __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains no elements");
                return decltype(source.Current())();
            }
            auto max_val = source.Current();
            // As System.Linq: NaN is skipped, and is the result only when every element is NaN
            while (__gsharp_linq_is_nan(max_val)) {
                if (!source.MoveNext()) return max_val;
                max_val = source.Current();
            }
            while (source.MoveNext()) {
                auto val = source.Current();
                if (val > max_val) max_val = val;
            }
            return max_val;
        }

        template <typename TSource>
        __device__ static inline auto Min(TSource source, uint32_t site_id = 0) {
            if (!source.MoveNext()) {
                if (site_id != 0) __gsharp_trap(GSharpErrorCode::InvalidOperation, site_id);
                else __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains no elements");
                return decltype(source.Current())();
            }
            auto min_val = source.Current();
            // As System.Linq: the first NaN is the result, and the elements after it are not read
            if (__gsharp_linq_is_nan(min_val)) return min_val;
            while (source.MoveNext()) {
                auto val = source.Current();
                if (val < min_val) min_val = val;
                else if (__gsharp_linq_is_nan(val)) return val;
            }
            return min_val;
        }

    private:
        template <typename T>
        __device__ static inline double avg_cast(T, double val) { return val; }
        __device__ static inline float avg_cast(float, double val) { return (float)val; }

    public:
        template <typename TSource>
        __device__ static inline auto Average(TSource source, uint32_t site_id = 0) {
            using TElement = decltype(source.Current());
            double sum = 0;
            int count = 0;
            while (source.MoveNext()) {
                sum += (double)source.Current();
                count++;
            }
            if (count == 0) {
                if (site_id != 0) __gsharp_trap(GSharpErrorCode::InvalidOperation, site_id);
                else __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains no elements");
                return avg_cast(TElement{}, 0.0);
            }
            return avg_cast(TElement{}, sum / count);
        }
    };
}}}
