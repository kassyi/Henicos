// The word walk that the shortName hooks of plugins share: they drop words of the short column of a job.

const Separator = /^(&&|\|\||;|\|)$/;

/**
 * The short column `short` without the words `drop` answers true for (`atStart`: the word starts a step), keeping the
 * separators between the steps that are left; undefined when nothing was dropped or nothing is left.
 */
export function dropShortWords(short: string, drop: (word: string, atStart: boolean) => boolean): string | undefined {
  const words: string[] = [];
  let atStart = true;
  for (const word of short.trim().split(/\s+/)) {
    if (word === "") {
      continue;
    }
    const separator = Separator.test(word) || word.endsWith(";");
    const token = word.endsWith(";") && word.length > 1 ? word.slice(0, -1) : word;
    if (drop(token, atStart)) {
      // dropped
    } else if (!Separator.test(word)) {
      words.push(token);
      atStart = false;
    } else if (words.length > 0 && !Separator.test(words[words.length - 1] ?? "")) {
      words.push(word);
    }
    if (separator) {
      atStart = true;
      if (word.length > 1 && words.length > 0 && !Separator.test(words[words.length - 1] ?? "")) {
        words.push(";");
      }
    }
  }
  while (words.length > 0 && Separator.test(words[words.length - 1] ?? "")) {
    words.pop();
  }
  if (words.length === 0) {
    return undefined;
  }
  const next = words.join(" ");
  return next === short.trim() ? undefined : next;
}
