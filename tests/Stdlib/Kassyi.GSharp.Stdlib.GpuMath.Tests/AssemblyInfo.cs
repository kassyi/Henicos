
// Important note on CUDA Stream Capture (CUDA Graph: GBI-31) and xUnit parallel test execution:
//
// The BigNum captures (KernelGraphHandler) run in relaxed mode, so an allocation such as cuMemAlloc on another thread
// does not disturb them. Work on the legacy default stream (Stream 0) does: a workspace stream is a blocking stream
// (CU_STREAM_DEFAULT), which synchronizes with the default stream, so default-stream work on any thread of the same
// CUDA context while a workspace stream captures fails with CUDA_ERROR_STREAM_CAPTURE_IMPLICIT and invalidates the
// capture (CUDA_ERROR_STREAM_CAPTURE_INVALIDATED). Tests that begin their own capture in a stricter mode also reject
// unsafe calls made during it.
//
// When xUnit executes test classes in parallel, other tests using auto-generated kernel wrappers or host array overloads
// may copy through the default stream concurrently, resulting in flaky test failures.
// Therefore, test parallelization is disabled for this test assembly to ensure deterministic and safe execution.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
