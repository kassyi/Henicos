extern "C" __global__ void MandelbrotKernel_Native(int* output, int width, int height, int max_iter, float x_min, float x_max, float y_min, float y_max)
{
    int col = blockIdx.x * blockDim.x + threadIdx.x;
    int row = blockIdx.y * blockDim.y + threadIdx.y;

    if (col < width && row < height)
    {
        float x0 = x_min + (x_max - x_min) * col / width;
        float y0 = y_min + (y_max - y_min) * row / height;
        float x = 0.0f;
        float y = 0.0f;
        int iter = 0;

        while (x * x + y * y <= 4.0f && iter < max_iter)
        {
            float xtemp = x * x - y * y + x0;
            y = 2.0f * x * y + y0;
            x = xtemp;
            iter++;
        }

        output[row * width + col] = iter;
    }
}
