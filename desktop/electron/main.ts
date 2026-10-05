import {
  app,
  BrowserWindow,
  dialog,
  ipcMain,
  shell,
} from 'electron'
import { spawn, execFile } from 'node:child_process'
import { promises as fs } from 'node:fs'
import * as fssync from 'node:fs'
import path from 'node:path'
import os from 'node:os'
import crypto from 'node:crypto'
import { fileURLToPath } from 'node:url'
import { promisify } from 'node:util'
import type {
  CliDetectResult,
  ConstructUnit,
  CoverageReport,
  FileUnit,
  GeneratedFileInfo,
  ReviewEntry,
  ReviewMap,
  RunLogEvent,
  RunRequest,
  Settings,
  TestUnit,
} from './types.js'

const execFileAsync = promisify(execFile)

/* ESM build has no __dirname; derive it from the module URL. */
const __dirname = path.dirname(fileURLToPath(import.meta.url))

let mainWindow: BrowserWindow | null = null
let activeProc: ReturnType<typeof spawn> | null = null

// ---------------------------------------------------------------- settings

function settingsFile(): string {
  return path.join(app.getPath('userData'), 'settings.json')
}

async function loadSettings(): Promise<Settings> {
  try {
    return JSON.parse(await fs.readFile(settingsFile(), 'utf8')) as Settings
  } catch {
    return {}
  }
}

async function saveSettings(s: Settings): Promise<void> {
  await fs.mkdir(path.dirname(settingsFile()), { recursive: true })
  await fs.writeFile(settingsFile(), JSON.stringify(s, null, 2), 'utf8')
}

// ------------------------------------------------------------- CLI helpers

function spawnCli(cliPath: string, args: string[]) {
  const ext = path.extname(cliPath).toLowerCase()
  const useShell = ext === '.cmd' || ext === '.bat'
  return spawn(cliPath, args, {
    shell: useShell,
    windowsHide: true,
    env: process.env,
  })
}

function runCliCapture(
  cliPath: string,
  args: string[],
  timeoutMs: number
): Promise<{ stdout: string; stderr: string; code: number | null }> {
  return new Promise((resolve, reject) => {
    const child = spawnCli(cliPath, args)
    let out = ''
    let err = ''
    const timer = setTimeout(() => {
      child.kill()
      reject(new Error(`CLI command timed out after ${timeoutMs}ms`))
    }, timeoutMs)
    child.stdout?.on('data', (d: Buffer) => (out += String(d)))
    child.stderr?.on('data', (d: Buffer) => (err += String(d)))
    child.on('error', (e) => {
      clearTimeout(timer)
      reject(e)
    })
    child.on('close', (code) => {
      clearTimeout(timer)
      resolve({ stdout: out, stderr: err, code })
    })
  })
}

async function findOnPath(name: string): Promise<string | null> {
  const cmd = process.platform === 'win32' ? 'where' : 'which'
  try {
    const { stdout } = await execFileAsync(cmd, [name], { timeout: 8000 })
    const first = stdout
      .split(/\r?\n/)
      .map((s) => s.trim())
      .find(Boolean)
    return first || null
  } catch {
    return null
  }
}

async function getCliVersion(cliPath: string): Promise<string | null> {
  try {
    const r = await runCliCapture(cliPath, ['--version'], 8000)
    const text = (r.stdout + '\n' + r.stderr).trim()
    const first = text.split(/\r?\n/).find(Boolean)
    return (first || null)?.slice(0, 200) ?? null
  } catch {
    return null
  }
}

async function detectCli(settings: Settings): Promise<CliDetectResult> {
  const candidate = settings.cliPath?.trim()
  if (candidate) {
    const abs = path.resolve(candidate)
    if (fssync.existsSync(abs)) {
      const version = await getCliVersion(abs)
      return { ok: true, path: abs, version, error: null }
    }
    return {
      ok: false,
      path: null,
      version: null,
      error: `Не найден файл по указанному пути: ${abs}`,
    }
  }
  const fromPath = await findOnPath('selenium-pw-migrator')
  if (fromPath) {
    const version = await getCliVersion(fromPath)
    return { ok: true, path: fromPath, version, error: null }
  }
  const homeBin = path.join(
    os.homedir(),
    '.selenium-pw-migrator',
    'bin',
    process.platform === 'win32' ? 'selenium-pw-migrator.exe' : 'selenium-pw-migrator'
  )
  if (fssync.existsSync(homeBin)) {
    const version = await getCliVersion(homeBin)
    return { ok: true, path: homeBin, version, error: null }
  }
  return {
    ok: false,
    path: null,
    version: null,
    error:
      'Не удалось найти selenium-pw-migrator. Установите CLI (`npm i -g selenium-pw-migrator`) либо укажите путь к исполняемому файлу в настройках.',
  }
}

function buildArgs(r: RunRequest): string[] {
  const args = ['--mode', r.mode, '--input', r.inputDir]
  if (r.configPath?.trim()) args.push('--config', r.configPath.trim())
  if (r.target?.trim()) args.push('--target', r.target.trim())
  if (r.targetTestFramework?.trim())
    args.push('--target-test-framework', r.targetTestFramework.trim())
  if (r.generationPolicy?.trim())
    args.push('--generation-policy', r.generationPolicy.trim())
  args.push('--out', r.outDir, '--format', 'both')
  if (r.advancedArgs?.trim()) args.push(...r.advancedArgs.trim().split(/\s+/))
  return args
}

function send(channel: string, payload: unknown): void {
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.webContents.send(channel, payload)
  }
}

function splitLines(chunk: string): string[] {
  return chunk.split(/\r?\n/)
}

// --------------------------------------------------------------- artifacts

async function readCoverage(outDir: string): Promise<CoverageReport | null> {
  const p = path.join(outDir, 'coverage-report.json')
  try {
    const raw = JSON.parse(await fs.readFile(p, 'utf8')) as unknown
    return normalizeCoverage(raw)
  } catch {
    return null
  }
}

/* The CLI serializes the report with System.Text.Json camelCase naming; map it to the
   PascalCase contract used across the app (and documented in the JSON schema). */
function normalizeCoverage(raw: unknown): CoverageReport | null {
  if (!raw || typeof raw !== 'object') return null
  const r = raw as Record<string, unknown>
  const num = (v: unknown, d = 0): number =>
    typeof v === 'number' ? v : typeof v === 'string' && v.trim() !== '' ? Number(v) || d : d
  const str = (v: unknown): string | null => (typeof v === 'string' ? v : null)
  const bool = (v: unknown, d = false): boolean => (typeof v === 'boolean' ? v : d)

  const normConstruct = (c: Record<string, unknown>): ConstructUnit => ({
    Id: str(c['id']) ?? str(c['Id']) ?? '',
    Kind: str(c['kind']) ?? str(c['Kind']) ?? '',
    State: (str(c['state']) ?? str(c['State']) ?? 'requires_review') as ConstructUnit['State'],
    SourceLine: num(c['sourceLine'] ?? c['SourceLine']),
    OutputOperationCount: num(c['outputOperationCount'] ?? c['OutputOperationCount']),
    OutputOperations:
      Array.isArray(c['outputOperations']) ? (c['outputOperations'] as string[]) : null,
    Reason: str(c['reason'] ?? c['Reason']),
    PolicySource: str(c['policySource'] ?? c['PolicySource']),
  })

  const normTest = (t: Record<string, unknown>): TestUnit => ({
    Id: str(t['id']) ?? str(t['Id']) ?? '',
    Name: str(t['name']) ?? str(t['Name']) ?? '',
    State: (str(t['state']) ?? str(t['State']) ?? 'none') as TestUnit['State'],
    ConstructCount: num(t['constructCount'] ?? t['ConstructCount']),
    TransformedCount: num(t['transformedCount'] ?? t['TransformedCount']),
    ActionableCount: num(t['actionableCount'] ?? t['ActionableCount']),
    Constructs: Array.isArray(t['constructs'])
      ? (t['constructs'] as Record<string, unknown>[]).map(normConstruct)
      : undefined,
  })

  const normFile = (f: Record<string, unknown>): FileUnit => ({
    Id: str(f['id']) ?? str(f['Id']) ?? '',
    RelativePath: str(f['relativePath']) ?? str(f['RelativePath']) ?? '',
    State: (str(f['state']) ?? str(f['State']) ?? 'none') as FileUnit['State'],
    ConstructCount: num(f['constructCount'] ?? f['ConstructCount']),
    Transformed: num(f['transformed'] ?? f['Transformed']),
    RequiresReview: num(f['requiresReview'] ?? f['RequiresReview']),
    Unsupported: num(f['unsupported'] ?? f['Unsupported']),
    Ambiguous: num(f['ambiguous'] ?? f['Ambiguous']),
    ParseError: num(f['parseError'] ?? f['ParseError']),
    SurveyComplete: bool(f['surveyComplete'] ?? f['SurveyComplete']),
    FailureMessage: str(f['failureMessage'] ?? f['FailureMessage']),
    Tests: Array.isArray(f['tests'])
      ? (f['tests'] as Record<string, unknown>[]).map(normTest)
      : undefined,
    SetUp: Array.isArray(f['setUp'])
      ? (f['setUp'] as Record<string, unknown>[]).map(normConstruct)
      : undefined,
  })

  const report: CoverageReport = {
    SchemaVersion: str(r['schemaVersion'] ?? r['SchemaVersion']) ?? 'migrator-coverage/v1',
    Id: str(r['id'] ?? r['Id']) ?? '',
    SourceRootRelative: str(r['sourceRootRelative'] ?? r['SourceRootRelative']),
    TargetBackend: str(r['targetBackend'] ?? r['TargetBackend']),
    SurveyStatus: (str(r['surveyStatus'] ?? r['SurveyStatus']) ?? 'partial') as CoverageReport['SurveyStatus'],
    TransformationStatus: (str(r['transformationStatus'] ?? r['TransformationStatus']) ??
      'incomplete') as CoverageReport['TransformationStatus'],
    AcceptanceStatus: str(r['acceptanceStatus'] ?? r['AcceptanceStatus']) ?? null,
    DetectionComplete: bool(r['detectionComplete'] ?? r['DetectionComplete']),
    ClassificationComplete: bool(r['classificationComplete'] ?? r['ClassificationComplete']),
    Files: num(r['files'] ?? r['Files']),
    Tests: num(r['tests'] ?? r['Tests']),
    Constructs: num(r['constructs'] ?? r['Constructs']),
    Transformed: num(r['transformed'] ?? r['Transformed']),
    RequiresReview: num(r['requiresReview'] ?? r['RequiresReview']),
    Unsupported: num(r['unsupported'] ?? r['Unsupported']),
    Ambiguous: num(r['ambiguous'] ?? r['Ambiguous']),
    ParseError: num(r['parseError'] ?? r['ParseError']),
    ExcludedFiles: num(r['excludedFiles'] ?? r['ExcludedFiles']),
    UnexplainedResidual: num(r['unexplainedResidual'] ?? r['UnexplainedResidual']),
    CoverageSha256: str(r['coverageSha256'] ?? r['CoverageSha256']) ?? '',
    FilesDetail: Array.isArray(r['filesDetail'])
      ? (r['filesDetail'] as Record<string, unknown>[]).map(normFile)
      : [],
    Excluded: Array.isArray(r['excluded'])
      ? (r['excluded'] as Record<string, unknown>[]).map((x) => ({
          Id: str(x['id']) ?? str(x['Id']) ?? '',
          RelativePath: str(x['relativePath']) ?? str(x['RelativePath']) ?? '',
          Reason: str(x['reason']) ?? str(x['Reason']) ?? '',
          PolicySource: str(x['policySource']) ?? str(x['PolicySource']) ?? undefined,
        }))
      : undefined,
  }
  return report
}

async function readReport(outDir: string): Promise<unknown> {
  const p = path.join(outDir, 'report.json')
  try {
    return JSON.parse(await fs.readFile(p, 'utf8'))
  } catch {
    return null
  }
}

async function listGenerated(outDir: string): Promise<GeneratedFileInfo[]> {
  const result: GeneratedFileInfo[] = []
  const generatedName = /Playwright\.(cs|ts)$/i
  async function walk(dir: string): Promise<void> {
    let entries
    try {
      entries = await fs.readdir(dir, { withFileTypes: true })
    } catch {
      return
    }
    for (const en of entries) {
      const full = path.join(dir, en.name)
      if (en.isDirectory()) {
        if (en.name === 'node_modules' || en.name === '.git') continue
        await walk(full)
      } else if (generatedName.test(en.name)) {
        result.push({ relativePath: path.relative(outDir, full), absolutePath: full })
      }
    }
  }
  await walk(outDir)
  result.sort((a, b) => a.relativePath.localeCompare(b.relativePath))
  return result
}

// --------------------------------------------------------- review sidecar

function reviewFile(): string {
  return path.join(app.getPath('userData'), 'review-state.json')
}

type ReviewStore = Record<string, Record<string, ReviewMap>>

async function loadReviews(): Promise<ReviewStore> {
  try {
    return JSON.parse(await fs.readFile(reviewFile(), 'utf8')) as ReviewStore
  } catch {
    return {}
  }
}

function outKey(outDir: string): string {
  return crypto
    .createHash('sha256')
    .update(path.resolve(outDir).toLowerCase())
    .digest('hex')
}

// ------------------------------------------------------------------- IPC

function isTrustedSender(frame: Electron.WebFrameMain | null): boolean {
  if (!frame) return false
  const url = frame.url
  if (!url) return false
  if (process.env.VITE_DEV_SERVER_URL) return url.startsWith(process.env.VITE_DEV_SERVER_URL)
  return url.startsWith('file://')
}

function registerIpc(): void {
  ipcMain.handle('settings:get', (e) =>
    isTrustedSender(e.senderFrame) ? loadSettings() : null
  )
  ipcMain.handle('settings:set', async (e, patch: Partial<Settings>) => {
    if (!isTrustedSender(e.senderFrame)) return null
    const next = { ...(await loadSettings()), ...patch }
    await saveSettings(next)
    return next
  })
  ipcMain.handle('cli:detect', async (e) =>
    isTrustedSender(e.senderFrame) ? detectCli(await loadSettings()) : null
  )
  ipcMain.handle('run:start', async (e, request: RunRequest) => {
    if (!isTrustedSender(e.senderFrame)) return null
    if (activeProc) {
      return { ok: false, path: null, version: null, error: 'Прогон уже выполняется' }
    }
    const det = await detectCli(await loadSettings())
    if (!det.ok || !det.path) return det
    const args = buildArgs(request)
    send('run:log', { stream: 'stdout', line: `> ${det.path} ${args.join(' ')}` })
    const started = Date.now()
    const proc = spawnCli(det.path, args)
    activeProc = proc
    proc.stdout?.on('data', (d: Buffer) => {
      for (const line of splitLines(String(d))) send('run:log', { stream: 'stdout', line })
    })
    proc.stderr?.on('data', (d: Buffer) => {
      for (const line of splitLines(String(d))) send('run:log', { stream: 'stderr', line })
    })
    proc.on('error', (err) => {
      send('run:log', { stream: 'stderr', line: String(err) })
    })
    proc.on('close', (code, signal) => {
      activeProc = null
      send('run:exit', {
        code,
        signal,
        durationMs: Date.now() - started,
        ok: code === 0,
      })
    })
    return { ok: true, path: det.path, version: det.version, error: null }
  })
  ipcMain.handle('run:cancel', (e) => {
    if (!isTrustedSender(e.senderFrame)) return
    if (activeProc) activeProc.kill()
  })
  ipcMain.handle('artifact:coverage', async (e, outDir: string) =>
    isTrustedSender(e.senderFrame) ? readCoverage(outDir) : null
  )
  ipcMain.handle('artifact:report', async (e, outDir: string) =>
    isTrustedSender(e.senderFrame) ? readReport(outDir) : null
  )
  ipcMain.handle('artifact:generated', async (e, outDir: string) =>
    isTrustedSender(e.senderFrame) ? listGenerated(outDir) : []
  )
  ipcMain.handle('review:get', async (e, outDir: string, coverageSha: string) => {
    if (!isTrustedSender(e.senderFrame)) return {}
    const all = await loadReviews()
    return all[outKey(outDir)]?.[coverageSha] ?? {}
  })
  ipcMain.handle(
    'review:set',
    async (
      e,
      outDir: string,
      coverageSha: string,
      key: string,
      entry: ReviewEntry
    ) => {
      if (!isTrustedSender(e.senderFrame)) return {}
      const all = await loadReviews()
      const okey = outKey(outDir)
      all[okey] ??= {}
      all[okey][coverageSha] ??= {}
      all[okey][coverageSha][key] = entry
      await fs.mkdir(path.dirname(reviewFile()), { recursive: true })
      await fs.writeFile(reviewFile(), JSON.stringify(all, null, 2), 'utf8')
      return all[okey][coverageSha]
    }
  )
  ipcMain.handle('shell:openPath', (e, p: string) => {
    if (!isTrustedSender(e.senderFrame)) return
    return shell.openPath(p)
  })
  ipcMain.handle('shell:openFolder', (e, p: string) => {
    if (!isTrustedSender(e.senderFrame)) return
    return shell.openPath(p)
  })
  ipcMain.handle('dialog:pickDirectory', async (e) => {
    if (!isTrustedSender(e.senderFrame)) return null
    const opts: Electron.OpenDialogOptions = {
      title: 'Выберите папку',
      properties: ['openDirectory', 'createDirectory'],
    }
    const r =
      mainWindow && !mainWindow.isDestroyed()
        ? await dialog.showOpenDialog(mainWindow, opts)
        : await dialog.showOpenDialog(opts)
    return r.canceled || r.filePaths.length === 0 ? null : r.filePaths[0]
  })
  ipcMain.handle('dialog:pickFile', async (e) => {
    if (!isTrustedSender(e.senderFrame)) return null
    const opts: Electron.OpenDialogOptions = {
      title: 'Выберите файл',
      properties: ['openFile'],
      filters: [{ name: 'Adapter config', extensions: ['json'] }],
    }
    const r =
      mainWindow && !mainWindow.isDestroyed()
        ? await dialog.showOpenDialog(mainWindow, opts)
        : await dialog.showOpenDialog(opts)
    return r.canceled || r.filePaths.length === 0 ? null : r.filePaths[0]
  })
}

// ---------------------------------------------------------------- window

function createWindow(): void {
  mainWindow = new BrowserWindow({
    width: 1320,
    height: 900,
    minWidth: 960,
    minHeight: 620,
    show: false,
    title: 'Selenium → Playwright Migrator',
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  })
  mainWindow.once('ready-to-show', () => {
    mainWindow?.show()
  })
  const devUrl = process.env.VITE_DEV_SERVER_URL
  if (devUrl) {
    void mainWindow.loadURL(devUrl)
  } else {
    void mainWindow.loadFile(path.join(__dirname, '..', 'dist', 'index.html'))
  }
  mainWindow.on('closed', () => {
    mainWindow = null
  })
}

// ------------------------------------------------------------------ smoke

async function runSmoke(): Promise<void> {
  registerIpc()
  const settings = await loadSettings()
  const det = await detectCli(settings)
  const idx = process.argv.indexOf('--smoke-out')
  let coverageOk: boolean | null = null
  let coverageFiles = 0
  if (idx !== -1 && process.argv[idx + 1]) {
    const cov = await readCoverage(path.resolve(process.argv[idx + 1]))
    coverageOk = cov !== null
    coverageFiles = cov?.Files ?? 0
  }
  const result = { cli: det, coverageOk, coverageFiles }

  if (process.argv.includes('--smoke-ui')) {
    const uiOk = await smokeLoadWindow()
    console.log('MIGRATOR-DESKTOP-SMOKE ' + JSON.stringify({ ...result, ui: uiOk }))
    app.exit(uiOk ? 0 : 1)
    return
  }

  console.log('MIGRATOR-DESKTOP-SMOKE ' + JSON.stringify(result))
  app.exit(det.ok ? 0 : 1)
}

function smokeLoadWindow(): Promise<boolean> {
  return new Promise((resolve) => {
    console.log('SMOKE-UI creating window')
    let settled = false
    const finish = (ok: boolean) => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      console.log('SMOKE-UI finish ' + ok)
      resolve(ok)
    }
    const timer = setTimeout(() => finish(false), 25000)
    ipcMain.once('renderer:ready', () => finish(true))
    createWindow()
    console.log('SMOKE-UI window created')
    const w = mainWindow
    if (!w) {
      finish(false)
      return
    }
    w.webContents.on('console-message', (_e, level: number, message: string) => {
      if (level === 3) {
        console.log('renderer console error: ' + message)
      }
    })
    w.webContents.on('did-fail-load', (_e, code, desc) => {
      console.log('did-fail-load ' + code + ' ' + desc)
      finish(false)
    })
    w.webContents.on('render-process-gone', (_e, details) => {
      console.log('render-process-gone ' + JSON.stringify(details))
      finish(false)
    })
  })
}

// ------------------------------------------------------------------- app

const isSmoke = process.argv.includes('--smoke')

void app.whenReady().then(async () => {
  if (isSmoke) {
    await runSmoke()
    return
  }
  registerIpc()
  createWindow()
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow()
  })
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit()
})
