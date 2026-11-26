// Unit checks for CommandTranslator (quoting, env rejection, cd, collectPaths, MSBuild switches, places) and rules.ts.

import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { PluginApiVersion, type QueuePlugin } from "../plugin-api.ts";
import { PluginHost } from "../plugin-host.ts";
import { translateForRemote } from "./command-translator.ts";
import { getEnvValue, hasEnvKey } from "./env-policy.ts";
import { resolveRemoteRules } from "./rules.ts";
import type { AssertModule } from "./checks-support.ts";

export const RemoteTranslatorChecks: Record<string, (assert: AssertModule) => void> = {
  remoteCommandTranslatesPowerShellAndCrossWorktreePaths(assert: AssertModule): void {
    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-rcmd-"));
    try {
      const wtA = path.join(tmp, "wt-main");
      const wtB = path.join(tmp, "wt-review");
      fs.mkdirSync(path.join(wtA, ".git"), { recursive: true });
      fs.mkdirSync(path.join(wtB, ".git"), { recursive: true });
      const scriptB = path.join(wtB, "scripts", "run-tests.js");
      fs.mkdirSync(path.dirname(scriptB), { recursive: true });
      fs.writeFileSync(scriptB, "console.log('ok');");

      const outB = path.join(wtB, "tmp", "out.json");
      const coreNodePlan = translateForRemote(`$env:CI='true'; node ${scriptB} --output ${outB}`, {
        cwd: wtA,
        worktree: wtA,
        shell: "pwsh",
        places: { [scriptB]: { root: wtB, ignored: false }, [outB]: { root: wtB, ignored: false } },
        cpus: 4,
        keepBuildServersWarm: true,
        env: {},
      });
      assert.ok(coreNodePlan !== null, "core translates cross-worktree node command without plugins");
      assert.strictEqual(path.resolve(coreNodePlan.worktree), path.resolve(wtB));
      assert.strictEqual(coreNodePlan.remoteCwdRel, "");
      assert.ok(coreNodePlan.bashCommand.includes("scripts/run-tests.js"));
      assert.strictEqual(coreNodePlan.env.CI, "true");
      assert.strictEqual(coreNodePlan.env.GSHARP_TEST_NO_CUDA, undefined, "core has no Henicos env hardcode");
      assert.strictEqual(coreNodePlan.env.MSBUILDCORELIMIT, undefined, "core has no MSBuild env hardcode");

      assert.strictEqual(
        translateForRemote("dotnet test App.csproj", {
          cwd: wtB,
          worktree: wtB,
          shell: "pwsh",
          places: {},
          cpus: 4,
          keepBuildServersWarm: true,
          env: {},
        }),
        null,
        "core without plugins does not hardcode dotnet as an allowed program",
      );

      const cargoPlugin: QueuePlugin = {
        name: "cargo",
        apiVersion: PluginApiVersion,
        remoteOffload: () => ({
          allowedPrograms: ["cargo"],
          pathValueOptions: ["--manifest-path"],
          nonPathValueOptions: ["--filter"],
          pathExtensions: ["toml"],
          defaultEnv: { CARGO_TERM_COLOR: "always" },
          rewriteStatement: (stmt) =>
            stmt.program === "cargo" && stmt.subcommand === "test"
              ? { args: [...stmt.args, "--nocapture"] }
              : undefined,
          prepareRemoteCommand: (command, grant) => ({
            command,
            env: { CARGO_BUILD_JOBS: String(grant.cpus) },
            note: `${grant.cpus} cargo jobs`,
          }),
        }),
      };
      const cargoHost = new PluginHost([cargoPlugin], [{}], wtA, () => {});
      const cargoRules = cargoHost.remoteOffload({
        cwd: wtA,
        worktree: wtA,
        configRoot: wtA,
        shell: "pwsh",
        env: {},
        log: () => {},
      });
      const manifestB = path.join(wtB, "Cargo.toml");
      fs.writeFileSync(manifestB, "[package]\nname = 'demo'\n");
      const cargoPlan = translateForRemote(`cargo test --manifest-path ${manifestB} --filter foo`, {
        cwd: wtA,
        worktree: wtA,
        shell: "pwsh",
        places: { [manifestB]: { root: wtB, ignored: false } },
        cpus: 4,
        keepBuildServersWarm: true,
        env: {},
        remoteRules: cargoRules,
      });
      assert.ok(cargoPlan !== null, "plugin-contributed cargo command translates");
      assert.strictEqual(path.resolve(cargoPlan.worktree), path.resolve(wtB));
      assert.ok(cargoPlan.bashCommand.includes("--manifest-path Cargo.toml"));
      assert.ok(cargoPlan.bashCommand.includes("--nocapture"));
      assert.strictEqual(cargoPlan.env.CARGO_TERM_COLOR, "always");
      assert.strictEqual(cargoPlan.env.CARGO_BUILD_JOBS, "4");
      assert.strictEqual(cargoPlan.note, "4 cargo jobs");

      // Inline --opt=val path and non-path options
      const inlineCargoPlan = translateForRemote(`cargo test --manifest-path=${manifestB} --filter=Name~Foo`, {
        cwd: wtA,
        worktree: wtA,
        shell: "pwsh",
        places: { [manifestB]: { root: wtB, ignored: false } },
        cpus: 4,
        keepBuildServersWarm: true,
        env: {},
        remoteRules: cargoRules,
      });
      assert.ok(inlineCargoPlan !== null);
      assert.ok(inlineCargoPlan.bashCommand.includes("--manifest-path=Cargo.toml"));
      assert.ok(inlineCargoPlan.bashCommand.includes("--filter=Name~Foo"));

      const scriptA = path.join(wtA, "scripts", "a.js");
      fs.mkdirSync(path.dirname(scriptA), { recursive: true });
      fs.writeFileSync(scriptA, "console.log('a');");
      assert.strictEqual(
        translateForRemote(`node ${scriptA} && node ${scriptB}`, {
          cwd: wtA,
          worktree: wtA,
          shell: "pwsh",
          places: {
            [scriptA]: { root: wtA, ignored: false },
            [scriptB]: { root: wtB, ignored: false },
          },
          cpus: 4,
          keepBuildServersWarm: true,
          env: {},
        }),
        null,
        "multi-worktree command must not offload",
      );

      assert.strictEqual(
        translateForRemote(`node ${scriptB} | Select-Object -First 5`, {
          cwd: wtB,
          worktree: wtB,
          shell: "pwsh",
          places: { [scriptB]: { root: wtB, ignored: false } },
          cpus: 4,
          keepBuildServersWarm: true,
          env: {},
        }),
        null,
      );
      assert.strictEqual(
        translateForRemote(`node ${scriptB}`, {
          cwd: wtB,
          worktree: wtB,
          shell: "pwsh",
          places: { [scriptB]: { root: wtB, ignored: true } },
          cpus: 4,
          keepBuildServersWarm: true,
          env: {},
        }),
        null,
      );
    } finally {
      fs.rmSync(tmp, { recursive: true, force: true });
    }
  },

  remoteCommandTranslatorEdgeCasesAndSecurity(assert: AssertModule): void {
    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-rcmd-edge-"));
    try {
      const wt = path.join(tmp, "wt");
      fs.mkdirSync(path.join(wt, ".git"), { recursive: true });
      fs.mkdirSync(path.join(wt, "scripts"), { recursive: true });
      fs.mkdirSync(path.join(wt, "tests"), { recursive: true });
      const script = path.join(wt, "scripts", "a.js");
      fs.writeFileSync(script, "console.log('a');");
      const baseOpts = {
        cwd: wt,
        worktree: wt,
        shell: "pwsh" as const,
        places: { [script]: { root: wt, ignored: false } },
        cpus: 4,
        keepBuildServersWarm: true,
        env: {},
      };

      // Internal quotes preserved / unescaped properly
      const pwshDoubled = translateForRemote("echo 'Name~Foo''Bar'", baseOpts);
      assert.ok(pwshDoubled !== null && pwshDoubled.bashCommand.includes("'Name~Foo'\\''Bar'"));
      const bashConcat = translateForRemote("echo Name~Foo''Bar", { ...baseOpts, shell: "bash" });
      assert.ok(bashConcat !== null && bashConcat.bashCommand.includes("Name~FooBar"));
      assert.strictEqual(
        translateForRemote("echo 'Name~Foo'Bar", baseOpts),
        null,
        "pwsh rejects mixed closed single-quote suffix",
      );
      const dblApostrophe = translateForRemote('echo "Name~it\'s"', baseOpts);
      assert.ok(dblApostrophe !== null && dblApostrophe.bashCommand.includes("'Name~it'\\''s'"));
      const sglDoubleQuote = translateForRemote("echo 'Name~\"Foo\"'", baseOpts);
      assert.ok(sglDoubleQuote !== null && sglDoubleQuote.bashCommand.includes("'Name~\"Foo\"'"));

      // $var and $(cmd) in env assignments fail closed
      for (const badEnvCmd of [
        `$env:FOO=$bar; node ${script}`,
        `$env:FOO=$(id); node ${script}`,
        `$env:FOO="$bar"; node ${script}`,
        `$env:FOO="$(id)"; node ${script}`,
        `$env:FOO=$bar`,
      ]) {
        assert.strictEqual(translateForRemote(badEnvCmd, baseOpts), null, `rejects pwsh env: ${badEnvCmd}`);
      }
      for (const badPosixEnv of [
        `export FOO=$bar; node ${script}`,
        `export FOO="$(id)"; node ${script}`,
        `FOO=$bar node ${script}`,
      ]) {
        assert.strictEqual(
          translateForRemote(badPosixEnv, { ...baseOpts, shell: "bash" }),
          null,
          `rejects bash env: ${badPosixEnv}`,
        );
      }

      // Leading cd works; mid-chain cd fails closed
      const leadCd = translateForRemote("cd scripts && node a.js", baseOpts);
      assert.ok(leadCd !== null && leadCd.remoteCwdRel === "scripts" && leadCd.bashCommand === "node a.js");
      assert.strictEqual(translateForRemote("node scripts/a.js && cd scripts && node a.js", baseOpts), null);
      assert.strictEqual(translateForRemote("node scripts/a.js; Set-Location scripts; node a.js", baseOpts), null);

      // NB5: cd -, cd ~, nonexistent cd, and file-target cd fail closed across shells (even if "-" or "~" dirs exist)
      fs.mkdirSync(path.join(wt, "-"), { recursive: true });
      fs.mkdirSync(path.join(wt, "~"), { recursive: true });
      for (const shell of ["bash", "pwsh", "cmd"] as const) {
        assert.strictEqual(
          translateForRemote("cd - && echo hi", { ...baseOpts, shell }),
          null,
          `${shell} rejects cd -`,
        );
        assert.strictEqual(
          translateForRemote("cd ~ && echo hi", { ...baseOpts, shell }),
          null,
          `${shell} rejects cd ~`,
        );
        assert.strictEqual(
          translateForRemote("cd ~/scripts && echo hi", { ...baseOpts, shell }),
          null,
          `${shell} rejects cd ~/scripts`,
        );
        assert.strictEqual(
          translateForRemote("cd nonexistent && echo hi", { ...baseOpts, shell }),
          null,
          `${shell} rejects missing cd target`,
        );
        assert.strictEqual(
          translateForRemote("cd scripts/a.js && echo hi", { ...baseOpts, shell }),
          null,
          `${shell} rejects file cd target`,
        );
      }
      assert.strictEqual(translateForRemote("Set-Location -; echo hi", baseOpts), null);
      assert.strictEqual(translateForRemote("Set-Location ~; echo hi", baseOpts), null);
      assert.strictEqual(translateForRemote("Set-Location -Path scripts; echo hi", baseOpts), null);
      assert.strictEqual(translateForRemote("sl nonexistent; echo hi", baseOpts), null);
      assert.strictEqual(translateForRemote('cd "" && echo hi', { ...baseOpts, shell: "bash" }), null);
      const pwshCd = translateForRemote("Set-Location scripts; node a.js", baseOpts);
      assert.ok(pwshCd !== null && pwshCd.remoteCwdRel === "scripts");

      // B3: reject variable expansion across bash, pwsh, and cmd while keeping single-quoted literals safe
      for (const badBash of [
        'node scripts/a.js --filter "FullyQualifiedName~$NAME"',
        'node scripts/a.js --filter "FullyQualifiedName~${NAME}"',
        "node scripts/a.js --filter $NAME",
      ]) {
        assert.strictEqual(
          translateForRemote(badBash, { ...baseOpts, shell: "bash" }),
          null,
          `bash rejects ${badBash}`,
        );
      }
      const bashSingleQuotedDollar = translateForRemote("node scripts/a.js --filter 'FullyQualifiedName~$NAME'", {
        ...baseOpts,
        shell: "bash",
      });
      assert.ok(
        bashSingleQuotedDollar !== null && bashSingleQuotedDollar.bashCommand.includes("'FullyQualifiedName~$NAME'"),
        "bash preserves single-quoted literal $NAME",
      );
      const bashPercentLiteral = translateForRemote('node scripts/a.js --filter "Name~100%"', {
        ...baseOpts,
        shell: "bash",
      });
      assert.ok(bashPercentLiteral !== null && bashPercentLiteral.bashCommand.includes("'Name~100%'"));

      for (const badPwsh of [
        `node ${script} --filter "FullyQualifiedName~$NAME"`,
        `node ${script} --filter "FullyQualifiedName~$env:NAME"`,
        `node ${script} --filter $env:NAME`,
      ]) {
        assert.strictEqual(translateForRemote(badPwsh, baseOpts), null, `pwsh rejects ${badPwsh}`);
      }
      const pwshLeadingEnv = translateForRemote(`$env:CI='true'; node ${script}`, baseOpts);
      assert.ok(pwshLeadingEnv !== null && pwshLeadingEnv.env.CI === "true");

      for (const badCmd of [
        `node ${script} --filter "FullyQualifiedName~%NAME%"`,
        `node ${script} --filter %NAME%`,
        `node ${script} --filter 'FullyQualifiedName~%NAME%'`,
      ]) {
        assert.strictEqual(translateForRemote(badCmd, { ...baseOpts, shell: "cmd" }), null, `cmd rejects ${badCmd}`);
      }
      const cmdLiteral = translateForRemote(`node ${script} --filter "FullyQualifiedName~SmokeTests"`, {
        ...baseOpts,
        shell: "cmd",
      });
      assert.ok(cmdLiteral !== null && cmdLiteral.bashCommand.includes("FullyQualifiedName~SmokeTests"));

      // NB2: fd duplication redirections (1>&2, 1>&1, *>&1, 2>&1) vs unsupported &-targets
      const bash12 = translateForRemote("node scripts/a.js 1>&2", { ...baseOpts, shell: "bash" });
      assert.ok(bash12 !== null && bash12.bashCommand.endsWith(" 1>&2"));
      assert.deepStrictEqual(bash12.collectPaths, []);
      const bash11 = translateForRemote("node scripts/a.js 1>&1", { ...baseOpts, shell: "bash" });
      assert.ok(bash11 !== null && bash11.bashCommand.endsWith(" 1>&1"));
      assert.deepStrictEqual(bash11.collectPaths, []);
      const pwshStar1 = translateForRemote("node scripts/a.js *>&1", baseOpts);
      assert.ok(pwshStar1 !== null && pwshStar1.bashCommand.endsWith(" 2>&1"));
      assert.deepStrictEqual(pwshStar1.collectPaths, []);
      assert.strictEqual(translateForRemote("node scripts/a.js 3>&1", baseOpts), null);
      assert.strictEqual(translateForRemote("node scripts/a.js 2>&-", { ...baseOpts, shell: "bash" }), null);
      assert.strictEqual(translateForRemote("node scripts/a.js >&2", { ...baseOpts, shell: "bash" }), null);
      assert.strictEqual(translateForRemote('node scripts/a.js > "&2"', { ...baseOpts, shell: "bash" }), null);

      // NB7-c: > NUL / > /dev/null rewrite without collection; > CON and reserved devices fail closed
      for (const nulShell of ["cmd", "pwsh"] as const) {
        const nulPlan = translateForRemote("node scripts/a.js > NUL", { ...baseOpts, shell: nulShell });
        assert.ok(nulPlan !== null && nulPlan.bashCommand.includes("> /dev/null"));
        assert.deepStrictEqual(nulPlan.collectPaths, []);
        const nul2Plan = translateForRemote("node scripts/a.js 2> nul 2>&1", { ...baseOpts, shell: nulShell });
        assert.ok(nul2Plan !== null && nul2Plan.bashCommand.includes("2> /dev/null 2>&1"));
        assert.deepStrictEqual(nul2Plan.collectPaths, []);
      }
      const devNullBash = translateForRemote("node scripts/a.js > /dev/null", { ...baseOpts, shell: "bash" });
      assert.ok(devNullBash !== null && devNullBash.bashCommand.includes("> /dev/null"));
      assert.deepStrictEqual(devNullBash.collectPaths, []);
      assert.strictEqual(translateForRemote("node scripts/a.js > NUL", { ...baseOpts, shell: "bash" }), null);
      for (const badRedir of ["> CON", "> con", "2> CON", "> PRN", "> AUX", "> COM1", "> tmp/CON.log"]) {
        assert.strictEqual(translateForRemote(`node scripts/a.js ${badRedir}`, baseOpts), null, `rejects ${badRedir}`);
        assert.strictEqual(
          translateForRemote(`node scripts/a.js ${badRedir}`, { ...baseOpts, shell: "bash" }),
          null,
          `bash rejects ${badRedir}`,
        );
      }

      // Broad --results-directory rejected while narrow tmp/trx allowed; /nr:false & /p:... not treated as paths
      const runnerPlugin: QueuePlugin = {
        name: "test-runner",
        apiVersion: PluginApiVersion,
        remoteOffload: () => ({
          allowedPrograms: ["dotnet"],
          pathValueOptions: ["--results-directory", "-d", "-o"],
          nonPathValueOptions: ["--filter", "--property", "-l", "--logger"],
          isOptionValuePath: (program, subcommand, option) =>
            program === "dotnet" && option === "-p" ? subcommand === "run" : undefined,
          collectDirOptions: ["--results-directory"],
        }),
      };
      const runnerRules = new PluginHost([runnerPlugin], [{}], wt, () => {}).remoteOffload({
        cwd: wt,
        worktree: wt,
        configRoot: wt,
        shell: "pwsh",
        env: {},
        log: () => {},
      });
      for (const broad of ["tests", ".", "src", "scripts"]) {
        assert.strictEqual(
          translateForRemote(`dotnet test --results-directory ${broad}`, { ...baseOpts, remoteRules: runnerRules }),
          null,
          `rejects broad --results-directory ${broad}`,
        );
      }
      assert.strictEqual(
        translateForRemote("dotnet test --results-directory=tests", { ...baseOpts, remoteRules: runnerRules }),
        null,
      );
      const narrow = translateForRemote("dotnet test --results-directory=tmp/trx /nr:false /p:Configuration=Release", {
        ...baseOpts,
        remoteRules: runnerRules,
      });
      assert.ok(narrow !== null);
      assert.deepStrictEqual(narrow.collectPaths, [{ path: "tmp/trx", kind: "dir" }]);
      assert.deepStrictEqual(narrow.redirectedResultsDirs, ["tmp/trx"]);
      assert.ok(narrow.bashCommand.includes("/nr:false"));
      assert.ok(narrow.bashCommand.includes("/p:Configuration=Release"));

      const spaceResultsDir = translateForRemote("dotnet test --results-directory tmp/trx2", {
        ...baseOpts,
        remoteRules: runnerRules,
      });
      assert.ok(spaceResultsDir !== null);
      assert.deepStrictEqual(spaceResultsDir.redirectedResultsDirs, ["tmp/trx2"]);

      const subDir = path.join(wt, "sub");
      fs.mkdirSync(subDir, { recursive: true });
      fs.writeFileSync(path.join(wt, "tests", "App.csproj"), "<Project />\n");
      for (const emptyResultsCmd of [
        'dotnet test ../tests/App.csproj --results-directory ""',
        'dotnet test ../tests/App.csproj --results-directory "  "',
        'dotnet test ../tests/App.csproj --results-directory=""',
      ]) {
        assert.strictEqual(
          translateForRemote(emptyResultsCmd, { ...baseOpts, cwd: subDir, remoteRules: runnerRules }),
          null,
          `rejects empty results-directory from subdir: ${emptyResultsCmd}`,
        );
      }

      const plainRedir = translateForRemote("node scripts/a.js > tmp/out.log", {
        ...baseOpts,
        remoteRules: runnerRules,
      });
      assert.ok(plainRedir !== null);
      assert.deepStrictEqual(plainRedir.collectPaths, [{ path: "tmp/out.log", kind: "file" }]);
      assert.strictEqual(plainRedir.redirectedResultsDirs, undefined);

      // B5: reject leading "-" paths/segments in --results-directory, -d, redirections, and plugin collectPaths
      for (const dashCmd of [
        "dotnet test --results-directory -delete",
        "dotnet test --results-directory=-delete",
        "dotnet test --results-directory ./-delete",
        "cd scripts && dotnet test --results-directory -delete",
        "dotnet test -d -evil.log",
        "node scripts/a.js > -evil.log",
        "node scripts/a.js > tmp/-evil.log",
      ]) {
        assert.strictEqual(
          translateForRemote(dashCmd, { ...baseOpts, remoteRules: runnerRules }),
          null,
          `rejects leading dash path: ${dashCmd}`,
        );
      }
      const dashPlugin: QueuePlugin = {
        name: "dash-collect",
        apiVersion: PluginApiVersion,
        remoteOffload: () => ({
          allowedPrograms: ["node"],
          rewriteStatement: () => ({ collectPaths: [{ path: "-delete", kind: "dir" }] }),
        }),
      };
      const dashRules = new PluginHost([dashPlugin], [{}], wt, () => {}).remoteOffload({
        cwd: wt,
        worktree: wt,
        configRoot: wt,
        shell: "pwsh",
        env: {},
        log: () => {},
      });
      assert.strictEqual(translateForRemote("node scripts/a.js", { ...baseOpts, remoteRules: dashRules }), null);

      // NB7-a: reject forbidden collect targets early in addCollect
      for (const forbiddenDir of [
        ".git",
        ".git/hooks",
        ".claude",
        ".vscode",
        "node_modules",
        "sub/node_modules",
        "docs/spec",
        "scripts/sub",
        ".agents/sub",
        ".github/workflows",
        "src/App/bin",
        "src/App/obj",
      ]) {
        assert.strictEqual(
          translateForRemote(`dotnet test --results-directory ${forbiddenDir}`, {
            ...baseOpts,
            remoteRules: runnerRules,
          }),
          null,
          `rejects forbidden collect dir: ${forbiddenDir}`,
        );
      }
      for (const forbiddenRedir of [
        "node scripts/a.js > .git/hooks/pre-commit",
        "node scripts/a.js > docs/spec/en/a.md",
        "node scripts/a.js > scripts/out.log",
        "node scripts/a.js > tests/Snap.verified.txt",
        "node scripts/a.js > src/App/obj/a.log",
      ]) {
        assert.strictEqual(
          translateForRemote(forbiddenRedir, baseOpts),
          null,
          `rejects forbidden redir: ${forbiddenRedir}`,
        );
      }
      const badCollectPlugin: QueuePlugin = {
        name: "bad-collect",
        apiVersion: PluginApiVersion,
        remoteOffload: () => ({
          allowedPrograms: ["node"],
          rewriteStatement: () => ({ collectPaths: [{ path: "docs/spec", kind: "dir" }] }),
        }),
      };
      const badCollectRules = new PluginHost([badCollectPlugin], [{}], wt, () => {}).remoteOffload({
        cwd: wt,
        worktree: wt,
        configRoot: wt,
        shell: "pwsh",
        env: {},
        log: () => {},
      });
      assert.strictEqual(translateForRemote("node scripts/a.js", { ...baseOpts, remoteRules: badCollectRules }), null);

      // NB7-b: allow valid "..foo" names while still rejecting ".." and "../escape"
      fs.mkdirSync(path.join(wt, "..foo"), { recursive: true });
      fs.writeFileSync(path.join(wt, "..foo", "a.js"), "console.log('ok');");
      const dotdotFooCmd = translateForRemote("node ..foo/a.js", baseOpts);
      assert.ok(dotdotFooCmd !== null && dotdotFooCmd.bashCommand.includes("..foo/a.js"));
      const dotdotFooDir = translateForRemote("dotnet test --results-directory ..foo", {
        ...baseOpts,
        remoteRules: runnerRules,
      });
      assert.ok(dotdotFooDir !== null);
      assert.deepStrictEqual(dotdotFooDir.collectPaths, [{ path: "..foo", kind: "dir" }]);
      const dotdotFooRedir = translateForRemote("node scripts/a.js > ..foo.log", baseOpts);
      assert.ok(dotdotFooRedir !== null);
      assert.deepStrictEqual(dotdotFooRedir.collectPaths, [{ path: "..foo.log", kind: "file" }]);
      assert.strictEqual(translateForRemote("node ../escape.js", baseOpts), null);

      // NB6: worktree containment after --, in MSBuild /p /bl /flp /l -p --property switches, and @rsp
      const outsideFile = path.join(tmp, "outside.txt");
      for (const nb6Reject of [
        "dotnet test -- ../outside.txt",
        "dotnet test -- /etc/passwd",
        `dotnet test -- ${outsideFile}`,
        "dotnet test -- --output=../outside.txt",
        "dotnet test -- -o ../outside.txt",
        "dotnet test -- RunConfiguration.ResultsDirectory=../outside",
        'dotnet test -- \'TestRunParameters.Parameter(name="x", value="../outside")\'',
        "dotnet test -- /m",
        "dotnet test /p:OutDir=../outside",
        "dotnet test /p:Configuration=Release;OutDir=../outside",
        "dotnet test -p:OutDir=../outside",
        "dotnet test --property:OutDir=../outside",
        "dotnet test --property OutDir=../outside",
        "dotnet test /bl:../outside.binlog",
        "dotnet test /bl:LogFile=../outside.binlog;ProjectImports=None",
        "dotnet test -bl:../outside.binlog",
        "dotnet test /flp:LogFile=../outside.log;Verbosity=diag",
        "dotnet test /flp1:LogFile=../outside.log",
        "dotnet test -flp:LogFile=../outside.log",
        "dotnet test /l:FileLogger,../outside.dll;logfile=../outside.log",
        'dotnet test --logger "trx;LogFileName=../outside.trx"',
        "dotnet test @local.rsp",
        "dotnet test @../outside.rsp",
        "dotnet test -- @local.rsp",
      ]) {
        assert.strictEqual(
          translateForRemote(nb6Reject, { ...baseOpts, remoteRules: runnerRules }),
          null,
          `NB6 rejects: ${nb6Reject}`,
        );
      }
      // pwsh refuses an unquoted @ (splatting) before the @rsp rule runs, so only bash reaches that rule
      for (const rspReject of ["dotnet test @local.rsp", "dotnet test -- @local.rsp"]) {
        assert.strictEqual(
          translateForRemote(rspReject, { ...baseOpts, shell: "bash" as const, remoteRules: runnerRules }),
          null,
          `NB6 rejects in bash: ${rspReject}`,
        );
      }
      const ddAllowed = translateForRemote(`dotnet test -- RunConfiguration.X=1 ${path.join(wt, "tests", "in.txt")}`, {
        ...baseOpts,
        remoteRules: runnerRules,
      });
      assert.ok(ddAllowed !== null && ddAllowed.bashCommand.includes("tests/in.txt"));
      const bareMsbuild = translateForRemote("dotnet test /bl /nologo /m /flp:Verbosity=diag", {
        ...baseOpts,
        remoteRules: runnerRules,
      });
      assert.ok(bareMsbuild !== null);
      const slashFilter = translateForRemote('dotnet test --filter "FullyQualifiedName~Name/WithSlash"', {
        ...baseOpts,
        remoteRules: runnerRules,
      });
      assert.ok(slashFilter !== null);
      const nodeEval = translateForRemote('node -e "console.log(1/2)"', baseOpts);
      assert.ok(nodeEval !== null);
      const blInside = translateForRemote(`dotnet test /bl:${path.join(wt, "tmp", "build.binlog")}`, {
        ...baseOpts,
        remoteRules: runnerRules,
      });
      assert.ok(blInside !== null && blInside.bashCommand.includes("/bl:tmp/build.binlog"));

      // Windows-specific Git Bash /c/... and case-insensitive options.places
      if (process.platform === "win32") {
        const drive = script.charAt(0).toLowerCase();
        const gitBashScript = `/${drive}/${script.slice(3).split(path.sep).join("/")}`;
        const gitBashPlan = translateForRemote(`node ${gitBashScript}`, { ...baseOpts, shell: "bash" });
        assert.ok(gitBashPlan !== null, "translates Git Bash /<drive>/... path");
        assert.ok(gitBashPlan.bashCommand.includes("scripts/a.js"));

        const upperPlacesPlan = translateForRemote(`node ${script.toLowerCase()}`, {
          ...baseOpts,
          places: { [script.toUpperCase()]: { root: wt, ignored: false } },
        });
        assert.ok(upperPlacesPlan !== null, "matches options.places case-insensitively on Windows");
      }
    } finally {
      fs.rmSync(tmp, { recursive: true, force: true });
    }
  },

  remoteRulesValidationRejectsUnsafeInputs(assert: AssertModule): void {
    const rawDefaultEnv = JSON.parse(
      '{"__proto__":"evil","constructor":"evil","bad-dash":"1","DiffEngine_Disabled":"true"}',
    ) as Record<string, string>;
    const resolved = resolveRemoteRules([
      {
        contribution: {
          allowedPrograms: ["Cargo", "evil;rm", "bad space", ".", "..", "node"],
          defaultEnv: rawDefaultEnv,
          allowedEnv: ["TEST_SEED", "MixedCase", "bad-dash", "bad space", "123NUM", "a;rm", "__proto__"],
          localOnlyEnv: ["GSHARP_SIGNING_KEY", "MIXEDCASE", "bad-local", "__proto__"],
          localOnlyEnvPrefixes: ["DOTNET_", "bad-prefix", ""],
          ignoredEnv: ["DOTNET_ROOT", "bad;x"],
          ignoredEnvPrefixes: ["Queue_", "a b"],
          syncExcludePrefixes: ["-rf", "tests/-rf", "./-rf", "../escape", "/abs", "valid/dir/"],
          resetCleanDirs: ["-rf", "a/-rf", "./-rf", "tmp/clean"],
          failureArtifacts: [
            { searchDirs: ["-exec"], findName: "*.txt", allowPattern: /^.*\.txt$/ },
            { searchDirs: ["tests/-rf"], findName: "*.txt", allowPattern: /^.*\.txt$/ },
            { searchDirs: ["tests"], findName: "*.actual.txt", allowPattern: /^tests\/.*\.actual\.txt$/ },
          ],
          crashSummaries: [{ searchDirs: ["-rf"], findName: "*.xml", pathPattern: /^.*\.xml$/, parse: () => ["x"] }],
        },
        call: (_subHook, invoke) => invoke(),
      },
    ]);
    assert.deepStrictEqual([...resolved.allowedPrograms].sort(), ["cargo", "node"]);
    assert.deepStrictEqual(Object.keys(resolved.defaultEnv), ["DiffEngine_Disabled"]);
    assert.strictEqual(resolved.defaultEnv.DiffEngine_Disabled, "true");
    assert.deepStrictEqual([...resolved.allowedEnv], ["TEST_SEED"]);
    assert.deepStrictEqual([...resolved.localOnlyEnv].sort(), ["GSHARP_SIGNING_KEY", "MIXEDCASE"]);
    assert.strictEqual(resolved.allowedEnvLower.has("mixedcase"), false);
    assert.strictEqual(resolved.localOnlyEnvLower.get("mixedcase"), "MIXEDCASE");
    assert.deepStrictEqual(resolved.localOnlyEnvPrefixesLower, ["dotnet_"]);
    assert.deepStrictEqual([...resolved.ignoredEnv], ["DOTNET_ROOT"]);
    assert.deepStrictEqual(resolved.ignoredEnvPrefixesLower, ["queue_"]);
    assert.deepStrictEqual(resolved.syncExcludePrefixes, ["valid/dir/"]);
    assert.deepStrictEqual(resolved.resetCleanDirs, ["tmp/clean"]);
    assert.strictEqual(resolved.failureArtifacts.length, 1);
    assert.strictEqual(resolved.crashSummaries.length, 0);
  },

  remoteCallerEnvAllowlistAndPrecedence(assert: AssertModule): void {
    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-rcmd-env-"));
    try {
      const wt = path.join(tmp, "wt");
      fs.mkdirSync(path.join(wt, ".git"), { recursive: true });
      fs.mkdirSync(path.join(wt, "scripts"), { recursive: true });
      const script = path.join(wt, "scripts", "a.js");
      fs.writeFileSync(script, "console.log('ok');\n");

      const envPlugin: QueuePlugin = {
        name: "env-policy-test",
        apiVersion: PluginApiVersion,
        remoteOffload: () => ({
          allowedPrograms: ["node"],
          defaultEnv: { DiffEngine_Disabled: "true" },
          allowedEnv: ["TEST_SEED", "GSharpNativeSanitizers", "DiffEngine_Disabled", "GSHARP_TEST_MEMORY_DUMP"],
          localOnlyEnv: ["GSHARP_SIGNING_KEY", "GSHARP_TEST_MEMORY_REPORT", "GSHARP_JOB_QUEUE_STATE_DIR"],
        }),
      };
      const rules = new PluginHost([envPlugin], [{}], wt, () => {}).remoteOffload({
        cwd: wt,
        worktree: wt,
        configRoot: wt,
        shell: "pwsh",
        env: {},
        log: () => {},
      });
      const baseOpts = {
        cwd: wt,
        worktree: wt,
        shell: "pwsh" as const,
        places: { [script]: { root: wt, ignored: false } },
        cpus: 4,
        keepBuildServersWarm: true,
        remoteRules: rules,
      };

      // Allowlisted non-path scalars forward while standard OS env is excluded
      const fwdPlan = translateForRemote("node scripts/a.js", {
        ...baseOpts,
        platform: "linux",
        env: {
          TEST_SEED: "42",
          GSharpNativeSanitizers: "address,undefined",
          GSHARP_TEST_MEMORY_DUMP: "gcdump",
          PATH: "/usr/bin",
          HOME: "/home/dev",
          TEMP: "/tmp",
        },
      });
      assert.ok(fwdPlan !== null);
      assert.strictEqual(fwdPlan.env.TEST_SEED, "42");
      assert.strictEqual(fwdPlan.env.GSharpNativeSanitizers, "address,undefined");
      assert.strictEqual(fwdPlan.env.GSHARP_TEST_MEMORY_DUMP, "gcdump");
      assert.strictEqual(fwdPlan.env.DiffEngine_Disabled, "true");
      assert.strictEqual(fwdPlan.env.PATH, undefined);
      assert.strictEqual(fwdPlan.env.HOME, undefined);
      assert.strictEqual(fwdPlan.env.TEMP, undefined);

      for (const scalar of ["1", "true", "gcdump", "42", "en-US", "C.UTF-8", "address,undefined"]) {
        const scalarPlan = translateForRemote("node scripts/a.js", {
          ...baseOpts,
          env: { TEST_SEED: scalar },
        });
        assert.ok(scalarPlan !== null, `scalar ${scalar} must forward without path rejection`);
        assert.strictEqual(scalarPlan.env.TEST_SEED, scalar);
      }

      // Windows case canonicalization vs POSIX exact case
      const winFwd = translateForRemote("node scripts/a.js", {
        ...baseOpts,
        platform: "win32",
        env: { test_seed: "77", gsharpnativesanitizers: "address" },
      });
      assert.ok(winFwd !== null);
      assert.strictEqual(winFwd.env.TEST_SEED, "77");
      assert.strictEqual(winFwd.env.test_seed, undefined);
      assert.strictEqual(winFwd.env.GSharpNativeSanitizers, "address");
      assert.strictEqual(
        translateForRemote("node scripts/a.js", {
          ...baseOpts,
          platform: "win32",
          env: { gsharp_signing_key: "secret" },
        }),
        null,
        "win32 matches localOnlyEnv case-insensitively",
      );
      const linuxCasePlan = translateForRemote("node scripts/a.js", {
        ...baseOpts,
        platform: "linux",
        env: { test_seed: "77" },
      });
      assert.ok(linuxCasePlan !== null && linuxCasePlan.env.TEST_SEED === undefined);

      // Dual-cased caller env on Windows always prefers canonical casing regardless of key order
      for (const dualEnv of [
        { test_seed: "11", TEST_SEED: "42" },
        { TEST_SEED: "42", test_seed: "11" },
      ]) {
        const dualPlan = translateForRemote("node scripts/a.js", {
          ...baseOpts,
          platform: "win32",
          env: dualEnv,
        });
        assert.ok(dualPlan !== null);
        assert.strictEqual(dualPlan.env.TEST_SEED, "42");
        assert.strictEqual(dualPlan.env.test_seed, undefined);
      }

      // Precedence: leading.env > defaultEnv > caller.env; prepareRemoteCommand > caller.env; segEnv preserved
      const leadWins = translateForRemote("$env:TEST_SEED='99'; node scripts/a.js", {
        ...baseOpts,
        env: { TEST_SEED: "42" },
      });
      assert.ok(leadWins !== null && leadWins.env.TEST_SEED === "99");

      const winLeadCaseWins = translateForRemote("$env:test_seed='99'; node scripts/a.js", {
        ...baseOpts,
        platform: "win32",
        env: { TEST_SEED: "42" },
      });
      // win32 respells the leading name canonically, so Linux reads TEST_SEED (and only once)
      assert.ok(winLeadCaseWins !== null);
      assert.strictEqual(winLeadCaseWins.env.TEST_SEED, "99");
      assert.strictEqual(winLeadCaseWins.env.test_seed, undefined);

      const winMixedDefault = translateForRemote("$env:diffengine_disabled='false'; node scripts/a.js", {
        ...baseOpts,
        platform: "win32",
        env: {},
      });
      assert.ok(winMixedDefault !== null);
      assert.strictEqual(winMixedDefault.env.DiffEngine_Disabled, "false");
      assert.strictEqual(winMixedDefault.env.diffengine_disabled, undefined);
      // A default-only name (not allowlisted) is respelled too, and the default does not come back beside it
      const defaultOnlyRules = resolveRemoteRules([
        {
          contribution: { allowedPrograms: ["node"], defaultEnv: { JQ_DEFAULT_ONLY: "1" } },
          call: (_subHook, invoke) => invoke(),
        },
      ]);
      const winDefaultOnly = translateForRemote("$env:jq_default_only='0'; node scripts/a.js", {
        ...baseOpts,
        remoteRules: defaultOnlyRules,
        platform: "win32",
        env: {},
      });
      assert.ok(winDefaultOnly !== null);
      assert.deepStrictEqual(
        Object.keys(winDefaultOnly.env).filter((key) => key.toLowerCase() === "jq_default_only"),
        ["JQ_DEFAULT_ONLY"],
      );
      assert.strictEqual(winDefaultOnly.env.JQ_DEFAULT_ONLY, "0");
      const linuxLeadCase = translateForRemote("$env:test_seed='99'; node scripts/a.js", {
        ...baseOpts,
        platform: "linux",
        env: {},
      });
      assert.ok(
        linuxLeadCase !== null && linuxLeadCase.env.test_seed === "99" && linuxLeadCase.env.TEST_SEED === undefined,
      );

      const defaultWins = translateForRemote("node scripts/a.js", {
        ...baseOpts,
        platform: "win32",
        env: { diffengine_disabled: "false" },
      });
      assert.ok(defaultWins !== null && defaultWins.env.DiffEngine_Disabled === "true");

      const leadBeatsDefault = translateForRemote("$env:DiffEngine_Disabled='false'; node scripts/a.js", {
        ...baseOpts,
        env: { DiffEngine_Disabled: "true" },
      });
      assert.ok(leadBeatsDefault !== null && leadBeatsDefault.env.DiffEngine_Disabled === "false");

      const segEnvPlan = translateForRemote("TEST_SEED=99 node scripts/a.js", {
        ...baseOpts,
        shell: "bash",
        env: { TEST_SEED: "42" },
      });
      assert.ok(segEnvPlan !== null);
      assert.ok(segEnvPlan.bashCommand.startsWith("TEST_SEED=99 node "));
      assert.strictEqual(segEnvPlan.env.TEST_SEED, "42");

      const prepRules = new PluginHost(
        [
          {
            name: "prep-override",
            apiVersion: PluginApiVersion,
            remoteOffload: () => ({
              allowedPrograms: ["node"],
              allowedEnv: ["TEST_SEED"],
              prepareRemoteCommand: (command) => ({ command, env: { TEST_SEED: "prepared" } }),
            }),
          },
        ],
        [{}],
        wt,
        () => {},
      ).remoteOffload({ cwd: wt, worktree: wt, configRoot: wt, shell: "pwsh", env: {}, log: () => {} });
      const prepPlan = translateForRemote("node scripts/a.js", {
        ...baseOpts,
        remoteRules: prepRules,
        env: { TEST_SEED: "42" },
      });
      assert.ok(prepPlan !== null && prepPlan.env.TEST_SEED === "prepared");

      // Empty/whitespace-only string & undefined treated as unset; "0" treated as set
      const emptyPlan = translateForRemote("node scripts/a.js", {
        ...baseOpts,
        env: { TEST_SEED: "", GSHARP_SIGNING_KEY: "", GSHARP_TEST_MEMORY_REPORT: undefined },
      });
      assert.ok(emptyPlan !== null && emptyPlan.env.TEST_SEED === undefined);
      const wsUnsetPlan = translateForRemote("node scripts/a.js", {
        ...baseOpts,
        env: { TEST_SEED: "   ", GSHARP_SIGNING_KEY: "   " },
      });
      assert.ok(wsUnsetPlan !== null && wsUnsetPlan.env.TEST_SEED === undefined);
      assert.strictEqual(
        translateForRemote("node scripts/a.js", { ...baseOpts, env: { GSHARP_SIGNING_KEY: "0" } }),
        null,
      );
      const zeroSeedPlan = translateForRemote("node scripts/a.js", { ...baseOpts, env: { TEST_SEED: "0" } });
      assert.ok(zeroSeedPlan !== null && zeroSeedPlan.env.TEST_SEED === "0");

      // Path-valued allowlisted variables (including whitespace-padded paths) fail closed without rewriting
      for (const pathVal of [
        "C:\\foo",
        "D:/bar",
        "C:rel",
        "  C:rel",
        "\\\\server\\share",
        "/tmp/out",
        "tmp/out",
        ".\\out",
        "../out",
        "~/out",
        ".",
        " . ",
        "..",
        " .. ",
        "~",
        " ~ ",
        "bad\nline",
      ]) {
        assert.strictEqual(
          translateForRemote("node scripts/a.js", { ...baseOpts, env: { TEST_SEED: pathVal } }),
          null,
          `rejects path-valued TEST_SEED=${JSON.stringify(pathVal)}`,
        );
      }

      // Prototype trap keys in leading/inline/segEnv assignments and Object.prototype lookups are rejected safely
      for (const [protoCmd, protoShell] of [
        ["$env:__proto__='evil'; node scripts/a.js", "pwsh"],
        ["$env:constructor='evil'; node scripts/a.js", "pwsh"],
        ["export __proto__=evil; node scripts/a.js", "bash"],
        ["__proto__=evil node scripts/a.js", "bash"],
        ["constructor=evil node scripts/a.js", "bash"],
      ] as const) {
        assert.strictEqual(
          translateForRemote(protoCmd, { ...baseOpts, shell: protoShell, env: {} }),
          null,
          `rejects prototype trap env command: ${protoCmd}`,
        );
      }
      assert.strictEqual(getEnvValue({}, "toString", "win32"), undefined);
      assert.strictEqual(getEnvValue({}, "toString", "linux"), undefined);
      assert.strictEqual(hasEnvKey({}, "toString", "win32"), false);
      assert.strictEqual(hasEnvKey({}, "toString", "linux"), false);

      // Queue infra variables ignored (even if erroneously listed in localOnlyEnv) & case-folded on win32
      const infraPlan = translateForRemote("node scripts/a.js", {
        ...baseOpts,
        platform: "win32",
        env: {
          gsharp_job_queue_state_dir: "C:\\tmp\\state",
          GSHARP_JOB_QUEUE_REMOTE_WARM_SERVERS: "0",
          JOB_QUEUE_CONFIG: "C:\\cfg.json",
          GSHARP_GPU_LOCK_PATH: "C:\\lock",
          GSHARP_GPU_POOL_WATCH: "0",
          GSHARP_GPU_HOLD_WATCH: "0",
        },
      });
      assert.ok(infraPlan !== null);

      const warmRules = new PluginHost(
        [
          {
            name: "warm-flag-check",
            apiVersion: PluginApiVersion,
            remoteOffload: () => ({
              allowedPrograms: ["node"],
              rewriteStatement: (stmt) => ({
                args: [...stmt.args, stmt.keepBuildServersWarm ? "--warm" : "--cold"],
              }),
            }),
          },
        ],
        [{}],
        wt,
        () => {},
      ).remoteOffload({ cwd: wt, worktree: wt, configRoot: wt, shell: "pwsh", env: {}, log: () => {} });
      const winWarmOff = translateForRemote("node scripts/a.js", {
        ...baseOpts,
        remoteRules: warmRules,
        platform: "win32",
        env: { gsharp_job_queue_remote_warm_servers: "0" },
      });
      assert.ok(winWarmOff !== null && winWarmOff.bashCommand.includes("--cold"));
      const linuxWarmKept = translateForRemote("node scripts/a.js", {
        ...baseOpts,
        remoteRules: warmRules,
        platform: "linux",
        env: { gsharp_job_queue_remote_warm_servers: "0" },
      });
      assert.ok(linuxWarmKept !== null && linuxWarmKept.bashCommand.includes("--warm"));
    } finally {
      fs.rmSync(tmp, { recursive: true, force: true });
    }
  },
};
