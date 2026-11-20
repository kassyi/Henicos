// Checks of how a job id is shown and typed (job-id.ts) and of the scheduler's resolution of the last digits.

import type * as Assert from "node:assert";
import * as fs from "node:fs";
import * as net from "node:net";
import * as os from "node:os";
import * as path from "node:path";
import { resolveJobRef } from "./client-commands.ts";
import { formatJobId, matchesSuffix, parseJobRef, pickSuffixMatch } from "./job-id.ts";
import { JobServer } from "./server.ts";
import { connectRaw, registration, removeDirectory } from "./self-test-support.ts";

type AssertModule = typeof Assert;

function checkFormat(assert: AssertModule): void {
  assert.strictEqual(formatJobId(1), "j0001");
  assert.strictEqual(formatJobId(8739), "j8739");
  assert.strictEqual(formatJobId(18739), "j8739");
  assert.strictEqual(formatJobId(10000), "j0000");
}

function checkParse(assert: AssertModule): void {
  assert.deepStrictEqual(parseJobRef("j8739"), { kind: "suffix", digits: "8739" });
  assert.deepStrictEqual(parseJobRef("J0234"), { kind: "suffix", digits: "0234" });
  assert.deepStrictEqual(parseJobRef("#12"), { kind: "suffix", digits: "12" });
  assert.deepStrictEqual(parseJobRef("8739"), { kind: "suffix", digits: "8739" });
  assert.deepStrictEqual(parseJobRef("18739"), { kind: "exact", id: 18739 });
  assert.deepStrictEqual(parseJobRef("j18739"), { kind: "exact", id: 18739 });
  for (const bad of ["", "j", "-1", "12a", "j 12", "1e3", "0x10", String(Number.MAX_SAFE_INTEGER) + "0"]) {
    assert.strictEqual(parseJobRef(bad), null, `"${bad}" is no job reference`);
  }
  assert.strictEqual(matchesSuffix(10234, "0234"), true);
  assert.strictEqual(matchesSuffix(234, "0234"), true, "j0234 is also job 234");
  assert.strictEqual(matchesSuffix(1234, "0234"), false);
  assert.strictEqual(matchesSuffix(1234, "34"), true, "fewer digits: the last two");
  assert.strictEqual(matchesSuffix(1235, "34"), false);
}

function checkPick(assert: AssertModule): void {
  const live = (id: number, state = "running") => ({ id, state });
  assert.deepStrictEqual(pickSuffixMatch("8739", [], [8739, 18739, 28739, 5]), { id: 28739 }, "the newest ended match");
  assert.deepStrictEqual(
    pickSuffixMatch("8739", [live(8739)], [18739]),
    { id: 8739 },
    "a live job beats a newer ended one",
  );
  assert.deepStrictEqual(
    pickSuffixMatch("8739", [live(8739), live(18739, "waiting")], []),
    { error: "j8739 matches more than one live job: 8739 (running), 18739 (waiting); give the full number" },
    "two live matches",
  );
  assert.deepStrictEqual(pickSuffixMatch("8739", [live(1)], [2]), { error: "no job ends with 8739" });
  assert.deepStrictEqual(pickSuffixMatch("39", [], [8739, 40, 139]), { id: 8739 }, "two digits");
  assert.deepStrictEqual(pickSuffixMatch("0234", [], [234, 10234]), { id: 10234 }, "leading zeros keep four digits");
  assert.deepStrictEqual(pickSuffixMatch("0234", [], [234]), { id: 234 });
}

/** The scheduler answers "resolve" from its running and waiting jobs and its recorded outcomes. */
async function checkResolveMessage(assert: AssertModule): Promise<void> {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-self-test-resolve-"));
  const server = new JobServer({
    slots: 1,
    memoryMb: 1e9,
    policy: "fifo",
    port: 0,
    stateDir: directory,
    log: () => {},
  });
  server.scheduler.holdUntil = 0;
  await new Promise<void>((resolve) => server.server.listen(0, "127.0.0.1", resolve));
  const port = (server.server.address() as net.AddressInfo).port;
  try {
    server.outcomes.set(28739, "succeeded");
    server.outcomes.set(7, "failed");
    const ask = async (digits: unknown) => {
      const client = await connectRaw(port);
      client.send({ type: "resolve", digits });
      const reply = (await client.until("resolved", 3000)) ?? (await client.until("error", 3000));
      client.close();
      return reply;
    };
    const newest = await ask("8739");
    assert.deepStrictEqual([newest?.type, newest?.id], ["resolved", 28739]);
    assert.strictEqual((await ask("0007"))?.id, 7);
    assert.match(String((await ask("1234"))?.message), /no job ends with 1234/);
    assert.strictEqual((await ask("12345"))?.type, "error", "five digits are not a suffix");
    const running = await connectRaw(port);
    running.send(registration("A", 1, process.pid));
    const granted = await running.until("granted");
    const id = Number(granted?.id);
    assert.strictEqual((await ask(String(id).padStart(4, "0")))?.id, id, "a live job");
    // A provisionally lost record is still a job a person may name (wait keeps waiting for it)
    server.freeSlots(server.records.get(id) as NonNullable<ReturnType<typeof server.records.get>>, "a check");
    assert.strictEqual((await ask(String(id).padStart(4, "0")))?.id, id, "a provisionally lost job");
    running.close();

    // The client says what the digits stood for whenever they are not the whole id, on stderr only
    server.outcomes.set(9873, "succeeded");
    const endpoint = { port, stateDir: directory, configPath: "" } as unknown as Parameters<typeof resolveJobRef>[0];
    const said = await stderrOf(async () => {
      assert.strictEqual(await resolveJobRef(endpoint, { kind: "suffix", digits: "873" }), 9873);
      assert.strictEqual(await resolveJobRef(endpoint, { kind: "suffix", digits: "8739" }), 28739);
      assert.strictEqual(await resolveJobRef(endpoint, { kind: "suffix", digits: "73" }), 9873, "the newest ended");
    });
    assert.deepStrictEqual(said.match(/j\d{4} = job \d+/g), [
      "j9873 = job 9873",
      "j8739 = job 28739",
      "j9873 = job 9873",
    ]);
  } finally {
    server.stopping = true;
    server.server.close();
    removeDirectory(directory);
  }
  // An older scheduler does not know resolve: the digits count as the full id, with a note
  const older = net.createServer((socket) => {
    socket.on("data", () =>
      socket.end(`${JSON.stringify({ type: "error", message: "unknown message type resolve" })}\n`),
    );
  });
  await new Promise<void>((resolve) => older.listen(0, "127.0.0.1", resolve));
  try {
    const olderPort = (older.address() as net.AddressInfo).port;
    const endpoint = { port: olderPort, stateDir: os.tmpdir(), configPath: "" } as unknown as Parameters<
      typeof resolveJobRef
    >[0];
    let id = 0;
    const said = await stderrOf(async () => {
      id = await resolveJobRef(endpoint, { kind: "suffix", digits: "0042" });
    });
    assert.strictEqual(id, 42, "an older scheduler: the digits as the full id");
    assert.match(said, /scheduler is older/);
  } finally {
    older.close();
  }
}

/** What `body` writes to stderr, kept off the console. */
async function stderrOf(body: () => Promise<void>): Promise<string> {
  const write = process.stderr.write;
  let text = "";
  process.stderr.write = ((chunk: string | Uint8Array) => {
    text += String(chunk);
    return true;
  }) as typeof process.stderr.write;
  try {
    await body();
  } finally {
    process.stderr.write = write;
  }
  return text;
}

export const JobIdChecks: Record<string, (assert: AssertModule) => void | Promise<void>> = {
  checkFormat,
  checkParse,
  checkPick,
  checkResolveMessage,
};
