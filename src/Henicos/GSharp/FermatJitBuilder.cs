using System;
using System.Text;

namespace Kassyi.GSharp
{
    public static class FermatJitBuilder
    {
        public static string BuildKernel(int E)
        {
            var sb = new StringBuilder();
            sb.AppendLine(@"#define CCCL_IGNORE_MSVC_TRADITIONAL_PREPROCESSOR_WARNING");
            sb.AppendLine(@"#define CCCL_IGNORE_DEPRECATED_CPP_DIALECT");
            sb.AppendLine(@"#include <cooperative_groups.h>");
            // We assume the include path is passed to nvcc
            sb.AppendLine(@"#include ""GpuBigInt_Core.cuh""");
            sb.AppendLine(@"#include ""GpuBigInt_Kernels.cuh""");
            sb.AppendLine(@"#include ""GpuBigInt_StaticLoop.cuh""");
            sb.AppendLine(@"namespace cg = cooperative_groups;");
            
            // Build the JIT Kernel
            sb.AppendLine(@"__global__ void Kernel_JitFermatLoop(");
            sb.AppendLine(@"    unsigned int* T, unsigned int* Q, unsigned int* R, unsigned int* MaskR,");
            sb.AppendLine(@"    unsigned int* T_tmp,");
            sb.AppendLine(@"    int numWords, int E_val, long long* carryBuffer,");
            sb.AppendLine(@"    int M, uint32_t* ntt_buf1, uint32_t* ntt_buf2, uint32_t* ntt_tmp,");
            sb.AppendLine(@"    const uint32_t* twiddle1_fwd, const uint32_t* twiddle2_fwd,");
            sb.AppendLine(@"    const uint32_t* twiddle1_inv, const uint32_t* twiddle2_inv) ");
            sb.AppendLine(@"{");
            sb.AppendLine(@"    cg::grid_group grid = cg::this_grid();");
            sb.AppendLine($@"    int bit2E1 = {2 * E + 1};");
            
            sb.AppendLine(@"    auto square_and_reduce = [&]() {");
            sb.AppendLine(@"        Device_MultiplySquare(T, numWords, grid, M, ntt_buf1, ntt_buf2, ntt_tmp, twiddle1_fwd, twiddle2_fwd, twiddle1_inv, twiddle2_inv);");
            sb.AppendLine(@"        for (int j = 0; j < 3; j++) {");
            sb.AppendLine(@"            Device_ShiftRight(T, bit2E1, Q, numWords * 2 + 16, grid);");
            sb.AppendLine(@"            Device_BitwiseAnd(T, MaskR, R, numWords * 2 + 16, grid);");
            sb.AppendLine($@"            Device_ShiftLeft(Q, {E}, T_tmp, numWords * 2 + 16, grid);");
            sb.AppendLine(@"            Device_Add(R, T_tmp, R, numWords * 2 + 16, carryBuffer, grid);");
            sb.AppendLine(@"            Device_ShiftLeft(Q, 1, T_tmp, numWords * 2 + 16, grid);");
            sb.AppendLine(@"            Device_Sub(R, Q, R, numWords * 2 + 16, carryBuffer, grid);");
            sb.AppendLine(@"            Device_Sub(R, T_tmp, R, numWords * 2 + 16, carryBuffer, grid);");
            sb.AppendLine(@"            Device_ShiftLeft(Q, 3, T_tmp, numWords * 2 + 16, grid);");
            sb.AppendLine(@"            Device_Sub(R, T_tmp, T, numWords * 2 + 16, carryBuffer, grid);");
            sb.AppendLine(@"        }");
            sb.AppendLine(@"    };");

            sb.AppendLine(@"    auto multiply_by_2_and_reduce = [&]() {");
            sb.AppendLine(@"        Device_ShiftLeft(T, 1, T_tmp, numWords * 2 + 16, grid);");
            sb.AppendLine(@"        Device_Copy(T_tmp, T, numWords * 2 + 16, grid);");
            sb.AppendLine(@"        for (int j = 0; j < 3; j++) {");
            sb.AppendLine(@"            Device_ShiftRight(T, bit2E1, Q, numWords * 2 + 16, grid);");
            sb.AppendLine(@"            Device_BitwiseAnd(T, MaskR, R, numWords * 2 + 16, grid);");
            sb.AppendLine($@"            Device_ShiftLeft(Q, {E}, T_tmp, numWords * 2 + 16, grid);");
            sb.AppendLine(@"            Device_Add(R, T_tmp, R, numWords * 2 + 16, carryBuffer, grid);");
            sb.AppendLine(@"            Device_ShiftLeft(Q, 1, T_tmp, numWords * 2 + 16, grid);");
            sb.AppendLine(@"            Device_Sub(R, Q, R, numWords * 2 + 16, carryBuffer, grid);");
            sb.AppendLine(@"            Device_Sub(R, T_tmp, R, numWords * 2 + 16, carryBuffer, grid);");
            sb.AppendLine(@"            Device_ShiftLeft(Q, 3, T_tmp, numWords * 2 + 16, grid);");
            sb.AppendLine(@"            Device_Sub(R, T_tmp, T, numWords * 2 + 16, carryBuffer, grid);");
            sb.AppendLine(@"        }");
            sb.AppendLine(@"    };");

            // Loop E times. Note that compiling static bounds could be even faster, 
            // but a loop is perfectly fine for PTX unrolling if #pragma unroll is used.
            // For now, standard for loop.
            sb.AppendLine($@"    for (int iter = 0; iter < E_val; iter++) {{");
            sb.AppendLine(@"        square_and_reduce();");
            sb.AppendLine(@"        multiply_by_2_and_reduce();");
            sb.AppendLine(@"    }");
            
            // Remaining E-4 times
            sb.AppendLine($@"    for (int iter = 0; iter < E_val - 4; iter++) {{");
            sb.AppendLine(@"        square_and_reduce();");
            sb.AppendLine(@"    }");

            // 1010 bit pattern
            sb.AppendLine(@"    square_and_reduce(); multiply_by_2_and_reduce(); // bit 3 (1)");
            sb.AppendLine(@"    square_and_reduce(); // bit 2 (0)");
            sb.AppendLine(@"    square_and_reduce(); multiply_by_2_and_reduce(); // bit 1 (1)");
            sb.AppendLine(@"    square_and_reduce(); // bit 0 (0)");

            sb.AppendLine(@"}");

            // Host Launch Function
            sb.AppendLine(@"extern ""C"" __declspec(dllexport) void Launch_JitFermatLoop(");
            sb.AppendLine(@"    unsigned int* T, unsigned int* Q, unsigned int* R, unsigned int* MaskR,");
            sb.AppendLine(@"    unsigned int* T_tmp,");
            sb.AppendLine(@"    int numWords, int E_val, long long* carryBuffer,");
            sb.AppendLine(@"    int M, uint32_t* ntt_buf1, uint32_t* ntt_buf2, uint32_t* ntt_tmp,");
            sb.AppendLine(@"    const uint32_t* twiddle1_fwd, const uint32_t* twiddle2_fwd,");
            sb.AppendLine(@"    const uint32_t* twiddle1_inv, const uint32_t* twiddle2_inv,");
            sb.AppendLine(@"    int gridDim, int blockDim) ");
            sb.AppendLine(@"{");
            sb.AppendLine(@"    void* args[] = { &T, &Q, &R, &MaskR, &T_tmp, &numWords, &E_val, &carryBuffer, &M, &ntt_buf1, &ntt_buf2, &ntt_tmp, &twiddle1_fwd, &twiddle2_fwd, &twiddle1_inv, &twiddle2_inv };");
            sb.AppendLine(@"    cudaError_t err = cudaLaunchCooperativeKernel((void*)Kernel_JitFermatLoop, gridDim, blockDim, args, 0, 0);");
            sb.AppendLine(@"    if (err != cudaSuccess) { printf(""JIT Launch Failed: %s\\n"", cudaGetErrorString(err)); fflush(stdout); }");
            sb.AppendLine(@"    err = cudaDeviceSynchronize();");
            sb.AppendLine(@"    if (err != cudaSuccess) { printf(""JIT Sync Failed: %s\\n"", cudaGetErrorString(err)); fflush(stdout); }");
            sb.AppendLine(@"}");

            return sb.ToString();
        }
    }
}
