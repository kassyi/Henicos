#pragma once

namespace Kassyi { namespace ZeroAlloc { namespace Linq {
    
    template <typename T>
    struct ArrayEnumerator {
        GSharpArray<T> source;
        int index;
        
        __device__ inline ArrayEnumerator(GSharpArray<T> src) : source(src), index(-1) {}
        
        __device__ inline bool MoveNext() {
            if (index + 1 < source.length) {
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
        
        __device__ inline SelectEnumerator(TSource src, TSelector sel) : source(src), selector(sel) {}
        
        __device__ inline bool MoveNext() { return source.MoveNext(); }
        __device__ inline auto Current() const { return selector(source.Current()); }
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
        
        __device__ inline SelectAtEnumerator(TSource src, TSelector sel) : source(src), selector(sel), index(-1) {}
        
        __device__ inline bool MoveNext() {
            if (source.MoveNext()) {
                index++;
                return true;
            }
            return false;
        }
        __device__ inline auto Current() const { return selector(source.Current(), index); }
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
            while (source.MoveNext()) {
                if (source.Current() == value) return true;
            }
            return false;
        }

        template <typename TSource>
        __device__ static inline auto ElementAt(TSource source, int index) {
            if (index >= 0) {
                int count = 0;
                while (source.MoveNext()) {
                    if (count == index) return source.Current();
                    count++;
                }
            }
            __gsharp_trap(GSharpErrorCode::ArgumentOutOfRange, "Index was out of range.");
            return decltype(source.Current())();
        }

        template <typename TSource>
        __device__ static inline auto ElementAtOrDefault(TSource source, int index) {
            int count = 0;
            while (source.MoveNext()) {
                if (count == index) return source.Current();
                count++;
            }
            return decltype(source.Current())();
        }

        template <typename TSource>
        __device__ static inline auto First(TSource source) {
            if (source.MoveNext()) return source.Current();
            __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains no elements");
            return decltype(source.Current())();
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline auto First(TSource source, TPredicate predicate) {
            while (source.MoveNext()) {
                if (predicate(source.Current())) return source.Current();
            }
            __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains no matching element");
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
                if (predicate(source.Current())) return source.Current();
            }
            return decltype(source.Current())();
        }

        template <typename TSource>
        __device__ static inline auto Single(TSource source) {
            if (!source.MoveNext()) {
                __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains no elements");
                return decltype(source.Current())();
            }
            auto result = source.Current();
            if (source.MoveNext()) {
                __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains more than one element");
            }
            return result;
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline auto Single(TSource source, TPredicate predicate) {
            auto result = decltype(source.Current())();
            bool found = false;
            while (source.MoveNext()) {
                if (predicate(source.Current())) {
                    if (found) {
                        __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains more than one matching element");
                        return result;
                    }
                    result = source.Current();
                    found = true;
                }
            }
            if (!found) {
                __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains no matching element");
            }
            return result;
        }

        template <typename TSource>
        __device__ static inline auto SingleOrDefault(TSource source) {
            if (!source.MoveNext()) return decltype(source.Current())();
            auto result = source.Current();
            if (source.MoveNext()) {
                __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains more than one element");
            }
            return result;
        }

        template <typename TSource, typename TPredicate>
        __device__ static inline auto SingleOrDefault(TSource source, TPredicate predicate) {
            auto result = decltype(source.Current())();
            bool found = false;
            while (source.MoveNext()) {
                if (predicate(source.Current())) {
                    if (found) {
                        __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains more than one matching element");
                        return result;
                    }
                    result = source.Current();
                    found = true;
                }
            }
            return result;
        }

        template <typename TSource>
        __device__ static inline auto Last(TSource source) {
            auto result = decltype(source.Current())();
            bool found = false;
            while (source.MoveNext()) {
                result = source.Current();
                found = true;
            }
            if (!found) {
                __gsharp_trap(GSharpErrorCode::InvalidOperation, "Sequence contains no elements");
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

        template <typename TSource>
        __device__ static inline auto Sum(TSource source) {
            decltype(source.Current()) sum = 0;
            while (source.MoveNext()) {
                sum += source.Current();
            }
            return sum;
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
        __device__ static inline auto Max(TSource source) {
            if (!source.MoveNext()) return decltype(source.Current())();
            auto max_val = source.Current();
            while (source.MoveNext()) {
                if (source.Current() > max_val) max_val = source.Current();
            }
            return max_val;
        }

        template <typename TSource>
        __device__ static inline auto Min(TSource source) {
            if (!source.MoveNext()) return decltype(source.Current())();
            auto min_val = source.Current();
            while (source.MoveNext()) {
                if (source.Current() < min_val) min_val = source.Current();
            }
            return min_val;
        }

        template <typename TSource>
        __device__ static inline auto Average(TSource source) {
            double sum = 0;
            int count = 0;
            while (source.MoveNext()) {
                sum += (double)source.Current();
                count++;
            }
            return count > 0 ? (sum / count) : 0;
        }
    };
}}}
