#include <cuda_runtime.h>
#define KERNEL_DECL __global__ void DefinedInMacro()
#define CONTINUED_DECL \
    __global__ void DefinedInContinuedMacro()

extern "C" __global__ void __launch_bounds__(256) BoundsOne(int* data) { }
extern "C" __global__ void __launch_bounds__(256, 2) BoundsTwo(int* data) { }
extern "C" __global__ void
__launch_bounds__(
    (128 * 2) /* threads */,
    2 // blocks
)
SplitAcrossLines(int* data) { }
extern "C" __global__ void __noinline__ NoInline(int* data) { }
extern "C" __global__ __launch_bounds__(64) void BoundsBeforeVoid(int* data) { }
extern "C" __global__ void __maxnreg__(32) __cluster_dims__(2, 1, 1) OtherAttributes(int* data) { }
extern "C" __global__ void __attribute__((used)) GnuAttribute(int* data) { }
extern "C" __global__ void /* comment */ CommentBeforeName(int* data) { }

// __global__ void InLineComment(int* data) { }
/* __global__ void InBlockComment(int* data) { } */
__device__ const char* s_text = "__global__ void InString()";
__device__ const char* s_raw = R"x(__global__ void InRawString() )" __global__ void AfterFakeEnd())x";
__device__ const char s_quote = '"'; __device__ const char* s_after = "__global__ void InSecondString()";
template <typename T> __global__ void __launch_bounds__(128) Templated(T* data) { }
extern "C" [[maybe_unused]] __global__ void [[deprecated("old")]] StandardAttribute(int* data) { }
