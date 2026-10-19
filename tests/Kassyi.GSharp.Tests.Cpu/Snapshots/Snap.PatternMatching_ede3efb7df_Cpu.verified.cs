struct __gsharp_context_ControlFlowKernels_SwitchExpressionKernel {
int __gsharp_state = 0;
__gsharp_context_ControlFlowKernels_SwitchExpressionKernel* __gsharp_all_contexts = nullptr;
uint64_t __gsharp_warp_exchange = 0;
bool __gsharp_yielded_for_grid_sync = false;
};
extern "C" __declspec(dllexport) void ControlFlowKernels_SwitchExpressionKernel(GSharpArray<int> input, GSharpArray<int> output, __gsharp_context_ControlFlowKernels_SwitchExpressionKernel* __gsharp_ctx){
#define __gsharp_state __gsharp_ctx->__gsharp_state
#define __gsharp_yielded_for_grid_sync __gsharp_ctx->__gsharp_yielded_for_grid_sync
switch(__gsharp_state)
{
case 0:
__gsharp_switch_1_state_0:
{
int tid = threadIdx.x;
int val = input[tid];
output[tid]=([&]() {
switch(val)
{
case 1: return 10;
case 2: return 20;
case 3: return 30;
default: return 99;
}
}());
__gsharp_state=-1;
return;
}
}
#undef __gsharp_state
#undef __gsharp_yielded_for_grid_sync
}
extern "C" __declspec(dllexport) void __gsharp_launch_ControlFlowKernels_SwitchExpressionKernel(uint32_t gridDimX, uint32_t gridDimY, uint32_t gridDimZ, uint32_t blockDimX, uint32_t blockDimY, uint32_t blockDimZ, GSharpArray<int> input, GSharpArray<int> output)
{
__gsharp_host_last_error.Reset();
GSharpErrorSlot __gsharp_launch_error;
int32_t __gsharp_total_blocks = (int32_t)(gridDimX * gridDimY * gridDimZ);
#pragma omp parallel for schedule(dynamic)
for (int32_t __gsharp_block_index = 0; __gsharp_block_index < __gsharp_total_blocks; __gsharp_block_index++)
{
if (__gsharp_launch_error.HasError()) continue;
int32_t bx = __gsharp_block_index % gridDimX;
int32_t by = (__gsharp_block_index / gridDimX) % gridDimY;
int32_t bz = __gsharp_block_index / (gridDimX * gridDimY);
__gsharp_reset_smem();
std::vector<__gsharp_context_ControlFlowKernels_SwitchExpressionKernel> __gsharp_contexts(blockDimX * blockDimY * blockDimZ);
for (auto& ctx : __gsharp_contexts) {
ctx.__gsharp_state = 0;
ctx.__gsharp_all_contexts = __gsharp_contexts.data();
}
std::atomic<bool> anyActive{true};
while (anyActive && !__gsharp_launch_error.HasError())
{
anyActive = false;
int32_t __gsharp_threads_per_block = (int32_t)(blockDimX * blockDimY * blockDimZ);
#pragma omp parallel for schedule(static)
for (int32_t __gsharp_thread_index = 0; __gsharp_thread_index < __gsharp_threads_per_block; __gsharp_thread_index++)
{
if (__gsharp_launch_error.HasError()) continue;
__gsharp_active_error_slot = &__gsharp_launch_error;
int32_t tx = __gsharp_thread_index % blockDimX;
int32_t ty = (__gsharp_thread_index / blockDimX) % blockDimY;
int32_t tz = __gsharp_thread_index / (blockDimX * blockDimY);
uint32_t __gsharp_ctx_tid = tz * blockDimY * blockDimX + ty * blockDimX + tx;
if (__gsharp_contexts[__gsharp_ctx_tid].__gsharp_state != -1)
{
anyActive = true;
__gsharp_set_thread_idx(tx, ty, tz, bx, by, bz, blockDimX, blockDimY, blockDimZ, gridDimX, gridDimY, gridDimZ);
ControlFlowKernels_SwitchExpressionKernel(input, output, &__gsharp_contexts[__gsharp_ctx_tid]);
}
}
}
}
if (__gsharp_launch_error.HasError()) {
__gsharp_host_last_error.Report(static_cast<GSharpErrorCode>(__gsharp_launch_error.code.load(std::memory_order_acquire)), __gsharp_launch_error.message);
}
}
