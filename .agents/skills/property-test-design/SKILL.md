---
name: property-test-design
description: Guidelines for designing or reviewing numeric and algorithm tests (GpuMath BigInt / BigFloat / NTT, CPU vs GPU comparisons). Define what "correct" means as properties first, then give each check an input that reaches it (coverage witness) and a one-line change that makes it fail (mutant), so that no test checks nothing.
---

# Property Test Design

Use this skill when you write or review tests for numeric code and algorithms: BigInt / BigFloat / NTT / radix conversion and similar code in `tests/Stdlib/Kassyi.GSharp.Stdlib.GpuMath.Tests`, and comparisons between a CPU and a GPU implementation.
Snapshot tests of transpiler output (Verify) are out of scope; follow [test-architecture.md](../../rules/test-architecture.md) for those.

A test is only as strong as its checks. A test that calls an API and does not look at the result, or a check that passes whatever you break, adds to the test count and protects nothing. This procedure prevents that by collecting two pieces of evidence for each property:

- **Coverage witness**: an input that actually runs the check (§4).
- **Mutant**: a one-line change to the implementation that makes the check fail (§5).

## 1. Decide what "correct" means first

Before you write a test, list the following for the API under test. If the specification or the code does not decide an answer, ask the user. Do not guess.

1. **Entry points**: Which public APIs and which call sequences do you test? What are their preconditions (normalized input, equal word counts, non-zero divisor, ...)?
2. **Input range**: How large can a real input be (words, bits, precision)? How far do the tests go? Move larger inputs to Stress (§7).
3. **Expected failures**: What must happen for invalid input: which exception type, an error result, or undefined behavior (the caller's responsibility)?
4. **Properties**: What must always be true, apart from "no exception"?

   | Kind | Example |
   | :- | :- |
   | Agrees with a reference | Same result as `System.Numerics.BigInteger`, `ReferenceNTT` or the CPU provider |
   | Round trip | `FromDecimal(ToDecimal(x)) == x`, `InverseNtt(Ntt(x)) == x` |
   | Algebraic identity | `q * b + r == a` and `0 <= r < b`; `s * s <= a < (s + 1) * (s + 1)` |
   | Two paths agree | `GSharpAot` and the CPU implementation agree; fused and unfused kernels agree |
   | Output bound | Word count of the result, floating-point error (write the bound as a formula) |

Agreement with a reference misses a bug that shares an assumption with the reference. Where possible, also check an identity (`q * b + r == a`).

## 2. Write each property as a check that always runs

- Write properties with xUnit `Assert.*`. Do not use `Debug.Assert` for a property: Release builds remove it.
- Each test method checks its results. Do not write a test that only calls APIs and has no `Assert`; fix one when you find it (for example, `GpuBigIntDivRemTests.Test_DivRem_Reduce_InverseSqrt_Heron` calls four APIs and checks nothing).
- Above each property check, write one or two comment lines: what it checks and why it must hold.
- **Do not take the expected value from the code under test**: compute the expected value with the reference implementation from the input data that the test holds. If the expected value is built from a value read back from the GPU, a bug in the readback (`ToBigInteger`, ...) cancels out between input and result and stays hidden. Test the readback itself with a separate round-trip test.

```csharp
// Bad: the input is also read back from the GPU
var expectedA = gpuA.ToBigInteger();

// Good: compute it from the input data that the test holds
var expectedA = ToBigInteger(leftData);
```

## 3. Keep expected failures apart from property violations

- Check an expected failure with a narrow exception type, such as `Assert.Throws<ArgumentException>(...)`. Do not wrap the call in a broad `catch` to make it pass.
- Do not hide a property violation by swallowing an exception in `try` / `catch` or by returning early when one is thrown.
- Use `[SkippableFact]` / `Skip` only for environments without a GPU (`CudaCapability.EnsureGpuAvailable()`). Never skip a case because its result is wrong.

## 4. Coverage witness: give each check an input that reaches it

Random inputs alone rarely reach rare branches. For each branch of the algorithm, add a named input that is known to reach it (`[Theory]` + `[MemberData]`, or a separate `[Fact]`).

Branches that numeric code often misses:

- A carry or borrow that runs through every word (add 1 to all-`0xFFFFFFFF` words, subtract 1 from `0`)
- Only the top bit set, only the top word non-zero, an exact power of 2
- 0, 1, the maximum value, equal operands, a one-word operand
- A normalization shift of 0 (the divisor's top bit is already set)
- The q̂ correction of Knuth D and its rare add-back step (build an input by hand that reaches it)
- Parallel boundaries: lengths around warp and block sizes (31 / 32 / 33, 1023 / 1024 / 1025), lengths that the thread count does not divide
- Both sides of an algorithm-switch threshold (schoolbook / Karatsuba / NTT, ...)

Confirm that each input runs the lines it targets with `just coverage-diff` (`scripts/coverage-diff.js` in [agent-scripts.md](../../rules/agent-scripts.md)). It lists the changed lines that no test executed. It cannot measure lines inside a GPU kernel; for those, build the input on the host so that it meets the branch condition, and write the reason in a comment on the test.

## 5. Mutant: show that a one-line change makes the check fail

For each property, find one one-line change (a mutant) that breaks the property, and show that one of the inputs of §4 makes the test fail.

1. Change one line of the implementation. For example, flip a comparison, remove a `+ 1`, stop a carry from propagating, or lower a loop bound by 1.
2. Run only the affected tests (`dotnet test --filter` through `just`, or with `DiffEngine_Disabled=true`).
3. Make sure that the test fails. If it does not, the check is too weak or no input reaches the branch; go back to §2 or §4.
4. **Undo the change with Edit.** Do not use `git checkout` / `git restore`: they also discard other changes in the same file. After you undo it, confirm with `git diff` that no difference is left in that file.
5. Record the mutant in a comment above the check (the line number and the change below only show the format):

```csharp
// The remainder is smaller than the divisor.
// mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:212 `if (cmp >= 0)` -> `if (cmp > 0)`
// input: DivisorEqualsTopWords
Assert.True(gpuR.ToBigInteger() < expectedB);
```

A check that no one-line change can make fail checks nothing. Write it again.
No tool runs this procedure automatically yet; it is manual. Whether to add a mutation-testing tool is a separate issue.

## 6. Make the tests deterministic

- Always give random generators a fixed seed (`new Random(42)`, `new Random(seed + numWords * 37)`). Make the seed visible in the failure message or the test name.
- Do not depend on clocks, thread timing or environment variables.
- Compare integer results exactly. When a floating-point comparison uses a tolerance, write in a comment where its bound comes from (number of operations × ulp, ...). Never use an arbitrary `1e-6`.
- A floating-point reduction whose result depends on the order of atomic operations is compared against an error bound, not exactly. Write the bound as a formula.

## 7. Keep inputs small

A normal test finishes in seconds. Bound every loop and size that comes from the input. Move large inputs (thousands of words or more, long random iterations) to `[Category(TestCategory.Stress)]` ([test-execution.md](../../rules/test-execution.md)).
Do not shrink a size that the property needs, for example the size that crosses the threshold into NTT.

## 8. Review checklist

When you finish a test, or when you review existing tests, confirm:

- [ ] Each test method has an `Assert` that checks its results
- [ ] Each property has a comment above it that says what it checks and why
- [ ] No expected value is built from a value read back from the code under test
- [ ] Besides agreement with a reference, the possible identities are checked
- [ ] Each boundary branch has a named input (§4)
- [ ] Each property has a recorded mutant that was shown to make it fail (§5)
- [ ] Random seeds are fixed and visible on failure
- [ ] Each floating-point tolerance has a stated bound
- [ ] Expected failures are checked with narrow exception types, not hidden behind a broad `catch` or a Skip

## Related

- [qa-testing-guidelines](../qa-testing-guidelines/SKILL.md): test attributes (`TestTarget` / `Category` / `Scenario`), naming, file placement, and the general boundary / stress / sad-path matrix.
- [test-architecture.md](../../rules/test-architecture.md): the test-first workflow (Phase 1) that this skill plugs into.
- [test-failed.ja.prompt.md](../../workflows/test-failed.ja.prompt.md): when a numeric test fails, fix the cause; never weaken a check to make it pass.
- `gpu-code-review` (synced from the shared agent package): reviews numeric tests against the checklist of §8, as `qa-testing-guidelines` §1 asks.

## Attribution

The ideas in §1–§7 (decide the properties first, write checks that always run instead of `assert`, coverage witnesses, mutants, determinism, input bounds) are adapted from §1 and §3 of `.claude/skills/add-project/SKILL.md` in Theorem Labs' [OSS Verify](https://github.com/theorem-labs/oss-verify) (Apache License 2.0).
The original targets fuzz targets in C / C++ / Rust and other languages that enroll in OSS Verify. This skill rewrites those ideas for G# xUnit tests and leaves out the enrollment steps, the build port and the licence-line rules.
