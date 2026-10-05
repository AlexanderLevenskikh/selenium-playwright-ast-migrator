export type MigrateMode = 'analyze' | 'migrate' | 'run'

export interface Settings {
  /** Path to the migrator CLI executable. Empty = auto-detect from PATH. */
  cliPath?: string
  inputDir?: string
  configPath?: string
  outDir?: string
  target?: string
  targetTestFramework?: string
  generationPolicy?: string
  /** Extra raw CLI options appended to the command line. */
  advancedArgs?: string
}

export interface CliDetectResult {
  ok: boolean
  path: string | null
  version: string | null
  error: string | null
}

export interface RunRequest {
  mode: MigrateMode
  inputDir: string
  configPath?: string
  outDir: string
  target?: string
  targetTestFramework?: string
  generationPolicy?: string
  advancedArgs?: string
}

export type RunLogStream = 'stdout' | 'stderr'

export interface RunLogEvent {
  stream: RunLogStream
  line: string
}

export interface RunExitEvent {
  code: number | null
  signal: string | null
  durationMs: number
  ok: boolean
}

export type ConstructState =
  | 'transformed'
  | 'requires_review'
  | 'unsupported'
  | 'ambiguous'
  | 'parse_error'

export type FileState =
  | 'transformed'
  | 'requires_review'
  | 'unsupported'
  | 'ambiguous'
  | 'parse_error'
  | 'partial'
  | 'none'

export interface ConstructUnit {
  Id: string
  Kind: string
  State: ConstructState
  SourceLine: number
  OutputOperationCount: number
  OutputOperations?: string[] | null
  Reason?: string | null
  PolicySource?: string | null
}

export interface TestUnit {
  Id: string
  Name: string
  State: FileState
  ConstructCount: number
  TransformedCount: number
  ActionableCount: number
  Constructs?: ConstructUnit[]
}

export interface FileUnit {
  Id: string
  RelativePath: string
  State: FileState
  ConstructCount: number
  Transformed: number
  RequiresReview: number
  Unsupported: number
  Ambiguous: number
  ParseError: number
  SurveyComplete: boolean
  FailureMessage?: string | null
  Tests?: TestUnit[]
  SetUp?: ConstructUnit[]
}

export interface ExcludedFileUnit {
  Id: string
  RelativePath: string
  Reason: string
  PolicySource?: string
}

export interface CoverageReport {
  SchemaVersion: string
  Id: string
  SourceRootRelative?: string | null
  TargetBackend?: string | null
  SurveyStatus: 'complete' | 'partial'
  TransformationStatus: 'complete' | 'incomplete'
  AcceptanceStatus?: string | null
  DetectionComplete: boolean
  ClassificationComplete: boolean
  Files: number
  Tests: number
  Constructs: number
  Transformed: number
  RequiresReview: number
  Unsupported: number
  Ambiguous: number
  ParseError: number
  ExcludedFiles: number
  UnexplainedResidual: number
  CoverageSha256: string
  FilesDetail: FileUnit[]
  Excluded?: ExcludedFileUnit[]
}

export interface ReviewEntry {
  reviewed: boolean
  note?: string
  at: string
}

export type ReviewMap = Record<string, ReviewEntry>

export interface LegacyFileReport {
  SourceFilePath: string
  TotalTests: number
  SuccessfullyConvertedTests: number
  UnsupportedCount: number
  SemanticActions: number
  SyntaxFallbackActions: number
  MappedTargets: number
  UnmappedTargets: number
  TodoComments: number
}

/** Fallback summary read from report.json on CLI versions without coverage accounting. */
export interface ReportSummary {
  FilesProcessed: number
  TestsFound: number
  ActionsFound: number
  SemanticActions: number
  SyntaxFallbackActions: number
  UnsupportedActions: number
  MappedTargets: number
  UnmappedTargets: number
  TodoComments: number
  FilesWithWarnings: number
  GeneratedFiles: number
  PerFileReports?: LegacyFileReport[]
  TopUnmappedTargets?: { SourceExpression: string; Usages: number; ExampleLine?: number }[]
}

export interface GeneratedFileInfo {
  relativePath: string
  absolutePath: string
}

export interface DesktopApi {
  platform: string
  getSettings(): Promise<Settings>
  setSettings(patch: Partial<Settings>): Promise<Settings>
  detectCli(): Promise<CliDetectResult>
  runStart(request: RunRequest): Promise<CliDetectResult>
  runCancel(): Promise<void>
  readCoverage(outDir: string): Promise<CoverageReport | null>
  readReport(outDir: string): Promise<ReportSummary | null>
  listGenerated(outDir: string): Promise<GeneratedFileInfo[]>
  reviewGet(outDir: string, coverageSha: string): Promise<ReviewMap>
  reviewSet(
    outDir: string,
    coverageSha: string,
    key: string,
    entry: ReviewEntry
  ): Promise<ReviewMap>
  pickDirectory(): Promise<string | null>
  pickFile(): Promise<string | null>
  openPath(path: string): Promise<void>
  openFolder(path: string): Promise<void>
  onRunLog(cb: (e: RunLogEvent) => void): () => void
  onRunExit(cb: (e: RunExitEvent) => void): () => void
  /** Renderer -> main signal fired after the React tree mounts (used by smoke/UI tests). */
  notifyReady(): void
}
