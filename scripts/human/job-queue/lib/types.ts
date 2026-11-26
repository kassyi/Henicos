// Pure type definitions for the shared job-queue library modules.

export interface ProcessEntry {
  ppid: number;
  /** Creation time in ms since the epoch, or -1 when Windows did not report it. */
  created: number;
}

export interface MainCheckoutModule {
  mainCheckout(fromDirectory: string, fallbackRoot: string): string;
}

export interface ProcessLivenessModule {
  readonly SpawnToleranceMs: number;
  readonly WindowsProcessQuery: string;
  isGone(target: number): boolean;
  parseProcessTable(text: string): Map<number, ProcessEntry>;
  queryWindowsProcessesSync(): Map<number, ProcessEntry> | null;
  commandProcesses(table: ReadonlyMap<number, ProcessEntry>, pid: number, spawnedAt: number): number[];
}

export interface TestProjectEntry {
  name: string;
  project: string;
}

export interface TargetDialectModule {
  stripXmlComments(text: string): string;
  testProjectFiles(testsDir: string): TestProjectEntry[];
  dialectOf(projectFile: string): "cpu" | "cuda" | null;
  remoteOffloadOf(projectFile: string): string | null;
}

export type ConfigFileStatus = "ok" | "missing" | "invalid";

export interface ConfigFileLoadResult {
  status: ConfigFileStatus;
  path: string | null;
  raw: Record<string, unknown> | null;
  error: string | null;
}

export interface ConfigFileModule {
  readonly ConfigFileName: string;
  findConfigFile(fromDirectory: string, overridePath?: string): string | null;
  loadConfigFile(fromDirectory: string, overridePath?: string): ConfigFileLoadResult;
}
