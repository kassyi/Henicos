#include <cuda_runtime.h>
__global__ void MyTestKernel(int* data) { }
__global__   void   AnotherKernel () { }
__device__ void Helper() { } // Should be ignored
