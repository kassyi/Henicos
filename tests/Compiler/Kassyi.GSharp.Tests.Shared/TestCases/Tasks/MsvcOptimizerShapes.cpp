// Input of MsvcOptimizerShapeTests: integer shapes that MSVC's global optimizer (/Og, part of /O2) miscompiles
// (#1078), written as G# emits them. Each static_assert gives the value that C++ defines; the test calls the exported
// functions with the same input and fails on any other value.
#include <climits>

#define GSHARP_SHAPE extern "C" __declspec(dllexport) __declspec(noinline)

constexpr int AddWrap(int a, int b) { return (int)((unsigned int)a + (unsigned int)b); }
constexpr int SubWrap(int a, int b) { return (int)((unsigned int)a - (unsigned int)b); }
constexpr int NegateWrap(int a) { return (int)(0u - (unsigned int)a); }

// A wrapping negation used as a dividend: /Og rewrites it as -(v / 2), wrong for INT_MIN
constexpr long long NegateThenDivide(int v) { return (long long)(NegateWrap(v) / 2); }
static_assert(NegateThenDivide(INT_MIN) == -0x40000000LL, "-int.MinValue / 2");
GSHARP_SHAPE long long GSharpNegateThenDivide(int v) { return NegateThenDivide(v); }

// One short widened through unsigned int and directly to unsigned long long: /Og reuses the wrong sign extension
constexpr unsigned long long ShortWidenedTwice(const int* src, int i)
{
    short s = (short)src[i];
    unsigned long long h = 14695981039346656037ull;
    h = (h ^ ((unsigned long long)(unsigned int)s + 1ull)) * 1099511628211ull;
    h = (h ^ ((unsigned long long)s + 4ull)) * 1099511628211ull;
    return h;
}
// The short is read from an array, as in a kernel: taken as a parameter, it is compiled correctly
constexpr int MinusOne[] = {0, -1};
static_assert(ShortWidenedTwice(MinusOne, 1) == 0x08356830B4EB6AD4ull, "fnv((ulong)(uint)s, (ulong)s)");
GSHARP_SHAPE unsigned long long GSharpShortWidenedTwice(const int* src, int i) { return ShortWidenedTwice(src, i); }

// A wrapping short increment whose value picks a pointer target: /Og drops the conversion back to short
constexpr int ShortIncrementPicksPointer(int input)
{
    short s = (short)input;
    s = (short)AddWrap(s, 1);
    int x = 10;
    int y = 20;
    int* target = s <= 0 ? &x : &y;
    *target = 0;
    return s;
}
static_assert(ShortIncrementPicksPointer(SHRT_MAX) == SHRT_MIN, "(short)(short.MaxValue + 1)");
GSHARP_SHAPE int GSharpShortIncrementPicksPointer(int input) { return ShortIncrementPicksPointer(input); }

// An sbyte compound subtraction whose wrapped value picks a pointer target: /Og compares the value before the
// conversion back to signed char. The same comparison in an if that assigns x or y is compiled correctly
constexpr int SByteSubtractPicksPointer(int input)
{
    signed char v = (signed char)input;
    v = (signed char)SubWrap(v, (signed char)127);
    int x = 10;
    int y = 20;
    int* target = (signed char)(v ^ (signed char)(-127)) < v ? &x : &y;
    *target = 0;
    return x * 100 + y;
}
static_assert(SByteSubtractPicksPointer(-2) == 20, "(sbyte)(-2 - 127) is 127, and (127 ^ -127) is -2 < 127");
GSHARP_SHAPE int GSharpSByteSubtractPicksPointer(int input) { return SByteSubtractPicksPointer(input); }
