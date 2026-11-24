// Unit checks for CommandTranslator (quoting, env rejection, cd, collectPaths, MSBuild switches, places) and rules.ts.

import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { PluginApiVersion, type QueuePlugin } from "../plugin-api.ts";
import { PluginHost } from "../plugin-host.ts";
import { translateForRemote } from "./command-translator.ts";
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

      // Broad --results-directory rejected while narrow tmp/trx allowed; /nr:false & /p:... not treated as paths
      const runnerPlugin: QueuePlugin = {
        name: "test-runner",
        apiVersion: PluginApiVersion,
        remoteOffload: () => ({
          allowedPrograms: ["dotnet"],
          pathValueOptions: ["--results-directory"],
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
      assert.ok(narrow.bashCommand.includes("/nr:false"));
      assert.ok(narrow.bashCommand.includes("/p:Configuration=Release"));

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
    const resolved = resolveRemoteRules([
      {
        contribution: {
          allowedPrograms: ["Cargo", "evil;rm", "bad space", ".", "..", "node"],
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
    assert.deepStrictEqual(resolved.syncExcludePrefixes, ["valid/dir/"]);
    assert.deepStrictEqual(resolved.resetCleanDirs, ["tmp/clean"]);
    assert.strictEqual(resolved.failureArtifacts.length, 1);
    assert.strictEqual(resolved.crashSummaries.length, 0);
  },
};
