// How a job id is shown and typed: `j` and the last four digits (8739 and 18739 both show as j8739), and a reference
// that is either a full number or the last digits of one. Ids stay full numbers in the protocol and in every JSON file.

/** A job id as people read it: `j` and the last four digits. lib/resource-lock.js keeps a copy of this one line. */
export function formatJobId(id: number): string {
  return `j${String(id % 10000).padStart(4, "0")}`;
}

/** What a person typed for a job: a full id, or the last digits of one (up to four, leading zeros counted). */
export type JobRef = { kind: "exact"; id: number } | { kind: "suffix"; digits: string };

/** `j8739`, `J0234`, `#12` or `8739`: up to four digits are a suffix, more are the full id; anything else is null. */
export function parseJobRef(text: string): JobRef | null {
  const match = /^[jJ#]?(\d+)$/.exec(text);
  if (match === null) {
    return null;
  }
  const digits = match[1] as string;
  if (digits.length <= 4) {
    return { kind: "suffix", digits };
  }
  const id = Number(digits);
  return Number.isSafeInteger(id) && id > 0 ? { kind: "exact", id } : null;
}

/** Whether the last digits of the id are `digits` (a string, so that `0234` differs from `234`). */
export function matchesSuffix(id: number, digits: string): boolean {
  return id % 10 ** digits.length === Number(digits);
}

/** The job a suffix names among the jobs that run or wait and the ids that ended, or why none does. */
export function pickSuffixMatch(
  digits: string,
  live: readonly { id: number; state: string }[],
  ended: Iterable<number>,
): { id: number } | { error: string } {
  const liveMatches = live.filter((job) => matchesSuffix(job.id, digits));
  if (liveMatches.length > 1) {
    const list = liveMatches.map((job) => `${job.id} (${job.state})`).join(", ");
    return { error: `j${digits} matches more than one live job: ${list}; give the full number` };
  }
  if (liveMatches.length === 1) {
    return { id: (liveMatches[0] as { id: number }).id };
  }
  let best = 0;
  for (const id of ended) {
    if (matchesSuffix(id, digits) && id > best) {
      best = id;
    }
  }
  return best > 0 ? { id: best } : { error: `no job ends with ${digits}` };
}
