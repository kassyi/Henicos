// Checks of planBuildStep (build-split.ts), part of the self-test: which run --gpu commands build outside the GPU turn
// and how the builds and the tests are written, and which commands stay as they are.

import type * as Assert from "node:assert";
import { planBuildStep } from "./build-split.ts";
import type { ShellName } from "./constants.ts";

type AssertModule = typeof Assert;

const Cuda = "tests/Compiler/Kassyi.GSharp.Tests.Cuda";

export function checkBuildSplit(assert: AssertModule): void {
  // [shell, command, the builds, the tests]
  const planned: [ShellName, string, string, string][] = [
    ["bash", `dotnet test ${Cuda}`, `dotnet build ${Cuda}`, `dotnet test --no-build ${Cuda}`],
    [
      "bash",
      `dotnet test ${Cuda} -c Release -f net10.0 -p:X=1 --filter "A|B" --logger trx --blame-crash -nodeReuse:false`,
      `dotnet build ${Cuda} -c Release -f net10.0 -p:X=1 -nodeReuse:false`,
      `dotnet test --no-build ${Cuda} -c Release -f net10.0 -p:X=1 --filter "A|B" --logger trx --blame-crash ` +
        "-nodeReuse:false",
    ],
    // The measured case: a build and the tests of what it built in one --gpu job
    [
      "bash",
      `dotnet build ${Cuda} -nodeReuse:false && dotnet test ${Cuda} --no-build --filter X`,
      `dotnet build ${Cuda} -nodeReuse:false`,
      `dotnet test ${Cuda} --no-build --filter X`,
    ],
    [
      "bash",
      `dotnet build A && dotnet test ${Cuda} --configuration=Debug -- RunConfiguration.X=1`,
      `dotnet build A && dotnet build ${Cuda} --configuration=Debug`,
      `dotnet test --no-build ${Cuda} --configuration=Debug -- RunConfiguration.X=1`,
    ],
    [
      "pwsh",
      `dotnet test ${Cuda} --filter "Name~it's" && dotnet test ${Cuda} -c Release`,
      `dotnet build ${Cuda} && dotnet build ${Cuda} -c Release`,
      `dotnet test --no-build ${Cuda} --filter "Name~it's" && dotnet test --no-build ${Cuda} -c Release`,
    ],
    [
      "cmd",
      `dotnet test "${Cuda}" -p:Y=%Z%`,
      `dotnet build "${Cuda}" -p:Y=%Z%`,
      `dotnet test --no-build "${Cuda}" -p:Y=%Z%`,
    ],
    // What agents queue: output to log files, variables for DiffEngine, quiet MSBuild output
    [
      "bash",
      `dotnet build ${Cuda} -nodeReuse:false -v:q -clp:ErrorsOnly > tmp/build-cuda.log 2>&1 && ` +
        `DiffEngine_Disabled=true dotnet test ${Cuda} --no-build --filter 'X' > tmp/test-cuda.log 2>&1`,
      `dotnet build ${Cuda} -nodeReuse:false -v:q -clp:ErrorsOnly > tmp/build-cuda.log 2>&1`,
      `DiffEngine_Disabled=true dotnet test ${Cuda} --no-build --filter 'X' > tmp/test-cuda.log 2>&1`,
    ],
    // The build of a test writes the test's log first; the test appends, as the one command wrote both
    [
      "bash",
      `export DiffEngine_Disabled=true CI=1; A="b c" dotnet test ${Cuda} -v:q -clp:ErrorsOnly &> "tmp/t.log"`,
      `export DiffEngine_Disabled=true CI=1; A="b c" dotnet build ${Cuda} -v:q -clp:ErrorsOnly &> "tmp/t.log"`,
      `export DiffEngine_Disabled=true CI=1; A="b c" dotnet test --no-build ${Cuda} -v:q -clp:ErrorsOnly ` +
        `&>> "tmp/t.log"`,
    ],
    [
      "bash",
      `dotnet test ${Cuda} --filter X >> tmp/a.log 2> tmp/err.log`,
      `dotnet build ${Cuda} >> tmp/a.log 2> tmp/err.log`,
      `dotnet test --no-build ${Cuda} --filter X >> tmp/a.log 2>> tmp/err.log`,
    ],
    [
      "pwsh",
      `$env:DiffEngine_Disabled='true'; dotnet build ${Cuda} *> tmp/b.log && ` +
        `dotnet test ${Cuda} --no-build 2>&1 > x.log`,
      `$env:DiffEngine_Disabled='true'; dotnet build ${Cuda} *> tmp/b.log`,
      `$env:DiffEngine_Disabled='true'; dotnet test ${Cuda} --no-build 2>&1 > x.log`,
    ],
    [
      "pwsh",
      `dotnet test ${Cuda} -c Release *> 'tmp/t.log'`,
      `dotnet build ${Cuda} -c Release *> 'tmp/t.log'`,
      `dotnet test --no-build ${Cuda} -c Release *>> 'tmp/t.log'`,
    ],
    [
      "cmd",
      `dotnet test ${Cuda} > t.log 2>&1`,
      `dotnet build ${Cuda} > t.log 2>&1`,
      `dotnet test --no-build ${Cuda} >> t.log 2>&1`,
    ],
  ];
  for (const [shell, command, build, tests] of planned) {
    assert.deepStrictEqual(planBuildStep(command, shell), { build, tests }, `${shell}: ${command}`);
  }
  // Left as they are
  const kept: [ShellName, string][] = [
    ["bash", `dotnet test ${Cuda} --no-build`], // nothing builds
    ["bash", "dotnet test"], // no project named: the one of the directory is left to the test
    ["bash", "dotnet test --filter X"],
    ["bash", "dotnet test bin/x.Tests.dll"], // a test of an assembly runs no build
    ["bash", `dotnet test ${Cuda} --unknown-option value`], // cannot tell whether a value follows
    ["bash", `dotnet test --unknown-switch ${Cuda}`],
    ["bash", `dotnet test ${Cuda} | tail -5`],
    ["bash", `dotnet test ${Cuda}; echo done`],
    ["bash", `dotnet test ${Cuda} || echo failed`],
    ["bash", `dotnet test $(echo ${Cuda})`],
    ["bash", `dotnet test ${Cuda} < input.txt`],
    ["bash", `dotnet test ${Cuda} > log.txt | tee x`],
    ["bash", `dotnet test ${Cuda} > $(date).log`],
    ["bash", `dotnet test ${Cuda} >&2`],
    ["bash", `dotnet test ${Cuda} > log.txt &`],
    ["bash", `cd x && dotnet test ${Cuda}`],
    ["bash", `FOO=$(pwd) dotnet test ${Cuda}`],
    ["bash", `echo x; dotnet test ${Cuda}`], // a ; that sets no variables
    ["bash", `export A=$(pwd); dotnet test ${Cuda}`],
    ["pwsh", `dotnet test ${Cuda} | Out-File x`],
    ["pwsh", `$env:A = $b; dotnet test ${Cuda}`],
    ["pwsh", `FOO=1 dotnet test ${Cuda}`], // an assignment is bash syntax
    ["bash", `dotnet test ${Cuda} && dotnet build A`], // a build after a test
    ["bash", `dotnet test ${Cuda} -p:X="a\\"b"`], // an escape inside quotes
    ["bash", `dotnet test ${Cuda}\ndotnet build A`],
    ["bash", "just test"],
    ["bash", "dotnet build A"], // no test
    ["pwsh", `dotnet test ${Cuda} --% -p:X=1`],
    ["pwsh", `dotnet test ${Cuda} -p:X=$y`],
    ["pwsh", `& dotnet test ${Cuda}`],
  ];
  for (const [shell, command] of kept) {
    assert.strictEqual(planBuildStep(command, shell), null, `kept as it is in ${shell}: ${command}`);
  }
}
