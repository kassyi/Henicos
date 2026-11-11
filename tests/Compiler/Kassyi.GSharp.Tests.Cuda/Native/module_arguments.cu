// Hand-written kernels launched through the YAML module binding Native/module_arguments.yml (ModuleArgumentTests).

static __device__ unsigned long long GlobalTimerNanoseconds()
{
    unsigned long long t;
    asm volatile("mov.u64 %0, %%globaltimer;" : "=l"(t));
    return t;
}

// Stores the address received for data in slots[0], page-locked host memory that the test reads while the kernel
// runs, and waits until the test sets slots[1] (at most 500 ms, below the Windows display driver timeout), so the
// device buffer behind the address is still allocated when the test asks the driver what kind of memory it is.
// The kernel never reads through data: a launcher that passes a host address cannot fault the device.
extern "C" __global__ void RecordPointer(float* data, unsigned long long slots)
{
    volatile unsigned long long* s = (volatile unsigned long long*)slots;
    s[0] = (unsigned long long)data;
    __threadfence_system();
    unsigned long long start = GlobalTimerNanoseconds();
    while (s[1] == 0ull && GlobalTimerNanoseconds() - start < 500000000ull)
    {
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
