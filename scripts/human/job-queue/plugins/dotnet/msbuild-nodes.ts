// Limits MSBuild parallel nodes (-m:<cpus>) on dotnet build/test/msbuild/publish/pack invocations that do not already
// specify a node limit or run a prebuilt test assembly (.dll/.exe). Moved verbatim from shell.ts.

const NodeLimitPattern = /(^|\s)(--?|\/)(m|maxcpucount|max-cpu-count)([:=\s]|$)/i;

/** The arguments of an invocation that starts at `start`: up to the next &&, ||, ;, | or line break outside quotes. */
function invocationArguments(command: string, start: number): string {
  let quote = "";
  for (let i = start; i < command.length; i++) {
    const ch = command[i];
    if (quote !== "") {
      quote = ch === quote ? "" : quote;
    } else if (ch === '"' || ch === "'") {
      quote = ch;
    } else if (ch === ";" || ch === "|" || ch === "\n" || ch === "\r" || (ch === "&" && command[i + 1] === "&")) {
      return command.slice(start, i);
    }
  }
  return command.slice(start);
}

/**
 * Adds -m:<cpus> after each dotnet build / test / msbuild / publish / pack of the command whose own arguments (up
 * to the next &&, ||, ;, | or line break) set no -m. MSBuild ignores the processor count variables and the affinity
 * of its process; only the command line limits its nodes.
 */
export function limitMsbuildNodes(command: string, cpus: number): string {
  const invocation = /((?:"[^"]*[\\/])?\bdotnet(?:\.exe)?"?)(\s+)(build|test|msbuild|publish|pack)(?![-\w.])/gi;
  return command.replace(invocation, (match: string, program: string, space: string, verb: string, offset: number) => {
    const rest = invocationArguments(command, offset + match.length);
    // dotnet test <assembly>.dll runs VSTest without MSBuild
    const vstest = verb.toLowerCase() === "test" && /(^|\s)"?[^\s"]+\.(dll|exe)"?(\s|$)/i.test(rest);
    return NodeLimitPattern.test(rest) || vstest ? match : `${program}${space}${verb} -m:${cpus}`;
  });
}
