// Henicos offload source scan: whether a test project can hold Windows-only tests (`TestPlatform.Windows` in its
// sources) and whether its built assembly is newer than those sources, read without building and fail-closed.

import { createRequire } from "node:module";
import * as fs from "node:fs";
import * as path from "node:path";
import { errorMessage } from "../job-queue/plugin-api.ts";
import type { TargetDialectModule } from "../job-queue/lib/types.ts";

const require = createRequire(import.meta.url);
const { stripXmlComments } = require("./target-dialect.js") as TargetDialectModule;

/** The text that every Windows-only test carries in its `[Platform(TestPlatform.Windows)]` attribute. */
export const WindowsPlatformMarker = "TestPlatform.Windows";
/** More `.cs` files than this in one project's scan: not judged, the job runs locally. */
export const MaxScanFiles = 5000;
/** More `.cs` bytes than this in one project's scan: not judged, the job runs locally. */
export const MaxScanBytes = 64 * 1024 * 1024;

export type SourceScan =
  /** No source of the project names the marker: no Windows-only test can be listed. */
  | { kind: "no-windows-tests" }
  /** A source names the marker; `newestInputMs` is the newest time of the sources and build files. */
  | { kind: "windows-tests"; newestInputMs: number }
  /** The sources cannot be judged (unreadable, too many, sources from outside the folder). */
  | { kind: "local"; reason: string };

/** Where the built assembly of a test step is looked for. */
export interface AssemblyQuery {
  projectFile: string;
  config: string;
  output: string;
  artifactsPath: string;
}

/** The file system reads of the offload decision; a self-test replaces them to stay offline. */
export interface OffloadSourceProbe {
  scan(projectFile: string): SourceScan;
  /** The modification times of the assemblies the list would read; empty when none is built. */
  assemblyTimes(query: AssemblyQuery): number[];
}

const SkippedDirs = new Set(["bin", "obj"]);
const BuildFileNames = ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"];

function isRepositoryRoot(dir: string): boolean {
  return fs.existsSync(path.join(dir, ".git"));
}

/** The folders from the project's folder up to the repository root (the folder that holds `.git`), inclusive. */
function foldersToRoot(projectDir: string): string[] {
  const folders: string[] = [];
  let current = path.resolve(projectDir);
  for (;;) {
    folders.push(current);
    const parent = path.dirname(current);
    if (isRepositoryRoot(current) || parent === current) {
      return folders;
    }
    current = parent;
  }
}

/**
 * The files a `Directory.Build.*` file adds with `<Compile Include="...">`, its properties (`<Name>value</Name>` of
 * the same file) and `$(MSBuildThisFileDirectory)` substituted; null when an item cannot be resolved to one file.
 */
export function directoryCompileItems(buildFile: string, text: string): string[] | null {
  const xml = stripXmlComments(text);
  const properties = new Map<string, string>();
  properties.set("msbuildthisfiledirectory", `${path.dirname(buildFile)}${path.sep}`);
  for (const match of xml.matchAll(/<([A-Za-z_][\w.-]*)>([^<]*)<\/\1>/g)) {
    properties.set((match[1] as string).toLowerCase(), (match[2] as string).trim());
  }
  const items: string[] = [];
  for (const match of xml.matchAll(/<Compile\s[^>]*\bInclude\s*=\s*"([^"]*)"/gi)) {
    let value = match[1] as string;
    for (let pass = 0; pass < 8 && value.includes("$("); pass++) {
      value = value.replace(
        /\$\(([A-Za-z_][\w.-]*)\)/g,
        (whole, name: string) => properties.get(name.toLowerCase()) ?? whole,
      );
    }
    if (value.trim() === "" || /[$*?;%@]/.test(value)) {
      return null;
    }
    items.push(path.resolve(path.dirname(buildFile), value.trim()));
  }
  return items;
}

interface ScanState {
  readonly limits: ScanLimits;
  files: number;
  bytes: number;
  marker: boolean;
  newestMs: number;
}

/** The bounds of one scan; a self-test lowers them. */
export interface ScanLimits {
  readonly files: number;
  readonly bytes: number;
}

const DefaultScanLimits: ScanLimits = { files: MaxScanFiles, bytes: MaxScanBytes };

function overLimit(state: ScanState): boolean {
  return state.files > state.limits.files || state.bytes > state.limits.bytes;
}

function limitText(state: ScanState): string {
  return `more than ${state.limits.files} source files or ${state.limits.bytes} bytes`;
}

/** Reads one `.cs` file into the state; false when it cannot be read. */
function scanSource(file: string, state: ScanState): boolean {
  try {
    const stat = fs.statSync(file);
    state.files++;
    state.bytes += stat.size;
    state.newestMs = Math.max(state.newestMs, stat.mtimeMs);
    if (!state.marker && !overLimit(state)) {
      state.marker = fs.readFileSync(file, "utf8").includes(WindowsPlatformMarker);
    }
    return true;
  } catch {
    // Not readable: the caller cannot judge the project and runs it locally
    return false;
  }
}

/** Walks the project's folder (without `bin` / `obj`, never through a link); a reason string when it cannot. */
function walkProject(dir: string, state: ScanState): string | null {
  let entries: fs.Dirent[];
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch (error) {
    return `cannot read ${dir}: ${errorMessage(error)}`;
  }
  for (const entry of entries) {
    const full = path.join(dir, entry.name);
    if (entry.isSymbolicLink()) {
      return `${full} is a link`;
    }
    if (entry.isDirectory()) {
      if (SkippedDirs.has(entry.name.toLowerCase())) {
        continue;
      }
      const inner = walkProject(full, state);
      if (inner !== null) {
        return inner;
      }
    } else if (entry.isFile()) {
      const lower = entry.name.toLowerCase();
      if (lower.endsWith(".cs") && !scanSource(full, state)) {
        return `cannot read ${full}`;
      }
      if (lower.endsWith(".csproj")) {
        try {
          state.newestMs = Math.max(state.newestMs, fs.statSync(full).mtimeMs);
        } catch {
          return `cannot read ${full}`;
        }
      }
    }
    if (overLimit(state)) {
      return limitText(state);
    }
  }
  return null;
}

/**
 * Scans a test project's sources for `TestPlatform.Windows`: the `.cs` files under its folder (without `bin` and
 * `obj`) and those a `Directory.Build.*` file between the folder and the repository root compiles in. A project file
 * that compiles sources itself (`<Compile Include=`) or imports a shared project (`.projitems`) is not judged.
 */
export function scanProjectSources(projectFile: string, limits: ScanLimits = DefaultScanLimits): SourceScan {
  const name = path.basename(projectFile, path.extname(projectFile));
  const local = (why: string): SourceScan => ({ kind: "local", reason: `cannot scan sources of ${name} (${why})` });
  let projectXml: string;
  try {
    projectXml = stripXmlComments(fs.readFileSync(projectFile, "utf8"));
  } catch (error) {
    return local(errorMessage(error));
  }
  if (/<Compile\s[^>]*\bInclude\s*=/i.test(projectXml) || /<Import\s[^>]*\.projitems\b/i.test(projectXml)) {
    return local("it compiles sources from outside its folder");
  }
  const state: ScanState = { limits, files: 0, bytes: 0, marker: false, newestMs: 0 };
  const walked = walkProject(path.dirname(projectFile), state);
  if (walked !== null) {
    return local(walked);
  }
  for (const folder of foldersToRoot(path.dirname(projectFile))) {
    for (const buildName of BuildFileNames) {
      const buildFile = path.join(folder, buildName);
      let text: string;
      try {
        if (!fs.existsSync(buildFile)) {
          continue;
        }
        state.newestMs = Math.max(state.newestMs, fs.statSync(buildFile).mtimeMs);
        text = fs.readFileSync(buildFile, "utf8");
      } catch (error) {
        return local(errorMessage(error));
      }
      const items = directoryCompileItems(buildFile, text);
      if (items === null) {
        return local(`${buildName} in ${folder} compiles an item it cannot resolve`);
      }
      for (const item of items) {
        if (!scanSource(item, state)) {
          return local(`cannot read ${item}`);
        }
      }
    }
  }
  if (overLimit(state)) {
    return local(limitText(state));
  }
  return state.marker ? { kind: "windows-tests", newestInputMs: state.newestMs } : { kind: "no-windows-tests" };
}

function findNamed(directory: string, fileName: string, depth = 0): string[] {
  let entries: fs.Dirent[];
  try {
    entries = fs.readdirSync(directory, { withFileTypes: true });
  } catch {
    // Not built (no output folder): nothing to find
    return [];
  }
  const found: string[] = [];
  for (const entry of entries) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory() && depth < 8) {
      found.push(...findNamed(full, fileName, depth + 1));
    } else if (entry.isFile() && entry.name.toLowerCase() === fileName.toLowerCase()) {
      found.push(full);
    }
  }
  return found;
}

/**
 * The times of `<project name>.dll` in the step's output: under `-o` or `--artifacts-path` when given, else under the
 * project's `bin` in a folder named after the configuration.
 */
export function builtAssemblyTimes(query: AssemblyQuery): number[] {
  const fileName = `${path.basename(query.projectFile, path.extname(query.projectFile))}.dll`;
  const custom = query.output !== "" ? query.output : query.artifactsPath;
  const candidates =
    custom !== ""
      ? findNamed(custom, fileName)
      : findNamed(path.join(path.dirname(query.projectFile), "bin"), fileName).filter((file) =>
          file.toLowerCase().split(/[\\/]/).includes(query.config.toLowerCase()),
        );
  const times: number[] = [];
  for (const file of candidates) {
    try {
      times.push(fs.statSync(file).mtimeMs);
    } catch {
      // Removed between the walk and the read: the remaining ones decide, none means not built
    }
  }
  return times;
}

export const realSourceProbe: OffloadSourceProbe = {
  scan: scanProjectSources,
  assemblyTimes: builtAssemblyTimes,
};
