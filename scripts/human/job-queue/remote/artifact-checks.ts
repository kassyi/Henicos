// Unit checks for RemoteStreamSanitizer, 1-pass artifact frame parsing, and the 6-gate ArtifactCollector boundary.

import { spawnSync } from "node:child_process";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { gitBashPath } from "../shell.ts";
import {
  ArtifactCollector,
  buildArtifactPullScript,
  isAllowedArtifactPath,
  MaxControlSeqLen,
  parseArtifactFrames,
  pruneQuarantineDirs,
  QuarantineFolderName,
  RemoteStreamSanitizer,
  removeReadOnlyTree,
  resolveQuarantineJobSegment,
  sanitizeRemoteLogText,
  validateAndWriteArtifacts,
} from "./artifact-collector.ts";
import { FakeRemoteTransport, makeFrame, makeRemote, type AssertModule } from "./checks-support.ts";
import { EmptyRemoteRules } from "./rules.ts";

function sanitizeAll(chunks: (string | Buffer)[]): string {
  const san = new RemoteStreamSanitizer();
  let out = "";
  for (const chunk of chunks) {
    out += san.push(chunk);
  }
  return out + san.flush();
}

export const RemoteArtifactChecks: Record<string, (assert: AssertModule) => void | Promise<void>> = {
  remoteStreamSanitizerStripsDangerousEscapes(assert: AssertModule): void {
    const san = new RemoteStreamSanitizer();
    const raw =
      "\x1b[31mFailed\x1b[0m MyTests.Crash\r\n" +
      "\x1b]52;c;ZXZpbA==\x07" +
      "\x1b]0;FakeTitle\x1b\\" +
      "\x1b[2A\x1b[2J" +
      "Error Message: テスト失敗\x08\x07\n" +
      "   at Foo.Bar() in /home/jobrunner/henicos/tests/Foo.cs:line 42\n";
    const buf = Buffer.from(raw, "utf8");
    let out = "";
    for (let i = 0; i < buf.length; i += 3) {
      out += san.push(buf.subarray(i, i + 3));
    }
    out += san.flush();
    assert.ok(out.includes("\x1b[31mFailed\x1b[0m MyTests.Crash"), "preserves SGR colors and test name");
    assert.ok(out.includes("Error Message: テスト失敗\n"), "preserves UTF-8 text and strips BS/BEL");
    assert.ok(out.includes("at Foo.Bar() in /home/jobrunner/henicos/tests/Foo.cs:line 42"), "preserves stack trace");
    assert.ok(!out.includes("52;c;"), "strips OSC 52 clipboard payload");
    assert.ok(!out.includes("FakeTitle"), "strips OSC 0 window title");
    assert.ok(!out.includes("\x1b[2A"), "strips CSI cursor movement");
    assert.ok(!out.includes("\x1b[2J"), "strips CSI screen clear");

    // Bounded unterminated OSC and DCS (caps at MaxControlSeqLen so subsequent output recovers)
    const longOsc = sanitizeAll([`\x1b]0;${"A".repeat(MaxControlSeqLen)}TAIL`]);
    assert.ok(longOsc.endsWith("TAIL") && longOsc.length < 20, "unterminated OSC resets after MaxControlSeqLen");
    assert.ok(!longOsc.includes("\x1b]"));
    const longDcs = sanitizeAll([`\x1bP${"B".repeat(MaxControlSeqLen + 5)}TAIL`]);
    assert.ok(longDcs.endsWith("TAIL") && longDcs.length < 20, "unterminated DCS resets after MaxControlSeqLen");
    assert.ok(!longDcs.includes("\x1bP"));

    // Reset on CAN (0x18), SUB (0x1a), C1 ST (U+009C), and OSC newline
    assert.strictEqual(sanitizeAll(["\x1b]0;evil\x18OK"]), "OK", "OSC resets on CAN");
    assert.strictEqual(sanitizeAll(["\x1bPdcs\x18OK"]), "OK", "DCS resets on CAN");
    assert.strictEqual(sanitizeAll(["\x1b]0;evil\x1aOK"]), "OK", "OSC resets on SUB");
    assert.strictEqual(sanitizeAll(["\x1bPdcs\x1aOK"]), "OK", "DCS resets on SUB");
    assert.strictEqual(sanitizeAll([Buffer.from("\x1b]0;evil\u009cOK", "utf8")]), "OK", "OSC resets on C1 ST");
    assert.strictEqual(sanitizeAll([Buffer.from("\x1bPdcs\u009cOK", "utf8")]), "OK", "DCS resets on C1 ST");
    assert.strictEqual(sanitizeAll(["\x1b]0;evil\nOK"]), "\nOK", "OSC resets on LF and preserves newline");
    assert.strictEqual(sanitizeAll(["\x1b]0;evil\rOK"]), "\rOK", "OSC resets on CR and preserves CR");
  },

  remoteArtifactValidatorBlocksAttacksAndWritesAllowedFiles(assert: AssertModule): void {
    // 1-pass frame parser edge cases
    const truncated = parseArtifactFrames(Buffer.from("a.txt\x0099\nhi", "utf8"));
    assert.strictEqual(truncated.frames.length, 0);
    assert.strictEqual(truncated.errors.length, 1);

    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-art-"));
    try {
      const wt = path.join(tmp, "wt");
      const qDir = path.join(tmp, "quarantine", "101");
      const outside = path.join(tmp, "outside");
      fs.mkdirSync(path.join(wt, "tests", "Snapshots"), { recursive: true });
      fs.mkdirSync(path.join(wt, "tmp"), { recursive: true });
      fs.mkdirSync(outside, { recursive: true });
      const customFailureRule = {
        searchDirs: ["tests"],
        findName: "*.actual.txt",
        allowPattern: /^tests\/[A-Za-z0-9_./-]+\.actual\.txt$/,
        requireKnownLocalDir: true,
      };
      const payload = Buffer.concat([
        makeFrame("tests/Snapshots/Snap.Foo.actual.txt", "actual output"),
        makeFrame("tmp/trx/run1.trx", "<TestRun />"),
        makeFrame("../outside.txt", "escape"),
        makeFrame("/etc/passwd", "abs"),
        makeFrame(".git/config", "git hijack"),
        makeFrame("scripts/human/job-queue.js", "rce"),
        makeFrame("tests/Snapshots/Snap.Foo.verified.txt", "snapshot tamper"),
        makeFrame("tests/Snapshots/Evil.cs", "source tamper"),
        makeFrame("tests/UnknownDir/Snap.Foo.actual.txt", "untracked dir"),
        makeFrame("tmp/trx/crash.dmp", "huge dump"),
      ]);
      const result = validateAndWriteArtifacts(payload, {
        quarantineDir: qDir,
        worktree: wt,
        allowReceived: true,
        knownLocalDirs: new Set(["tests/Snapshots"]),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
        failureArtifacts: [customFailureRule],
      });
      assert.deepStrictEqual(result.writtenFiles, ["tests/Snapshots/Snap.Foo.actual.txt", "tmp/trx/run1.trx"]);
      assert.strictEqual(result.rejectedFiles.length, 8, "blocks all 8 unapproved/attack paths");
      const actualQuarantined = path.join(qDir, "tests", "Snapshots", "Snap.Foo.actual.txt");
      const trxQuarantined = path.join(qDir, "tmp", "trx", "run1.trx");
      assert.strictEqual(fs.readFileSync(actualQuarantined, "utf8"), "actual output");
      assert.strictEqual(fs.readFileSync(trxQuarantined, "utf8"), "<TestRun />");
      assert.strictEqual(fs.statSync(actualQuarantined).mode & 0o777, 0o444, "quarantined file is read-only 0o444");
      assert.strictEqual(fs.statSync(trxQuarantined).mode & 0o777, 0o444, "quarantined trx is read-only 0o444");
      if (process.platform === "win32") {
        assert.strictEqual(
          fs.readFileSync(`${actualQuarantined}:Zone.Identifier`, "utf8"),
          "[ZoneTransfer]\r\nZoneId=3\r\n",
        );
      }
      assert.ok(!fs.existsSync(path.join(wt, "tests", "Snapshots", "Snap.Foo.actual.txt")), "never writes to worktree");
      assert.ok(!fs.existsSync(path.join(wt, "tmp", "trx", "run1.trx")), "never writes collectPaths to worktree");
      assert.ok(!fs.existsSync(path.join(tmp, "outside.txt")));
      assert.ok(!fs.existsSync(path.join(wt, "tests", "Snapshots", "Snap.Foo.verified.txt")));
      assert.ok(!fs.existsSync(path.join(wt, "scripts", "human", "job-queue.js")));

      // Windows reserved device names & trailing dot/space segments in Gate 1
      const qHygiene = path.join(tmp, "quarantine", "102");
      const winHygiene = validateAndWriteArtifacts(
        Buffer.concat([
          makeFrame("tmp/trx/CON.trx", "bad"),
          makeFrame("tmp/trx/NUL.txt", "bad"),
          makeFrame("tmp/trx/AUX/sub.trx", "bad"),
          makeFrame("tmp/trx/com1.log", "bad"),
          makeFrame("tmp/trx./run.trx", "bad"),
          makeFrame("tmp/trx /run.trx", "bad"),
          makeFrame("tmp/trx/sub./a.trx", "bad"),
          makeFrame("tmp/trx/run.trx ", "bad"),
        ]),
        {
          quarantineDir: qHygiene,
          worktree: wt,
          allowReceived: false,
          knownLocalDirs: new Set(),
          collectPaths: [
            { path: "tmp/trx", kind: "dir" },
            { path: "tmp/trx.", kind: "dir" },
          ],
        },
      );
      assert.deepStrictEqual(winHygiene.writtenFiles, []);
      assert.strictEqual(winHygiene.rejectedFiles.length, 8, "rejects Windows reserved names & trailing dot/space");
      assert.ok(!fs.existsSync(qHygiene), "lazy creation skips quarantineDir when 0 frames pass");

      // Case-colliding frames in one payload
      const caseCollision = validateAndWriteArtifacts(
        Buffer.concat([makeFrame("tmp/trx/Case.trx", "first"), makeFrame("tmp/trx/case.trx", "second")]),
        {
          quarantineDir: path.join(tmp, "quarantine", "103"),
          worktree: wt,
          allowReceived: false,
          knownLocalDirs: new Set(),
          collectPaths: [{ path: "tmp/trx", kind: "dir" }],
        },
      );
      assert.deepStrictEqual(caseCollision.writtenFiles, ["tmp/trx/Case.trx"]);
      assert.strictEqual(caseCollision.rejectedFiles.length, 1);
      assert.ok(caseCollision.rejectedFiles[0]?.reason.includes("case-colliding"));

      // knownTrackedFiles prevents overwriting tracked files even in allowed collectPaths
      const trackedRes = validateAndWriteArtifacts(
        Buffer.concat([makeFrame("tmp/trx/tracked.txt", "overwrite"), makeFrame("tmp/trx/fresh.txt", "ok")]),
        {
          quarantineDir: path.join(tmp, "quarantine", "104"),
          worktree: wt,
          allowReceived: false,
          knownLocalDirs: new Set(),
          knownTrackedFiles: new Set(["tmp/trx/tracked.txt"]),
          collectPaths: [{ path: "tmp/trx", kind: "dir" }],
        },
      );
      assert.deepStrictEqual(trackedRes.writtenFiles, ["tmp/trx/fresh.txt"]);
      assert.strictEqual(trackedRes.rejectedFiles.length, 1);
      assert.ok(trackedRes.rejectedFiles[0]?.reason.includes("tracked"));

      // Gate 3 .cs source-file guard executes BEFORE custom plugin failureArtifacts rules
      const qCs = path.join(tmp, "quarantine", "105");
      const permissiveCsRule = {
        searchDirs: ["tests"],
        findName: "*.cs",
        allowPattern: /^tests\/.*\.cs$/,
        requireKnownLocalDir: false,
      };
      const csGuard = validateAndWriteArtifacts(
        Buffer.concat([
          makeFrame("tests/Snapshots/Exploit.cs", "class Evil {}"),
          makeFrame("tests/Snapshots/Snap.Foo.received.cs", "// received"),
        ]),
        {
          quarantineDir: qCs,
          worktree: wt,
          allowReceived: true,
          knownLocalDirs: new Set(["tests/Snapshots"]),
          collectPaths: [],
          failureArtifacts: [permissiveCsRule],
        },
      );
      assert.deepStrictEqual(csGuard.writtenFiles, ["tests/Snapshots/Snap.Foo.received.cs"]);
      assert.strictEqual(csGuard.rejectedFiles.length, 1);
      assert.ok(csGuard.rejectedFiles[0]?.reason.includes("source file"));
      assert.ok(!fs.existsSync(path.join(wt, "tests", "Snapshots", "Snap.Foo.received.cs")));
      assert.strictEqual(fs.statSync(path.join(qCs, "tests", "Snapshots", "Snap.Foo.received.cs")).mode & 0o777, 0o444);

      // Gate 5 junction/symlink escape guard along ancestor segments inside quarantineDir
      const qJunction = path.join(tmp, "quarantine", "106");
      fs.mkdirSync(path.join(qJunction, "tmp"), { recursive: true });
      const escapeLink = path.join(qJunction, "tmp", "escape");
      fs.symlinkSync(outside, escapeLink, process.platform === "win32" ? "junction" : "dir");
      const junctionRes = validateAndWriteArtifacts(makeFrame("tmp/escape/pwned.trx", "boom"), {
        quarantineDir: qJunction,
        worktree: wt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "tmp/escape", kind: "dir" }],
      });
      assert.deepStrictEqual(junctionRes.writtenFiles, []);
      assert.strictEqual(junctionRes.rejectedFiles.length, 1);
      assert.ok(junctionRes.rejectedFiles[0]?.reason.includes("symlink/junction"));
      assert.ok(!fs.existsSync(path.join(outside, "pwned.trx")));

      const noPluginRules = validateAndWriteArtifacts(makeFrame("tests/Snapshots/Snap.Foo.received.txt", "x"), {
        quarantineDir: path.join(tmp, "quarantine", "107"),
        worktree: wt,
        allowReceived: true,
        knownLocalDirs: new Set(["tests/Snapshots"]),
        collectPaths: [],
      });
      assert.deepStrictEqual(noPluginRules.writtenFiles, []);
      assert.strictEqual(noPluginRules.rejectedFiles.length, 1);

      const big = validateAndWriteArtifacts(makeFrame("tmp/trx/big.trx", "x".repeat(200)), {
        quarantineDir: path.join(tmp, "quarantine", "108"),
        worktree: wt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
        maxSingleBytes: 100,
      });
      assert.deepStrictEqual(big.writtenFiles, []);
      assert.strictEqual(big.rejectedFiles.length, 1);

      // *.verified.* inside an explicitly allowed collectPaths dir must still be blocked by Gate 2
      const qVerified = path.join(tmp, "quarantine", "109");
      const verifiedInsideCollect = validateAndWriteArtifacts(
        makeFrame("tmp/trx/Snap.Foo.verified.txt", "snapshot tamper"),
        {
          quarantineDir: qVerified,
          worktree: wt,
          allowReceived: true,
          knownLocalDirs: new Set(),
          collectPaths: [{ path: "tmp/trx", kind: "dir" }],
        },
      );
      assert.deepStrictEqual(verifiedInsideCollect.writtenFiles, []);
      assert.strictEqual(verifiedInsideCollect.rejectedFiles.length, 1);
      assert.ok(verifiedInsideCollect.rejectedFiles[0]?.reason.includes(".verified."));
      assert.ok(!fs.existsSync(path.join(qVerified, "tmp", "trx", "Snap.Foo.verified.txt")));

      // ..foo directory/files are valid paths inside quarantine (not parent traversal)
      const qDotdot = path.join(tmp, "quarantine", "110");
      const dotdotNameRes = validateAndWriteArtifacts(makeFrame("..foo/run1.trx", "<TestRun />"), {
        quarantineDir: qDotdot,
        worktree: wt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "..foo", kind: "dir" }],
      });
      assert.deepStrictEqual(dotdotNameRes.writtenFiles, ["..foo/run1.trx"]);
      assert.strictEqual(dotdotNameRes.rejectedFiles.length, 0);
      assert.strictEqual(fs.readFileSync(path.join(qDotdot, "..foo", "run1.trx"), "utf8"), "<TestRun />");
      assert.ok(!fs.existsSync(path.join(wt, "..foo", "run1.trx")));
    } finally {
      removeReadOnlyTree(tmp);
    }
  },

  async remoteArtifactWarningsSanitizeAndPullUsesDashDash(assert: AssertModule): Promise<void> {
    assert.strictEqual(
      sanitizeRemoteLogText("\x1b]52;c;ZXZpbA==\x07\x1b[31mSuite.Evil\x1b[0m\r\n\x1b[2J"),
      "Suite.Evil\\u000d\\u000a",
    );

    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-art-nb1-b5-"));
    try {
      const wt = path.join(tmp, "wt");
      const stateDir = path.join(tmp, "state");
      fs.mkdirSync(path.join(wt, "tmp"), { recursive: true });
      fs.mkdirSync(stateDir, { recursive: true });
      const warnings: string[] = [];

      // Rejected artifact path and malformed frame stream warnings strip OSC 52, SGR, CSI, and CR/LF
      validateAndWriteArtifacts(
        Buffer.concat([
          makeFrame("tmp/trx/\x1b]52;c;ZXZpbA==\x07\x1b[31mevil\x1b[0m\n.txt", "bad"),
          Buffer.from("tmp/\x1b]52;c;ZXZpbA==\x07bad\x00notanint\nhi", "utf8"),
        ]),
        {
          quarantineDir: path.join(stateDir, QuarantineFolderName, "201"),
          worktree: wt,
          stateDir,
          allowReceived: false,
          knownLocalDirs: new Set(),
          collectPaths: [{ path: "tmp/trx", kind: "dir" }],
          warn: (msg) => warnings.push(msg),
        },
      );
      assert.strictEqual(warnings.length, 2);
      for (const msg of warnings) {
        assert.ok(!msg.includes("\x1b"), `no ESC in warning: ${msg}`);
        assert.ok(!msg.includes("\x07"), `no BEL in warning: ${msg}`);
        assert.ok(!msg.includes("52;c;"), `no OSC 52 payload in warning: ${msg}`);
        assert.ok(!/[\r\n]/.test(msg), `single-line warning: ${msg}`);
      }

      // ArtifactCollector.pull sanitizes invalid crash summary paths & malformed frames
      const pullWarnings: string[] = [];
      const transport = new FakeRemoteTransport(makeRemote({ workspace: "~/demo" }));
      transport.binaryHandler = () =>
        Buffer.concat([
          makeFrame("TestResults/\x1b]52;c;ZXZpbA==\x07/Sequence_1.xml", "<TestSequence />"),
          Buffer.from("TestResults/x\x00bad\nhi", "utf8"),
        ]);
      const collector = new ArtifactCollector(transport, {
        ...EmptyRemoteRules,
        crashSummaries: [
          {
            searchDirs: ["TestResults"],
            findName: "Sequence_*.xml",
            pathPattern: /(?:^|\/)TestResults\/.*Sequence_[^/]+\.xml$/i,
            cleanBeforeRun: false,
            parse: () => [],
          },
        ],
      });
      const pullRes = await collector.pull(wt, true, new Set(), [], {
        stateDir,
        jobId: 202,
        warn: (m) => pullWarnings.push(m),
      });
      assert.strictEqual(pullRes.written, 0);
      assert.ok(!fs.existsSync(pullRes.quarantineDir), "pull with 0 written frames does not create empty dir");
      assert.strictEqual(pullWarnings.length, 2);
      for (const msg of pullWarnings) {
        assert.ok(!msg.includes("\x1b") && !msg.includes("\x07") && !msg.includes("52;c;"));
      }

      // B5: buildArtifactPullScript prefixes ./ on find targets and adds -- to find, stat, and cat
      const pullScript = buildArtifactPullScript(
        "~/demo",
        true,
        [
          { path: "tmp/trx", kind: "dir" },
          { path: "-delete", kind: "dir" },
        ],
        {
          ...EmptyRemoteRules,
          failureArtifacts: [
            {
              searchDirs: ["tests"],
              findName: "*.actual.txt",
              allowPattern: /^tests\/.*\.actual\.txt$/,
              requireKnownLocalDir: true,
              cleanBeforeRun: false,
            },
          ],
        },
      );
      assert.ok(pullScript !== null);
      assert.ok(pullScript.includes("find -- ./tmp/trx "));
      assert.ok(pullScript.includes("find -- ./-delete "));
      assert.ok(pullScript.includes("find -- ./tests "));
      assert.ok(pullScript.includes('stat -c %s -- "$p"'));
      assert.ok(pullScript.includes('cat -- "$p"'));

      const bashBin = gitBashPath();
      const canRunBash =
        (process.platform !== "win32" || bashBin !== "bash") &&
        spawnSync(bashBin, ["-c", "command -v find stat cat printf >/dev/null"], { windowsHide: true }).status === 0;
      if (canRunBash) {
        const fakeHome = path.join(tmp, "home");
        const wsDir = path.join(fakeHome, "demo");
        fs.mkdirSync(path.join(wsDir, "-delete"), { recursive: true });
        fs.mkdirSync(path.join(wsDir, "tmp", "trx"), { recursive: true });
        fs.writeFileSync(path.join(wsDir, "keep.txt"), "keep", "utf8");
        fs.writeFileSync(path.join(wsDir, "-delete", "a.txt"), "dash-data", "utf8");
        fs.writeFileSync(path.join(wsDir, "tmp", "trx", "run1.trx"), "<TestRun />", "utf8");

        const res = spawnSync(bashBin, ["-c", pullScript], {
          env: { ...process.env, HOME: fakeHome.replace(/\\/g, "/") },
          windowsHide: true,
        });
        assert.strictEqual(res.status, 0, res.stderr?.toString("utf8"));
        assert.strictEqual(fs.existsSync(path.join(wsDir, "keep.txt")), true, "GNU find must not delete keep.txt");
        const parsed = parseArtifactFrames(res.stdout);
        assert.deepStrictEqual(parsed.errors, []);
        assert.deepStrictEqual(parsed.frames.map((f) => f.relPosix).sort(), ["-delete/a.txt", "tmp/trx/run1.trx"]);
      }
    } finally {
      removeReadOnlyTree(tmp);
    }
  },

  remoteQuarantineStrictAllowlistProtectionsAndRetention(assert: AssertModule): void {
    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-quarantine-"));
    try {
      const wt = path.join(tmp, "wt");
      const stateDir = path.join(tmp, "state");
      const outside = path.join(tmp, "outside");
      fs.mkdirSync(path.join(wt, "tests", "Snapshots"), { recursive: true });
      fs.mkdirSync(stateDir, { recursive: true });
      fs.mkdirSync(outside, { recursive: true });

      // 1. Job ID validation uses full numeric ID and rejects traversal / reserved segments
      assert.strictEqual(resolveQuarantineJobSegment(18739), "18739");
      assert.throws(() => resolveQuarantineJobSegment(0));
      assert.throws(() => resolveQuarantineJobSegment("0"));
      assert.throws(() => resolveQuarantineJobSegment("000"));
      assert.throws(() => resolveQuarantineJobSegment(-5));
      assert.throws(() => resolveQuarantineJobSegment("../evil"));
      assert.throws(() => resolveQuarantineJobSegment("CON"));
      assert.throws(() =>
        validateAndWriteArtifacts(makeFrame("tmp/trx/a.txt", "x"), {
          quarantineDir: wt,
          worktree: wt,
          allowReceived: false,
          knownLocalDirs: new Set(),
          collectPaths: [{ path: "tmp/trx", kind: "dir" }],
        }),
      );
      assert.throws(() =>
        validateAndWriteArtifacts(makeFrame("tmp/trx/a.txt", "x"), {
          quarantineDir: outside,
          stateDir,
          worktree: wt,
          allowReceived: false,
          knownLocalDirs: new Set(),
          collectPaths: [{ path: "tmp/trx", kind: "dir" }],
        }),
      );

      // Main-checkout containment: <wt>/tmp/job-queue/remote-quarantine/<id> is allowed when stateDir is inside wt
      const mainCheckoutState = path.join(wt, "tmp", "job-queue");
      fs.mkdirSync(mainCheckoutState, { recursive: true });
      const nestedQ = path.join(mainCheckoutState, QuarantineFolderName, "18740");
      const nestedRes = validateAndWriteArtifacts(makeFrame("tmp/trx/nested.txt", "nested"), {
        quarantineDir: nestedQ,
        stateDir: mainCheckoutState,
        worktree: wt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
      });
      assert.deepStrictEqual(nestedRes.writtenFiles, ["tmp/trx/nested.txt"]);
      assert.ok(fs.existsSync(path.join(nestedQ, "tmp", "trx", "nested.txt")));

      // 2. Strict positive text-only allowlist across both collectPaths AND permissive failureArtifacts
      const permissiveFailureRule = {
        searchDirs: ["tests"],
        findName: "*",
        allowPattern: /^tests\/.*$/,
        requireKnownLocalDir: false,
      };
      const disallowedNames = [
        "evil.dll",
        "evil.exe",
        "lib.so",
        "evil.dylib",
        "app.pdb",
        "run.ps1",
        "mod.psm1",
        "run.sh",
        "run.bash",
        "run.js",
        "run.mjs",
        "run.ts",
        "run.cmd",
        "run.bat",
        "build.binlog",
        "crash.dmp",
        "heap.gcdump",
        "page.html",
        "NOEXT",
        "Makefile",
        ".env",
        ".txt",
        ".hidden.txt",
        "..txt",
        ".received.txt",
        ".hidden.received.cs",
        "Snap.Foo.verified.txt",
        "Snap.Foo.verified.cs",
        "Evil.cs",
        "Evil.cu",
        "Evil.cpp",
        "Evil.c",
        "Evil.h",
        "Evil.hpp",
      ];
      const badCollectFrames = disallowedNames.map((name) => makeFrame(`tmp/trx/${name}`, "x"));
      // .received.cs is also blocked in collectPaths (must only arrive via failureArtifacts)
      badCollectFrames.push(makeFrame("tmp/trx/Snap.Foo.received.cs", "// received"));
      const qBadCollect = path.join(stateDir, QuarantineFolderName, "301");
      const badCollectRes = validateAndWriteArtifacts(Buffer.concat(badCollectFrames), {
        quarantineDir: qBadCollect,
        stateDir,
        worktree: wt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
      });
      assert.deepStrictEqual(badCollectRes.writtenFiles, []);
      assert.strictEqual(badCollectRes.rejectedFiles.length, badCollectFrames.length);
      assert.ok(!fs.existsSync(qBadCollect), "no empty quarantine directory created when all collect frames fail");

      const badFailureFrames = disallowedNames.map((name) => makeFrame(`tests/Snapshots/${name}`, "x"));
      const qBadFailure = path.join(stateDir, QuarantineFolderName, "302");
      const badFailureRes = validateAndWriteArtifacts(Buffer.concat(badFailureFrames), {
        quarantineDir: qBadFailure,
        stateDir,
        worktree: wt,
        allowReceived: true,
        knownLocalDirs: new Set(["tests/Snapshots"]),
        collectPaths: [],
        failureArtifacts: [permissiveFailureRule],
      });
      assert.deepStrictEqual(badFailureRes.writtenFiles, []);
      assert.strictEqual(badFailureRes.rejectedFiles.length, badFailureFrames.length);
      assert.ok(!fs.existsSync(qBadFailure), "no empty quarantine directory created when all failure frames fail");

      for (const dotCollectPath of ["tmp/trx/.hidden.txt", "tmp/trx/..txt"]) {
        assert.strictEqual(
          isAllowedArtifactPath(dotCollectPath, false, new Set(), [{ path: "tmp/trx", kind: "dir" }]).ok,
          false,
          `collectPaths rejects dotfile ${dotCollectPath}`,
        );
      }
      for (const dotFailurePath of ["tests/A/.hidden.txt", "tests/A/..txt", "tests/A/.hidden.received.cs"]) {
        assert.strictEqual(
          isAllowedArtifactPath(
            dotFailurePath,
            true,
            new Set(["tests/A"]),
            [],
            [{ searchDirs: ["tests"], findName: "*", allowPattern: /^tests\/.*$/, requireKnownLocalDir: false }],
          ).ok,
          false,
          `permissive failureArtifacts rejects dotfile ${dotFailurePath}`,
        );
      }
      assert.strictEqual(
        isAllowedArtifactPath("..foo/run1.trx", false, new Set(), [{ path: "..foo", kind: "dir" }]).ok,
        true,
        "..foo/run1.trx remains allowed",
      );

      // Allowed text extensions & *.received.* extensions pass
      const goodCollect = [
        "a.trx",
        "coverage.cobertura.xml",
        "run.coverage.xml",
        "a.xml",
        "a.log",
        "a.txt",
        "a.json",
        "a.md",
        "a.csv",
        "a.tsv",
      ];
      const goodReceived = [
        "S.received.txt",
        "S.received.cs",
        "S.received.cu",
        "S.received.cpp",
        "S.received.c",
        "S.received.h",
        "S.received.hpp",
        "S.received.json",
        "S.received.xml",
        "S.received.md",
      ];
      const qGood = path.join(stateDir, QuarantineFolderName, "303");
      const goodRes = validateAndWriteArtifacts(
        Buffer.concat([
          ...goodCollect.map((name) => makeFrame(`tmp/trx/${name}`, `ok:${name}`)),
          ...goodReceived.map((name) => makeFrame(`tests/Snapshots/${name}`, `rx:${name}`)),
        ]),
        {
          quarantineDir: qGood,
          stateDir,
          worktree: wt,
          allowReceived: true,
          knownLocalDirs: new Set(["tests/Snapshots"]),
          collectPaths: [{ path: "tmp/trx", kind: "dir" }],
          failureArtifacts: [permissiveFailureRule],
        },
      );
      assert.strictEqual(goodRes.rejectedFiles.length, 0);
      assert.strictEqual(goodRes.writtenFiles.length, goodCollect.length + goodReceived.length);

      // 3. Non-Windows ADS skip & Windows ADS failure warning-only (file kept at 0o444, never +x)
      const qLinuxSim = path.join(stateDir, QuarantineFolderName, "304");
      const adsCalls: string[] = [];
      const chmodModes: number[] = [];
      const linuxSimRes = validateAndWriteArtifacts(makeFrame("tmp/trx/out.log", "hello"), {
        quarantineDir: qLinuxSim,
        stateDir,
        worktree: wt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
        platform: "linux",
        writeAds: (p) => adsCalls.push(p),
        chmod: (p, mode) => {
          chmodModes.push(mode);
          fs.chmodSync(p, mode);
        },
      });
      assert.deepStrictEqual(linuxSimRes.writtenFiles, ["tmp/trx/out.log"]);
      assert.deepStrictEqual(adsCalls, [], "non-win32 must not attach Zone.Identifier");
      assert.deepStrictEqual(chmodModes, [0o444], "must lock quarantined file to 0o444 with zero execute bits");
      assert.strictEqual(fs.statSync(path.join(qLinuxSim, "tmp", "trx", "out.log")).mode & 0o777, 0o444);

      const qAdsFail = path.join(stateDir, QuarantineFolderName, "305");
      const adsWarnings: string[] = [];
      const adsFailRes = validateAndWriteArtifacts(makeFrame("tmp/trx/out.log", "keep-me"), {
        quarantineDir: qAdsFail,
        stateDir,
        worktree: wt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
        platform: "win32",
        writeAds: () => {
          throw new Error("ADS unsupported on volume");
        },
        warn: (m) => adsWarnings.push(m),
      });
      assert.deepStrictEqual(adsFailRes.writtenFiles, ["tmp/trx/out.log"]);
      assert.strictEqual(adsWarnings.length, 1);
      assert.ok(adsWarnings[0]?.includes("Zone.Identifier"));
      const keptPath = path.join(qAdsFail, "tmp", "trx", "out.log");
      assert.strictEqual(fs.readFileSync(keptPath, "utf8"), "keep-me");
      assert.strictEqual(fs.statSync(keptPath).mode & 0o777, 0o444);

      const qChmodFail = path.join(stateDir, QuarantineFolderName, "306");
      const chmodFailRes = validateAndWriteArtifacts(makeFrame("tmp/trx/out.log", "cleanup-me"), {
        quarantineDir: qChmodFail,
        stateDir,
        worktree: wt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
        chmod: () => {
          throw new Error("chmod denied");
        },
      });
      assert.deepStrictEqual(chmodFailRes.writtenFiles, []);
      assert.strictEqual(chmodFailRes.rejectedFiles.length, 1);
      assert.ok(!fs.existsSync(path.join(qChmodFail, "tmp", "trx", "out.log")), "chmod failure removes unlocked file");

      // 4. Poisoned pre-existing symlink/junction at <stateDir>/remote-quarantine/<jobId>, stateDir, or parentDir must reject
      const poisonedJobDir = path.join(stateDir, QuarantineFolderName, "9");
      fs.symlinkSync(outside, poisonedJobDir, process.platform === "win32" ? "junction" : "dir");
      const poisonedRes = validateAndWriteArtifacts(makeFrame("tmp/trx/pwn.trx", "evil"), {
        quarantineDir: poisonedJobDir,
        stateDir,
        worktree: wt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
      });
      assert.deepStrictEqual(poisonedRes.writtenFiles, []);
      assert.strictEqual(poisonedRes.rejectedFiles.length, 1);
      assert.ok(poisonedRes.rejectedFiles[0]?.reason.includes("symlink/junction"));
      assert.ok(!fs.existsSync(path.join(outside, "tmp", "trx", "pwn.trx")));

      const symState = path.join(tmp, "sym-state");
      fs.symlinkSync(wt, symState, process.platform === "win32" ? "junction" : "dir");
      const symStateRes = validateAndWriteArtifacts(makeFrame("tmp/trx/pwn.trx", "evil"), {
        quarantineDir: path.join(symState, QuarantineFolderName, "777"),
        stateDir: symState,
        worktree: wt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
      });
      assert.deepStrictEqual(symStateRes.writtenFiles, []);
      assert.strictEqual(symStateRes.rejectedFiles.length, 1);
      assert.ok(symStateRes.rejectedFiles[0]?.reason.includes("symlink/junction"));
      assert.ok(!fs.existsSync(path.join(wt, QuarantineFolderName, "777")));

      const symParent = path.join(tmp, "sym-parent");
      fs.symlinkSync(wt, symParent, process.platform === "win32" ? "junction" : "dir");
      const symParentRes = validateAndWriteArtifacts(makeFrame("tmp/trx/pwn.trx", "evil"), {
        quarantineDir: path.join(symParent, "778"),
        worktree: wt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
      });
      assert.deepStrictEqual(symParentRes.writtenFiles, []);
      assert.strictEqual(symParentRes.rejectedFiles.length, 1);
      assert.ok(symParentRes.rejectedFiles[0]?.reason.includes("symlink/junction"));
      assert.ok(!fs.existsSync(path.join(wt, "778")));

      // Generalized symlink/junction ancestors above parentDir / stateDir or inside worktree must reject before mkdir
      const symGrand = path.join(tmp, "sym-grand");
      fs.symlinkSync(wt, symGrand, process.platform === "win32" ? "junction" : "dir");
      const symGrandOmittedRes = validateAndWriteArtifacts(makeFrame("tmp/trx/pwn.trx", "evil"), {
        quarantineDir: path.join(symGrand, QuarantineFolderName, "780"),
        worktree: wt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
      });
      assert.deepStrictEqual(symGrandOmittedRes.writtenFiles, []);
      assert.strictEqual(symGrandOmittedRes.rejectedFiles.length, 1);
      assert.ok(symGrandOmittedRes.rejectedFiles[0]?.reason.includes("worktree"));
      assert.ok(!fs.existsSync(path.join(wt, QuarantineFolderName)));

      const symGrandState = path.join(symGrand, "job-queue");
      const symGrandStateRes = validateAndWriteArtifacts(makeFrame("tmp/trx/pwn.trx", "evil"), {
        quarantineDir: path.join(symGrandState, QuarantineFolderName, "781"),
        stateDir: symGrandState,
        worktree: wt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
      });
      assert.deepStrictEqual(symGrandStateRes.writtenFiles, []);
      assert.strictEqual(symGrandStateRes.rejectedFiles.length, 1);
      assert.ok(symGrandStateRes.rejectedFiles[0]?.reason.includes("worktree"));
      assert.ok(!fs.existsSync(path.join(wt, "job-queue")));

      const symGrandTests = path.join(tmp, "sym-grand-tests");
      fs.symlinkSync(path.join(wt, "tests"), symGrandTests, process.platform === "win32" ? "junction" : "dir");
      const symGrandTestsRes = validateAndWriteArtifacts(makeFrame("tmp/trx/pwn.trx", "evil"), {
        quarantineDir: path.join(symGrandTests, "job-queue", QuarantineFolderName, "783"),
        stateDir: path.join(symGrandTests, "job-queue"),
        worktree: wt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
      });
      assert.deepStrictEqual(symGrandTestsRes.writtenFiles, []);
      assert.strictEqual(symGrandTestsRes.rejectedFiles.length, 1);
      assert.ok(symGrandTestsRes.rejectedFiles[0]?.reason.includes("worktree"));
      assert.ok(!fs.existsSync(path.join(wt, "tests", "job-queue")));

      const wtTmpJunction = path.join(wt, "tmp-junc-root");
      fs.mkdirSync(path.join(wtTmpJunction, "tests", "Snapshots"), { recursive: true });
      fs.symlinkSync(
        path.join(wtTmpJunction, "tests"),
        path.join(wtTmpJunction, "tmp"),
        process.platform === "win32" ? "junction" : "dir",
      );
      const wtTmpState = path.join(wtTmpJunction, "tmp", "job-queue");
      const wtTmpJunctionRes = validateAndWriteArtifacts(makeFrame("tests/Snapshots/Foo.received.cs", "// evil"), {
        quarantineDir: path.join(wtTmpState, QuarantineFolderName, "782"),
        stateDir: wtTmpState,
        worktree: wtTmpJunction,
        allowReceived: true,
        knownLocalDirs: new Set(["tests/Snapshots"]),
        collectPaths: [],
        failureArtifacts: [
          {
            searchDirs: ["tests"],
            findName: "*.received.*",
            allowPattern: /^tests\/[A-Za-z0-9_./-]+\.received\.cs$/,
            requireKnownLocalDir: true,
          },
        ],
      });
      assert.deepStrictEqual(wtTmpJunctionRes.writtenFiles, []);
      assert.strictEqual(wtTmpJunctionRes.rejectedFiles.length, 1);
      assert.ok(wtTmpJunctionRes.rejectedFiles[0]?.reason.includes("symlink/junction"));
      assert.ok(!fs.existsSync(path.join(wtTmpJunction, "tests", "job-queue")));

      // Physical <wt>/tmp/job-queue/remote-quarantine/<id> and symlinked worktree-alias paths continue to succeed
      const physWt = path.join(tmp, "phys-wt");
      const physState = path.join(physWt, "tmp", "job-queue");
      fs.mkdirSync(physWt, { recursive: true });
      const physRes = validateAndWriteArtifacts(makeFrame("tmp/trx/phys.trx", "<TestRun />"), {
        quarantineDir: path.join(physState, QuarantineFolderName, "555"),
        stateDir: physState,
        worktree: physWt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
      });
      assert.deepStrictEqual(physRes.writtenFiles, ["tmp/trx/phys.trx"]);
      assert.strictEqual(physRes.rejectedFiles.length, 0);
      assert.ok(fs.existsSync(path.join(physState, QuarantineFolderName, "555", "tmp", "trx", "phys.trx")));

      const linkParent = path.join(tmp, "link-parent");
      fs.symlinkSync(physWt, linkParent, process.platform === "win32" ? "junction" : "dir");
      const aliasState = path.join(linkParent, "tmp", "job-queue");
      const aliasRes = validateAndWriteArtifacts(makeFrame("tmp/trx/alias.trx", "<TestRun />"), {
        quarantineDir: path.join(aliasState, QuarantineFolderName, "556"),
        stateDir: aliasState,
        worktree: linkParent,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
      });
      assert.deepStrictEqual(aliasRes.writtenFiles, ["tmp/trx/alias.trx"]);
      assert.strictEqual(aliasRes.rejectedFiles.length, 0);
      assert.ok(fs.existsSync(path.join(physState, QuarantineFolderName, "556", "tmp", "trx", "alias.trx")));

      // 5. Retention pruning: oldest removed first (count + age cap), 0o444 cleared, symlinks ignored, excludeName kept
      const qRoot = path.join(tmp, "retention-root");
      const symlinkEscapeTarget = path.join(tmp, "retention-outside");
      fs.mkdirSync(qRoot, { recursive: true });
      fs.mkdirSync(symlinkEscapeTarget, { recursive: true });
      fs.writeFileSync(path.join(symlinkEscapeTarget, "keep.txt"), "safe");
      const baseNow = 1_700_000_000_000;
      const makeJobDir = (name: string, ageMs: number): void => {
        const d = path.join(qRoot, name);
        fs.mkdirSync(path.join(d, "sub"), { recursive: true });
        const f = path.join(d, "sub", "out.log");
        fs.writeFileSync(f, name, { mode: 0o644 });
        fs.chmodSync(f, 0o444);
        const t = new Date(baseNow - ageMs);
        fs.utimesSync(d, t, t);
      };
      makeJobDir("1", 10 * 86_400_000); // expired by age (> 7 days)
      makeJobDir("2", 5 * 86_400_000);
      makeJobDir("3", 3 * 86_400_000);
      makeJobDir("4", 1 * 86_400_000);
      makeJobDir("5", 20 * 86_400_000); // excluded current jobId, must never be removed
      fs.symlinkSync(symlinkEscapeTarget, path.join(qRoot, "99"), process.platform === "win32" ? "junction" : "dir");

      const pruned = pruneQuarantineDirs(qRoot, {
        maxDirs: 3,
        maxAgeMs: 7 * 86_400_000,
        excludeName: "5",
        now: baseNow,
      });
      assert.deepStrictEqual(pruned.removed, ["1", "2"]);
      assert.deepStrictEqual(pruned.kept.sort(), ["3", "4", "5"]);
      assert.ok(!fs.existsSync(path.join(qRoot, "1")));
      assert.ok(!fs.existsSync(path.join(qRoot, "2")));
      assert.ok(fs.existsSync(path.join(qRoot, "3")));
      assert.ok(fs.existsSync(path.join(qRoot, "4")));
      assert.ok(fs.existsSync(path.join(qRoot, "5")), "excludeName must never be pruned");
      assert.ok(fs.existsSync(path.join(symlinkEscapeTarget, "keep.txt")), "symlink target must never be touched");

      // 6. pruneQuarantineDirs rejects symlink/junction ancestors (direct caller & F5.13 worktree/stateDir)
      //    while still pruning through a legitimate symlinked worktree alias (linkParent -> physWt)
      const directAncTarget = path.join(wt, "tests", "direct-prune-target");
      const directAncFixture = path.join(directAncTarget, QuarantineFolderName, "42", "fixture.txt");
      fs.mkdirSync(path.dirname(directAncFixture), { recursive: true });
      fs.writeFileSync(directAncFixture, "keep-direct-anc", { mode: 0o644 });
      fs.chmodSync(directAncFixture, 0o444);
      const oldTime = new Date(baseNow - 30 * 86_400_000);
      fs.utimesSync(path.join(directAncTarget, QuarantineFolderName, "42"), oldTime, oldTime);
      const symStatePrune = path.join(tmp, "sym-state-prune");
      fs.symlinkSync(directAncTarget, symStatePrune, process.platform === "win32" ? "junction" : "dir");
      const directAncWarnings: string[] = [];
      const directAncPruned = pruneQuarantineDirs(path.join(symStatePrune, QuarantineFolderName), {
        maxDirs: 1,
        maxAgeMs: 7 * 86_400_000,
        now: baseNow,
        warn: (m) => directAncWarnings.push(m),
      });
      assert.deepStrictEqual(directAncPruned.removed, []);
      assert.ok(directAncWarnings.some((w) => w.includes("symlink/junction")));
      assert.ok(
        fs.existsSync(directAncFixture),
        "direct pruneQuarantineDirs must not follow symlinked parent into target",
      );

      // Direct symlinked quarantineRoot itself preserves "not a regular directory" warning
      const symDirectRoot = path.join(tmp, "sym-direct-root");
      fs.symlinkSync(
        path.join(directAncTarget, QuarantineFolderName),
        symDirectRoot,
        process.platform === "win32" ? "junction" : "dir",
      );
      const directRootWarnings: string[] = [];
      const directRootPruned = pruneQuarantineDirs(symDirectRoot, {
        maxDirs: 1,
        maxAgeMs: 7 * 86_400_000,
        now: baseNow,
        warn: (m) => directRootWarnings.push(m),
      });
      assert.deepStrictEqual(directRootPruned.removed, []);
      assert.ok(directRootWarnings.some((w) => w.includes("not a regular directory")));
      assert.ok(fs.existsSync(directAncFixture));

      // F5.13 worktree/stateDir ancestor junction (<wt>/tmp -> <wt>/tests)
      const f513Wt = path.join(tmp, "f513-prune-wt");
      const f513KeepFile = path.join(f513Wt, "tests", "job-queue", QuarantineFolderName, "1", "keep.txt");
      fs.mkdirSync(path.dirname(f513KeepFile), { recursive: true });
      fs.writeFileSync(f513KeepFile, "keep-f513", { mode: 0o644 });
      fs.chmodSync(f513KeepFile, 0o444);
      fs.utimesSync(path.join(f513Wt, "tests", "job-queue", QuarantineFolderName, "1"), oldTime, oldTime);
      fs.symlinkSync(
        path.join(f513Wt, "tests"),
        path.join(f513Wt, "tmp"),
        process.platform === "win32" ? "junction" : "dir",
      );
      const f513StateDir = path.join(f513Wt, "tmp", "job-queue");
      const f513Warnings: string[] = [];
      const f513Pruned = pruneQuarantineDirs(path.join(f513StateDir, QuarantineFolderName), {
        maxDirs: 1,
        maxAgeMs: 7 * 86_400_000,
        now: baseNow,
        worktree: f513Wt,
        stateDir: f513StateDir,
        warn: (m) => f513Warnings.push(m),
      });
      assert.deepStrictEqual(f513Pruned.removed, []);
      assert.ok(f513Warnings.some((w) => w.includes("symlink/junction")));
      assert.ok(fs.existsSync(f513KeepFile), "F5.13 pruneQuarantineDirs must not delete via wt/tmp junction");

      // Legitimate symlinked worktree alias (linkParent -> physWt) still prunes expired quarantine dirs
      const aliasQRoot = path.join(aliasState, QuarantineFolderName);
      for (const id of ["1", "2"]) {
        const d = path.join(aliasQRoot, id);
        fs.mkdirSync(d, { recursive: true });
        const f = path.join(d, "old.log");
        fs.writeFileSync(f, id, { mode: 0o644 });
        fs.chmodSync(f, 0o444);
        const t = new Date(baseNow - (id === "1" ? 20 : 1) * 86_400_000);
        fs.utimesSync(d, t, t);
      }
      const aliasPruned = pruneQuarantineDirs(aliasQRoot, {
        maxDirs: 5,
        maxAgeMs: 7 * 86_400_000,
        now: baseNow,
        worktree: linkParent,
        stateDir: aliasState,
      });
      assert.deepStrictEqual(aliasPruned.removed, ["1"]);
      assert.ok(!fs.existsSync(path.join(physState, QuarantineFolderName, "1")));
      assert.ok(fs.existsSync(path.join(physState, QuarantineFolderName, "2", "old.log")));

      const physPruneDir1 = path.join(physState, QuarantineFolderName, "10");
      fs.mkdirSync(physPruneDir1, { recursive: true });
      fs.utimesSync(physPruneDir1, oldTime, oldTime);
      const physAliasPruned = pruneQuarantineDirs(path.join(physState, QuarantineFolderName), {
        maxDirs: 5,
        maxAgeMs: 7 * 86_400_000,
        now: baseNow,
        worktree: linkParent,
        stateDir: physState,
      });
      assert.deepStrictEqual(physAliasPruned.removed, ["10"]);
    } finally {
      removeReadOnlyTree(tmp);
    }
  },
};
