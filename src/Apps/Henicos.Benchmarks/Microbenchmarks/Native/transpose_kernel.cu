extern "C" __global__ void TransposeKernel_Native(float* input, float* output, int width, int height)
{
    int col = blockIdx.x * blockDim.x + threadIdx.x;
    int row = blockIdx.y * blockDim.y + threadIdx.y;

    if (col < width && row < height)
    {
        output[col * height + row] = input[row * width + col];
    }
}
