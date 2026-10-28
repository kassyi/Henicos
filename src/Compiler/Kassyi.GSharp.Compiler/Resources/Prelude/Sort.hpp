// --- MemoryExtensions.Sort (CPU and CUDA dialects) ---
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// (The sort below is ported from dotnet/runtime; see THIRD-PARTY-NOTICES.txt at the root of the G# repository.)
//
// [WHY] A port of the introsort of .NET, so that every dialect puts equal keys (and their items) in the same order as the
// managed path, which runs .NET itself. The algorithm is the one of dotnet/runtime
// src/libraries/System.Private.CoreLib/src/System/Collections/Generic/ArraySortHelper.cs at commit
// e750854de9ad6a2ced72feb9b8008e506f04608f. A comparer struct is copied once and that one copy is used for the whole
// sort, as .NET 10 does (it boxes the comparer once). The sort runs in the calling thread, in place, and allocates nothing.
// The recursion of IntroSort on the right part is an explicit stack that keeps the order of .NET: the right part is sorted
// first, then the left part (depth at most 2 * (log2(int.MaxValue) + 1) = 62 entries).

// The items sorted with the keys: none, or a span that receives every move of the keys.
struct __gsharp_sort_no_items
{
    typedef char Value;
    __device__ __forceinline__ __gsharp_sort_no_items offset(int) const { return *this; }
    __device__ __forceinline__ Value get(int) const { return 0; }
    __device__ __forceinline__ void set(int, Value) const {}
    __device__ __forceinline__ void swap(int, int) const {}
};

template <typename TValue>
struct __gsharp_sort_items
{
    typedef TValue Value;
    TValue* data;
    __device__ __forceinline__ __gsharp_sort_items offset(int start) const { return __gsharp_sort_items{data + start}; }
    __device__ __forceinline__ Value get(int i) const { return data[i]; }
    __device__ __forceinline__ void set(int i, Value v) const { data[i] = v; }
    __device__ __forceinline__ void swap(int i, int j) const { TValue t = data[i]; data[i] = data[j]; data[j] = t; }
};

// The default order of a primitive key (IComparable<T> of a number, char, bool or enum).
template <typename T>
struct __gsharp_sort_default_compare
{
    __device__ __forceinline__ int operator()(const T& a, const T& b) const { return a < b ? -1 : (b < a ? 1 : 0); }
};

// A comparison written by the user (a comparison, a comparer struct, CompareTo): a trap inside it is recorded as the
// exception .NET wraps it in (__gsharp_sort_compare_scope, ErrorChannel.hpp on the CPU dialects)
template <typename TCompare>
struct __gsharp_sort_user_compare
{
    TCompare compare;
    template <typename T>
    __device__ __forceinline__ int operator()(T& a, T& b)
    {
        __gsharp_sort_compare_scope scope;
        return compare(a, b);
    }
};

template <typename T> __device__ __forceinline__ bool __gsharp_sort_is_nan(const T&) { return false; }
__device__ __forceinline__ bool __gsharp_sort_is_nan(const float& v) { return v != v; }
__device__ __forceinline__ bool __gsharp_sort_is_nan(const double& v) { return v != v; }

__device__ __forceinline__ int __gsharp_sort_log2(unsigned int v)
{
    int r = 0;
    while (v >>= 1) { r++; }
    return r;
}

// SortUtils.MoveNansToFront: the number of NaN keys, moved (with their items) to the front in the order of .NET
template <typename T, typename TItems>
__device__ int __gsharp_sort_move_nans_to_front(T* keys, TItems items, int length)
{
    int left = 0;
    for (int i = 0; i < length; i++)
    {
        if (__gsharp_sort_is_nan(keys[i]))
        {
            T temp = keys[left];
            keys[left] = keys[i];
            keys[i] = temp;
            items.swap(left, i);
            left++;
        }
    }
    return left;
}

template <typename T, typename TItems, typename TCompare>
__device__ __forceinline__ void __gsharp_sort_swap_if_greater(T* keys, TItems items, TCompare& compare, int i, int j)
{
    if (compare(keys[i], keys[j]) > 0)
    {
        T key = keys[i];
        keys[i] = keys[j];
        keys[j] = key;
        items.swap(i, j);
    }
}

template <typename T, typename TItems>
__device__ __forceinline__ void __gsharp_sort_swap(T* keys, TItems items, int i, int j)
{
    T key = keys[i];
    keys[i] = keys[j];
    keys[j] = key;
    items.swap(i, j);
}

template <typename T, typename TItems, typename TCompare>
__device__ void __gsharp_sort_insertion(T* keys, TItems items, int length, TCompare& compare)
{
    for (int i = 0; i < length - 1; i++)
    {
        T t = keys[i + 1];
        typename TItems::Value tValue = items.get(i + 1);
        int j = i;
        while (j >= 0 && compare(t, keys[j]) < 0)
        {
            keys[j + 1] = keys[j];
            items.set(j + 1, items.get(j));
            j--;
        }
        keys[j + 1] = t;
        items.set(j + 1, tValue);
    }
}

template <typename T, typename TItems, typename TCompare>
__device__ void __gsharp_sort_down_heap(T* keys, TItems items, int i, int n, TCompare& compare)
{
    T d = keys[i - 1];
    typename TItems::Value dValue = items.get(i - 1);
    while (i <= n >> 1)
    {
        int child = 2 * i;
        if (child < n && compare(keys[child - 1], keys[child]) < 0)
        {
            child++;
        }
        if (!(compare(d, keys[child - 1]) < 0))
        {
            break;
        }
        keys[i - 1] = keys[child - 1];
        items.set(i - 1, items.get(child - 1));
        i = child;
    }
    keys[i - 1] = d;
    items.set(i - 1, dValue);
}

template <typename T, typename TItems, typename TCompare>
__device__ void __gsharp_sort_heap(T* keys, TItems items, int n, TCompare& compare)
{
    for (int i = n >> 1; i >= 1; i--)
    {
        __gsharp_sort_down_heap(keys, items, i, n, compare);
    }
    for (int i = n; i > 1; i--)
    {
        __gsharp_sort_swap(keys, items, 0, i - 1);
        __gsharp_sort_down_heap(keys, items, 1, i - 1, compare);
    }
}

__device__ __forceinline__ void __gsharp_sort_bad_comparer()
{
    __gsharp_trap(GSharpErrorCode::Argument,
        "Unable to sort because the IComparer.Compare() method returns inconsistent results.");
}

// How the scans of PickPivotAndPartition stop, which differs between the three .NET helpers:
// - Guarded: the default order of keys only (GenericArraySortHelper<T>). The scans stop at the ends of the partition and
//   compare the pivot first on the left (pivot > key).
// - Default: the default order of keys and items (GenericArraySortHelper<TKey, TValue>). No stop at the ends; the pivot
//   comes first on the left as well.
// - Comparer: a comparer or a comparison (ArraySortHelper and ArraySortHelperForTComparer). No stop at the ends; the key
//   comes first on the left (key < pivot).
// Without a stop, an inconsistent order runs a scan past the partition, which .NET reports as ArgumentException.
enum class __gsharp_sort_scan { Guarded, Default, Comparer };

// PickPivotAndPartition; -1 when an inconsistent order ran a scan past the partition.
template <__gsharp_sort_scan Scan, typename T, typename TItems, typename TCompare>
__device__ int __gsharp_sort_partition(T* keys, TItems items, int length, TCompare& compare)
{
    int hi = length - 1;
    int middle = hi >> 1;
    __gsharp_sort_swap_if_greater(keys, items, compare, 0, middle);
    __gsharp_sort_swap_if_greater(keys, items, compare, 0, hi);
    __gsharp_sort_swap_if_greater(keys, items, compare, middle, hi);

    T pivot = keys[middle];
    __gsharp_sort_swap(keys, items, middle, hi - 1);
    int left = 0;
    int right = hi - 1;
    while (left < right)
    {
        if (Scan == __gsharp_sort_scan::Guarded)
        {
            while (left < hi - 1 && compare(pivot, keys[++left]) > 0) {}
            while (right > 0 && compare(pivot, keys[--right]) < 0) {}
        }
        else
        {
            for (;;)
            {
                if (++left >= length) { __gsharp_sort_bad_comparer(); return -1; }
                bool before = Scan == __gsharp_sort_scan::Default ? compare(pivot, keys[left]) > 0 : compare(keys[left], pivot) < 0;
                if (!before) { break; }
            }
            for (;;)
            {
                if (--right < 0) { __gsharp_sort_bad_comparer(); return -1; }
                if (!(compare(pivot, keys[right]) < 0)) { break; }
            }
        }
        if (left >= right)
        {
            break;
        }
        __gsharp_sort_swap(keys, items, left, right);
    }
    if (left != hi - 1)
    {
        __gsharp_sort_swap(keys, items, left, hi - 1);
    }
    return left;
}

// IntrospectiveSort + IntroSort (Array.IntrosortSizeThreshold = 16)
template <__gsharp_sort_scan Scan, typename T, typename TItems, typename TCompare>
__device__ void __gsharp_sort_intro(T* keys, TItems items, int length, TCompare& compare)
{
    if (length <= 1)
    {
        return;
    }

    struct Pending { int start; int size; int depth; };
    Pending pending[64];
    int count = 0;
    int start = 0;
    int size = length;
    int depth = 2 * (__gsharp_sort_log2((unsigned int)length) + 1);
    for (;;)
    {
        while (size > 1)
        {
            T* k = keys + start;
            TItems it = items.offset(start);
            if (size <= 16)
            {
                if (size == 2)
                {
                    __gsharp_sort_swap_if_greater(k, it, compare, 0, 1);
                }
                else if (size == 3)
                {
                    __gsharp_sort_swap_if_greater(k, it, compare, 0, 1);
                    __gsharp_sort_swap_if_greater(k, it, compare, 0, 2);
                    __gsharp_sort_swap_if_greater(k, it, compare, 1, 2);
                }
                else
                {
                    __gsharp_sort_insertion(k, it, size, compare);
                }
                break;
            }
            if (depth == 0)
            {
                __gsharp_sort_heap(k, it, size, compare);
                break;
            }
            depth--;
            int p = __gsharp_sort_partition<Scan>(k, it, size, compare);
            if (p < 0)
            {
                return;
            }
            // The left part [start, start + p) waits until the right part (start + p, start + size) is sorted
            pending[count].start = start;
            pending[count].size = p;
            pending[count].depth = depth;
            count++;
            start += p + 1;
            size -= p + 1;
        }
        if (count == 0)
        {
            return;
        }
        count--;
        start = pending[count].start;
        size = pending[count].size;
        depth = pending[count].depth;
    }
}

// The default order: NaN keys first (SortUtils.MoveNansToFront), then the rest by the default comparison
template <__gsharp_sort_scan Scan, typename T, typename TItems>
__device__ void __gsharp_sort_default(T* keys, TItems items, int length)
{
    if (length <= 1)
    {
        return;
    }
    int nanLeft = __gsharp_sort_move_nans_to_front(keys, items, length);
    if (nanLeft == length)
    {
        return;
    }
    __gsharp_sort_default_compare<T> compare;
    __gsharp_sort_intro<Scan>(keys + nanLeft, items.offset(nanLeft), length - nanLeft, compare);
}

// A span argument: a GSharpArray, or the C++ array of a stackalloc span
template <typename T> __device__ __forceinline__ GSharpArray<T> __gsharp_sort_span(GSharpArray<T> span) { return span; }
template <typename T, int N> __device__ __forceinline__ GSharpArray<T> __gsharp_sort_span(T (&span)[N]) { return GSharpArray<T>(span, N); }

template <typename TValue>
__device__ __forceinline__ __gsharp_sort_items<TValue> __gsharp_sort_items_of(GSharpArray<TValue> span) { return __gsharp_sort_items<TValue>{span.data}; }

template <typename TKey, typename TValue>
__device__ __forceinline__ bool __gsharp_sort_same_length(const GSharpArray<TKey>& keys, const GSharpArray<TValue>& items)
{
    if (keys.length != items.length)
    {
        __gsharp_trap(GSharpErrorCode::Argument, "Length of items must be same as length of keys.");
        return false;
    }
    return true;
}

// span.Sort()
template <typename TKeys>
__device__ void __gsharp_sort(TKeys&& keySpan)
{
    auto keys = __gsharp_sort_span(keySpan);
    __gsharp_sort_default<__gsharp_sort_scan::Guarded>(keys.data, __gsharp_sort_no_items{}, keys.length);
}

// span.Sort(comparison) or span.Sort(comparer): a lambda, a device function, or a lambda that calls the comparer struct
template <typename TKeys, typename TCompare>
__device__ void __gsharp_sort(TKeys&& keySpan, TCompare comparison)
{
    auto keys = __gsharp_sort_span(keySpan);
    __gsharp_sort_user_compare<TCompare> compare{comparison};
    __gsharp_sort_intro<__gsharp_sort_scan::Comparer>(keys.data, __gsharp_sort_no_items{}, keys.length, compare);
}

// span.Sort() of a user struct: a lambda that calls its IComparable<T>.CompareTo
template <typename TKeys, typename TCompare>
__device__ void __gsharp_sort_comparable(TKeys&& keySpan, TCompare comparison)
{
    auto keys = __gsharp_sort_span(keySpan);
    __gsharp_sort_user_compare<TCompare> compare{comparison};
    __gsharp_sort_intro<__gsharp_sort_scan::Guarded>(keys.data, __gsharp_sort_no_items{}, keys.length, compare);
}

// MemoryExtensions.Sort(keys, items)
template <typename TKeys, typename TItems>
__device__ void __gsharp_sort_pairs(TKeys&& keySpan, TItems&& itemSpan)
{
    auto keys = __gsharp_sort_span(keySpan);
    auto items = __gsharp_sort_span(itemSpan);
    if (__gsharp_sort_same_length(keys, items))
    {
        __gsharp_sort_default<__gsharp_sort_scan::Default>(keys.data, __gsharp_sort_items_of(items), keys.length);
    }
}

// MemoryExtensions.Sort(keys, items, comparison) or Sort(keys, items, comparer)
template <typename TKeys, typename TItems, typename TCompare>
__device__ void __gsharp_sort_pairs(TKeys&& keySpan, TItems&& itemSpan, TCompare comparison)
{
    auto keys = __gsharp_sort_span(keySpan);
    auto items = __gsharp_sort_span(itemSpan);
    if (__gsharp_sort_same_length(keys, items))
    {
        __gsharp_sort_user_compare<TCompare> compare{comparison};
        __gsharp_sort_intro<__gsharp_sort_scan::Comparer>(keys.data, __gsharp_sort_items_of(items), keys.length, compare);
    }
}

// MemoryExtensions.Sort(keys, items) of user struct keys: a lambda that calls their IComparable<T>.CompareTo
template <typename TKeys, typename TItems, typename TCompare>
__device__ void __gsharp_sort_pairs_comparable(TKeys&& keySpan, TItems&& itemSpan, TCompare comparison)
{
    auto keys = __gsharp_sort_span(keySpan);
    auto items = __gsharp_sort_span(itemSpan);
    if (__gsharp_sort_same_length(keys, items))
    {
        __gsharp_sort_user_compare<TCompare> compare{comparison};
        __gsharp_sort_intro<__gsharp_sort_scan::Default>(keys.data, __gsharp_sort_items_of(items), keys.length, compare);
    }
}
