// Unit checks for RemoteStreamSanitizer, 1-pass artifact frame parsing, and the 6-gate ArtifactCollector boundary.

import { spawnSync } from "node:child_process";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { gitBashPath } from "../shell.ts";
import {
  ArtifactCollector,
  buildArtifactPullScript,
  MaxControlSeqLen,
  parseArtifactFrames,
  RemoteStreamSanitizer,
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
        worktree: wt,
        allowReceived: true,
        knownLocalDirs: new Set(["tests/Snapshots"]),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
        failureArtifacts: [customFailureRule],
      });
      assert.deepStrictEqual(result.writtenFiles, ["tests/Snapshots/Snap.Foo.actual.txt", "tmp/trx/run1.trx"]);
      assert.strictEqual(result.rejectedFiles.length, 8, "blocks all 8 unapproved/attack paths");
      assert.strictEqual(
        fs.readFileSync(path.join(wt, "tests", "Snapshots", "Snap.Foo.actual.txt"), "utf8"),
        "actual output",
      );
      assert.strictEqual(fs.readFileSync(path.join(wt, "tmp", "trx", "run1.trx"), "utf8"), "<TestRun />");
      assert.ok(!fs.existsSync(path.join(tmp, "outside.txt")));
      assert.ok(!fs.existsSync(path.join(wt, "tests", "Snapshots", "Snap.Foo.verified.txt")));
      assert.ok(!fs.existsSync(path.join(wt, "scripts", "human", "job-queue.js")));

      // Windows reserved device names & trailing dot/space segments in Gate 1
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

      // Case-colliding frames in one payload
      const caseCollision = validateAndWriteArtifacts(
        Buffer.concat([makeFrame("tmp/trx/Case.trx", "first"), makeFrame("tmp/trx/case.trx", "second")]),
        {
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

      // Gate 5 junction/symlink escape guard along ancestor segments
      const escapeLink = path.join(wt, "tmp", "escape");
      fs.symlinkSync(outside, escapeLink, process.platform === "win32" ? "junction" : "dir");
      const junctionRes = validateAndWriteArtifacts(makeFrame("tmp/escape/pwned.trx", "boom"), {
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
        worktree: wt,
        allowReceived: true,
        knownLocalDirs: new Set(["tests/Snapshots"]),
        collectPaths: [],
      });
      assert.deepStrictEqual(noPluginRules.writtenFiles, []);
      assert.strictEqual(noPluginRules.rejectedFiles.length, 1);

      const big = validateAndWriteArtifacts(makeFrame("tmp/trx/big.trx", "x".repeat(200)), {
        worktree: wt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
        maxSingleBytes: 100,
      });
      assert.deepStrictEqual(big.writtenFiles, []);
      assert.strictEqual(big.rejectedFiles.length, 1);

      // NB3a: *.verified.* inside an explicitly allowed collectPaths dir must still be blocked by Gate 2
      const verifiedInsideCollect = validateAndWriteArtifacts(
        makeFrame("tmp/trx/Snap.Foo.verified.txt", "snapshot tamper"),
        {
          worktree: wt,
          allowReceived: true,
          knownLocalDirs: new Set(),
          collectPaths: [{ path: "tmp/trx", kind: "dir" }],
        },
      );
      assert.deepStrictEqual(verifiedInsideCollect.writtenFiles, []);
      assert.strictEqual(verifiedInsideCollect.rejectedFiles.length, 1);
      assert.ok(verifiedInsideCollect.rejectedFiles[0]?.reason.includes(".verified."));
      assert.ok(!fs.existsSync(path.join(wt, "tmp", "trx", "Snap.Foo.verified.txt")));

      // NB7: ..foo directory/files are valid paths inside worktree (not parent traversal)
      const dotdotNameRes = validateAndWriteArtifacts(makeFrame("..foo/run1.trx", "<TestRun />"), {
        worktree: wt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "..foo", kind: "dir" }],
      });
      assert.deepStrictEqual(dotdotNameRes.writtenFiles, ["..foo/run1.trx"]);
      assert.strictEqual(dotdotNameRes.rejectedFiles.length, 0);
    } finally {
      fs.rmSync(tmp, { recursive: true, force: true });
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
      fs.mkdirSync(path.join(wt, "tmp"), { recursive: true });
      const warnings: string[] = [];

      // NB1: rejected artifact path and malformed frame stream warnings strip OSC 52, SGR, CSI, and CR/LF
      validateAndWriteArtifacts(
        Buffer.concat([
          makeFrame("tmp/trx/\x1b]52;c;ZXZpbA==\x07\x1b[31mevil\x1b[0m\n.txt", "bad"),
          Buffer.from("tmp/\x1b]52;c;ZXZpbA==\x07bad\x00notanint\nhi", "utf8"),
        ]),
        {
          worktree: wt,
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

      // NB1: ArtifactCollector.pull sanitizes invalid crash summary paths & malformed frames
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
      await collector.pull(wt, true, new Set(), [], { warn: (m) => pullWarnings.push(m) });
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
      fs.rmSync(tmp, { recursive: true, force: true });
    }
  },
};
