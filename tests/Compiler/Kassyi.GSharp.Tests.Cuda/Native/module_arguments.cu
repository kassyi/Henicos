// Hand-written kernels launched through the YAML module binding Native/module_arguments.yml (ModuleArgumentTests).

static __device__ unsigned long long GlobalTimerNanoseconds()
{
    unsigned long long t;
    asm volatile("mov.u64 %0, %%globaltimer;" : "=l"(t));
    return t;
}

// Stores the address received for data in slots[0], page-locked host memory that the test reads while the kernel
// runs, and waits until the test sets slots[1] (at most 500 ms, below the Windows display driver timeout), so the
// device buffer behind the address is still allocated when the test asks the driver what kind of memory it is. When it
// stops waiting without the answer, it sets slots[2], so the test can tell a late query from a host address.
// The kernel never reads through data: a launcher that passes a host address cannot fault the device.
extern "C" __global__ void RecordPointer(float* data, unsigned long long slots)
{
    volatile unsigned long long* s = (volatile unsigned long long*)slots;
    s[0] = (unsigned long long)data;
    __threadfence_system();
    unsigned long long start = GlobalTimerNanoseconds();
    while (s[1] == 0ull)
    {
        if (GlobalTimerNanoseconds() - start >= 500000000ull)
        {
            s[2] = 1ull;
            __threadfence_system();
            return;
        }
    }
}

// data[i] = data[i] * factor + i for i < n, so a missing copy-in, copy-back or a shifted element shows in the result.
extern "C" __global__ void Scale(float* data, int n, float factor)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n)
    {
        data[i] = data[i] * factor + (float)i;
    }
}

// seen[0] = the address received for data, so a test can tell a null pointer from a device buffer. Never reads data.
extern "C" __global__ void RecordAddress(float* data, unsigned long long* seen)
{
    seen[0] = (unsigned long long)data;
}

// The 'ref' total becomes total * 3 + seed, the 'out' result becomes its value at the launch (default) + seed + 7, and
// the 'in' seed is overwritten on the device, which must not reach the caller.
static __device__ void UpdateValues(int* total, int* seed, int* result)
{
    int s = *seed;
    *total = *total * 3 + s;
    *result = *result + s + 7;
    *seed = -1;
}

extern "C" __global__ void UpdateByReference(int* total, int* seed, int* result)
{
    UpdateValues(total, seed, result);
}

// The same update in a kernel limited to 32 threads per block: the driver rejects a launch of more threads after the
// launcher has staged the arguments, so a test can tell what the launcher wrote to the caller before the launch.
extern "C" __global__ void __launch_bounds__(32) UpdateByReferenceBounded(int* total, int* seed, int* result)
{
    UpdateValues(total, seed, result);
}

// x[r * cols + c] += r - c for a row-major rows x cols matrix: the result differs between rows and columns, so a
// transposed or column-major copy shows in it.
extern "C" __global__ void ShiftMatrix(float* x, int rows, int cols)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < rows * cols)
    {
        int r = i / cols;
        int c = i % cols;
        x[i] += (float)(r - c);
    }
}

// *a += 1, then *b += 10: when both point to one variable, it ends 11 higher, as in C#; two copies give 10.
extern "C" __global__ void AddTwo(int* a, int* b)
{
    *a += 1;
    *b += 10;
}

// The same on the first element of two arrays, which a caller may pass as one array.
extern "C" __global__ void AddToFirstOfTwo(float* a, float* b)
{
    a[0] += 1.0f;
    b[0] += 10.0f;
}
