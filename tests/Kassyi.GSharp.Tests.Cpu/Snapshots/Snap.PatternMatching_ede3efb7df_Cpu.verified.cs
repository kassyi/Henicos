struct __gsharp_context_ControlFlowKernels_SwitchExpressionKernel {
int _state = 0;
__gsharp_context_ControlFlowKernels_SwitchExpressionKernel* _all_contexts = nullptr;
uint64_t _warpExchange = 0;
bool _yieldedForGridSync = false;
};
extern "C" __declspec(dllexport) void ControlFlowKernels_SwitchExpressionKernel(GSharpArray<int> input, GSharpArray<int> output, __gsharp_context_ControlFlowKernels_SwitchExpressionKernel* _ctx){
#define _state _ctx->_state
#define _yieldedForGridSync _ctx->_yieldedForGridSync
switch(_state)
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
_state=-1;
return;
}
}
#undef _state
#undef _yieldedForGridSync
}
extern "C" __declspec(dllexport) void __gsharp_launch_ControlFlowKernels_SwitchExpressionKernel(uint32_t gridDimX, uint32_t gridDimY, uint32_t gridDimZ, uint32_t blockDimX, uint32_t blockDimY, uint32_t blockDimZ, GSharpArray<int> input, GSharpArray<int> output)
{
int32_t _gsharp_total_blocks = (int32_t)(gridDimX * gridDimY * gridDimZ);
#pragma omp parallel for schedule(dynamic)
for (int32_t _b_idx = 0; _b_idx < _gsharp_total_blocks; _b_idx++)
{
int32_t bx = _b_idx % gridDimX;
int32_t by = (_b_idx / gridDimX) % gridDimY;
int32_t bz = _b_idx / (gridDimX * gridDimY);
__gsharp_reset_smem();
std::vector<__gsharp_context_ControlFlowKernels_SwitchExpressionKernel> _gsharp_contexts(blockDimX * blockDimY * blockDimZ);
for (auto& ctx : _gsharp_contexts) {
ctx._state = 0;
ctx._all_contexts = _gsharp_contexts.data();
}
std::atomic<bool> anyActive{true};
while (anyActive)
{
anyActive = false;
int32_t _gsharp_total_threads = (int32_t)(blockDimX * blockDimY * blockDimZ);
#pragma omp parallel for schedule(static)
for (int32_t _t_idx = 0; _t_idx < _gsharp_total_threads; _t_idx++)
{
int32_t tx = _t_idx % blockDimX;
int32_t ty = (_t_idx / blockDimX) % blockDimY;
int32_t tz = _t_idx / (blockDimX * blockDimY);
uint32_t _ctxtid = tz * blockDimY * blockDimX + ty * blockDimX + tx;
if (_gsharp_contexts[_ctxtid]._state != -1)
{
anyActive = true;
__gsharp_set_thread_idx(tx, ty, tz, bx, by, bz, blockDimX, blockDimY, blockDimZ, gridDimX, gridDimY, gridDimZ);
ControlFlowKernels_SwitchExpressionKernel(input, output, &_gsharp_contexts[_ctxtid]);
}
}
}
}
}
