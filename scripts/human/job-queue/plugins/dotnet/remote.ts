// Remote CPU offload rules for the built-in dotnet plugin: allowed program, path/non-path option tables,
// warm build-server flag stripping, automatic lightweight `--blame`, MSBuild node limits, Verify `*.received.*`
// failure artifacts, and VSTest `Sequence_*.xml` crash attribution.

import type {
  RemoteCommandGrant,
  RemoteCommandRewrite,
  RemoteCrashSummaryRule,
  RemoteFailureArtifactRule,
  RemoteOffloadContribution,
  RemoteStatementContext,
  RemoteStatementRewrite,
} from "../../plugin-api.ts";

const MsbuildArgsVariable = "GSHARP_JOB_QUEUE_MSBUILD_ARGS";
const CoreLimitVariable = "MSBUILDCORELIMIT";
const CoreWeightVariable = "MSBUILDNODECOREALLOCATIONWEIGHT";

const DotnetPathValueOptions = [
  "--results-directory",
  "-s",
  "--settings",
  "--artifacts-path",
  "--project",
  "-d",
  "--diag",
  "--test-adapter-path",
] as const;

const DotnetNonPathValueOptions = [
  "--filter",
  "-c",
  "--configuration",
  "-f",
  "--framework",
  "-r",
  "--runtime",
  "-a",
  "--arch",
  "--os",
  "-v",
  "--verbosity",
  "-l",
  "--logger",
  "--collect",
  "--blame-crash-dump-type",
  "--blame-hang-dump-type",
  "--blame-hang-timeout",
  "--property",
] as const;

const DotnetPathExtensions = ["csproj", "fsproj", "vbproj", "sln", "slnx", "slnf", "runsettings", "cs"] as const;

/** Verify `*.received.*` failure artifact rule pulled back on non-zero exit into existing local directories only. */
export const DotnetReceivedArtifactRule: RemoteFailureArtifactRule = {
  searchDirs: ["tests", "src"],
  findName: "*.received.*",
  allowPattern: /^(?:tests|src)\/[A-Za-z0-9_./-]+\.received\.(?:txt|cs|cu|cpp|h|hpp|json|xml|md)$/,
  requireKnownLocalDir: true,
  cleanBeforeRun: true,
};

// The largest Sequence_*.xml the collector pulls back (MaxSingleArtifactBytes of remote/artifact-collector.ts): a
// full Tests.Cpu run writes about 4 MiB, and the parse is linear, so a lower cap would drop the names of a crash.
export const MaxSequenceXmlBytes = 16 * 1024 * 1024;

function isWordCharCode(code: number): boolean {
  return (code >= 65 && code <= 90) || (code >= 97 && code <= 122) || (code >= 48 && code <= 57) || code === 95;
}

/**
 * Extracts test names that did not complete (native SIGSEGV/SIGABRT or OOM crash) from VSTest `--blame`
 * `Sequence_*.xml` contents in O(n) time without regex backtracking.
 */
export function parseSequenceXmlInProgress(xml: string): string[] {
  if (xml.length > MaxSequenceXmlBytes) {
    return [];
  }
  const crashed: string[] = [];
  let pos = 0;
  while (pos < xml.length) {
    const start = xml.indexOf("<Test", pos);
    if (start < 0) {
      break;
    }
    const afterTag = start + 5;
    if (afterTag >= xml.length) {
      break;
    }
    if (isWordCharCode(xml.charCodeAt(afterTag))) {
      pos = afterTag;
      continue;
    }
    const gt = xml.indexOf(">", afterTag);
    if (gt < 0) {
      break;
    }
    if (gt > afterTag && xml.charCodeAt(gt - 1) === 47) {
      const attrs = xml.slice(afterTag, gt - 1);
      const completed = /\bCompleted="([^"]*)"/i.exec(attrs)?.[1]?.toLowerCase();
      if (completed === "false") {
        const name = /\bName="([^"]+)"/i.exec(attrs)?.[1];
        if (name !== undefined && name !== "") {
          crashed.push(name);
        }
      }
    }
    pos = gt + 1;
  }
  return crashed;
}

/** VSTest `--blame` `Sequence_*.xml` crash attribution rule parsed in memory on non-zero exit. */
export const DotnetCrashSummaryRule: RemoteCrashSummaryRule = {
  searchDirs: ["TestResults", "tests"],
  findName: "Sequence_*.xml",
  pathPattern: /(?:^|\/)TestResults\/.*Sequence_[^/]+\.xml$/i,
  cleanBeforeRun: true,
  parse: parseSequenceXmlInProgress,
};

const MsbuildVerbs = new Set(["build", "test", "msbuild", "publish", "pack"]);
const NodeLimitArgPattern = /^(?:--?|\/)(?:m|maxcpucount|max-cpu-count)(?:[:=].*)?$/i;

function rewriteDotnetStatement(stmt: RemoteStatementContext): RemoteStatementRewrite | undefined {
  if (stmt.program !== "dotnet") {
    return undefined;
  }
  const outArgs: string[] = [];
  let hasBlame = false;
  let hasListTests = false;
  let hasNodeLimit = false;
  let hasPrebuiltTest = false;
  let seenDoubleDash = false;

  for (let i = 0; i < stmt.args.length; i++) {
    const token = stmt.args[i] as string;
    if (token === "--") {
      seenDoubleDash = true;
      if (stmt.subcommand === "test" && !hasBlame && !hasListTests) {
        outArgs.push("--blame");
        hasBlame = true;
      }
      outArgs.push("--", ...stmt.args.slice(i + 1));
      break;
    }
    const lower = token.toLowerCase();
    if (lower === "--blame" || lower.startsWith("--blame-")) {
      hasBlame = true;
    } else if (lower === "--list-tests" || lower === "-t") {
      hasListTests = true;
    }
    if (i > 0) {
      if (NodeLimitArgPattern.test(token)) {
        hasNodeLimit = true;
      }
      if (stmt.subcommand === "test" && /\.(dll|exe)$/i.test(token)) {
        hasPrebuiltTest = true;
      }
    }
    // Optionally keep VBCSCompiler warm on Linux by dropping Windows file-lock workarounds
    if (stmt.keepBuildServersWarm) {
      if (lower === "--disable-build-servers" || /^[-/](nodereuse|nr)[:=]false$/.test(lower)) {
        continue;
      }
    }
    outArgs.push(token);
  }

  // Inject -m:<cpus> on parsed AST args instead of regex-rewriting bashCommand so quoted filters stay intact.
  if (stmt.cpus > 0 && MsbuildVerbs.has(stmt.subcommand) && !hasNodeLimit && !hasPrebuiltTest && outArgs.length > 0) {
    outArgs.splice(1, 0, `-m:${stmt.cpus}`);
  }

  // Automatically attach lightweight `--blame` to remote `dotnet test` so native crashes record the exact in-progress
  // test in TestResults/**/Sequence_*.xml without taking large crash dumps.
  if (!seenDoubleDash && stmt.subcommand === "test" && !hasBlame && !hasListTests) {
    outArgs.push("--blame");
  }

  return { args: outArgs };
}

function prepareDotnetRemoteCommand(command: string, grant: RemoteCommandGrant): RemoteCommandRewrite | undefined {
  if (grant.cpus <= 0) {
    return undefined;
  }
  const env: Record<string, string> = {
    [MsbuildArgsVariable]: `-m:${grant.cpus}`,
  };
  if (grant.env[CoreLimitVariable] === undefined) {
    env[CoreLimitVariable] = String(grant.cpus);
  }
  if (grant.env[CoreWeightVariable] === undefined) {
    env[CoreWeightVariable] = "100";
  }
  return {
    command,
    env,
    note: `${grant.cpus} MSBuild nodes`,
  };
}

/** Remote CPU offload contribution of the built-in dotnet plugin. */
export function dotnetRemoteOffload(): RemoteOffloadContribution {
  return {
    allowedPrograms: ["dotnet"],
    pathValueOptions: DotnetPathValueOptions,
    nonPathValueOptions: DotnetNonPathValueOptions,
    isOptionValuePath: (program, subcommand, option) =>
      program === "dotnet" && option === "-p" ? subcommand === "run" : undefined,
    pathExtensions: DotnetPathExtensions,
    collectDirOptions: ["--results-directory"],
    defaultEnv: { DiffEngine_Disabled: "true" },
    syncExcludePrefixes: ["TestResults/", "BenchmarkDotNet.Artifacts/"],
    syncExcludeFindNames: ["*.received.*", "*.user"],
    rewriteStatement: rewriteDotnetStatement,
    prepareRemoteCommand: prepareDotnetRemoteCommand,
    failureArtifacts: [DotnetReceivedArtifactRule],
    crashSummaries: [DotnetCrashSummaryRule],
    resetProcessNames: ["dotnet", "VBCSCompiler", "testhost", "vstest"],
    resetCleanDirs: ["TestResults", "BenchmarkDotNet.Artifacts"],
  };
}
