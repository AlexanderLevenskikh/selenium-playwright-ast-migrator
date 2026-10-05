import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  Bot,
  FileJson,
  FolderOpen,
  Loader2,
  Play,
  RefreshCw,
  Square,
  Wrench,
} from 'lucide-react'
import type {
  CliDetectResult,
  CoverageReport,
  GeneratedFileInfo,
  MigrateMode,
  ReportSummary,
  ReviewMap,
  RunExitEvent,
  RunLogEvent,
  RunRequest,
  Settings,
} from '../electron/types.js'
import { SummaryCards, ReportHeader } from './components/SummaryCards.js'
import { FilesTable, ExcludedList } from './components/FilesTable.js'
import { ReviewList } from './components/ReviewList.js'
import { GeneratedList } from './components/GeneratedList.js'
import { LegacySummary } from './components/LegacySummary.js'
import { flattenReviewItems, type ReviewItem } from './lib/flatten.js'

const api = window.migratorDesktop

const MODES: { id: MigrateMode; label: string }[] = [
  { id: 'analyze', label: 'Analyze' },
  { id: 'migrate', label: 'Migrate' },
  { id: 'run', label: 'Run' },
]
const TARGETS = ['', 'playwright-dotnet', 'playwright-typescript', 'dotnet', 'ts']
const TEST_FRAMEWORKS = ['', 'nunit', 'xunit']
const POLICIES = ['', 'conservative', 'balanced', 'aggressive']

type Tab = 'coverage' | 'review' | 'files' | 'log'

const LOG_CAP = 4000

function joinPath(a: string, b: string): string {
  const sep = api.platform === 'win32' ? '\\' : '/'
  const base = a.endsWith(sep) || a.endsWith('/') ? a.slice(0, -1) : a
  return base + sep + b.replace(/^[\\/]+/, '')
}

export default function App() {
  const [settings, setSettings] = useState<Settings>({})
  const [cli, setCli] = useState<CliDetectResult | null>(null)
  const [mode, setMode] = useState<MigrateMode>('migrate')
  const [running, setRunning] = useState(false)
  const [logs, setLogs] = useState<RunLogEvent[]>([])
  const [lastExit, setLastExit] = useState<RunExitEvent | null>(null)
  const [report, setReport] = useState<CoverageReport | null>(null)
  const [legacy, setLegacy] = useState<ReportSummary | null>(null)
  const [reviewMap, setReviewMap] = useState<ReviewMap>({})
  const [generated, setGenerated] = useState<GeneratedFileInfo[]>([])
  const [tab, setTab] = useState<Tab>('coverage')
  const [selectedFile, setSelectedFile] = useState<string | null>(null)
  const logRef = useRef<HTMLDivElement | null>(null)

  const settingsRef = useRef(settings)
  settingsRef.current = settings
  const reportRef = useRef(report)
  reportRef.current = report

  const loadSettings = useCallback(async () => {
    const s = await api.getSettings()
    setSettings(s)
    return s
  }, [])

  const detect = useCallback(async () => {
    const d = await api.detectCli()
    setCli(d)
    return d
  }, [])

  const refreshArtifacts = useCallback(async (outDir: string) => {
    const [cov, gen, sum] = await Promise.all([
      api.readCoverage(outDir),
      api.listGenerated(outDir),
      api.readReport(outDir),
    ])
    setReport(cov)
    setLegacy(cov ? null : sum)
    setGenerated(gen)
    if (cov) {
      const map = await api.reviewGet(outDir, cov.CoverageSha256)
      setReviewMap(map)
    } else {
      setReviewMap({})
    }
  }, [])

  useEffect(() => {
    api.notifyReady()
  }, [])

  useEffect(() => {
    void loadSettings()
  }, [loadSettings])

  useEffect(() => {
    if (!settings.outDir) return
    void refreshArtifacts(settings.outDir)
  }, [settings.outDir, refreshArtifacts])

  useEffect(() => {
    void detect()
  }, [detect])

  useEffect(() => {
    const offLog = api.onRunLog((e) => {
      setLogs((prev) => [...prev.slice(-(LOG_CAP - 1)), e])
    })
    const offExit = api.onRunExit((e) => {
      setRunning(false)
      setLastExit(e)
      const outDir = settingsRef.current.outDir
      if (outDir) void refreshArtifacts(outDir)
    })
    return () => {
      offLog()
      offExit()
    }
  }, [refreshArtifacts])

  useEffect(() => {
    if (logRef.current) {
      logRef.current.scrollTop = logRef.current.scrollHeight
    }
  }, [logs])

  const patchSettings = useCallback(
    async (patch: Partial<Settings>) => {
      const next = await api.setSettings(patch)
      setSettings(next)
      return next
    },
    []
  )

  const setPath = useCallback(
    (field: keyof Settings) =>
      api[field === 'configPath' ? 'pickFile' : 'pickDirectory']().then((p) => {
        if (p) return patchSettings({ [field]: p })
        return null
      }),
    [patchSettings]
  )

  const onRunStart = useCallback(async () => {
    const request: RunRequest = {
      mode,
      inputDir: settings.inputDir ?? '',
      configPath: settings.configPath,
      outDir: settings.outDir ?? '',
      target: settings.target,
      targetTestFramework: settings.targetTestFramework,
      generationPolicy: settings.generationPolicy,
      advancedArgs: settings.advancedArgs,
    }
    await api.runStart(request)
  }, [mode, settings])

  const estimate = useMemo(() => {
    const a = ['--mode', mode, '--input', settings.inputDir || '...']
    if (settings.configPath) a.push('--config', settings.configPath)
    if (settings.target) a.push('--target', settings.target)
    if (settings.targetTestFramework) a.push('--target-test-framework', settings.targetTestFramework)
    if (settings.generationPolicy) a.push('--generation-policy', settings.generationPolicy)
    a.push('--out', settings.outDir || '...', '--format', 'both')
    if (settings.advancedArgs) a.push(...settings.advancedArgs.split(/\s+/))
    return 'selenium-pw-migrator ' + a.join(' ')
  }, [mode, settings])

  const reviewItems: ReviewItem[] = useMemo(
    () => (report ? flattenReviewItems(report) : []),
    [report]
  )

  const toggleReview = useCallback(
    async (key: string, reviewed: boolean) => {
      const outDir = settings.outDir
      const cov = reportRef.current
      if (!outDir || !cov) return
      const entry = reviewMap[key]
      const next = await api.reviewSet(outDir, cov.CoverageSha256, key, {
        reviewed,
        note: entry?.note,
        at: new Date().toISOString(),
      })
      setReviewMap(next)
    },
    [reviewMap, settings.outDir]
  )

  const noteReview = useCallback(
    async (key: string, note: string) => {
      const outDir = settings.outDir
      const cov = reportRef.current
      if (!outDir || !cov) return
      const entry = reviewMap[key]
      const next = await api.reviewSet(outDir, cov.CoverageSha256, key, {
        reviewed: entry?.reviewed ?? false,
        note,
        at: new Date().toISOString(),
      })
      setReviewMap(next)
    },
    [reviewMap, settings.outDir]
  )

  const openSource = useCallback(
    (relPath: string) => {
      if (!settings.inputDir) return
      void api.openPath(joinPath(settings.inputDir, relPath))
    },
    [settings.inputDir]
  )

  const refreshButton = useCallback(() => {
    if (settings.outDir) void refreshArtifacts(settings.outDir)
    void detect()
  }, [settings.outDir, refreshArtifacts, detect])

  return (
    <div className="app">
      <div className="toolbar">
        <div className="brand">
          <span className="brand-icon">
            <Wrench size={14} />
          </span>
          Selenium → Playwright Migrator
          <span className="brand-sub">desktop</span>
        </div>
        <div className="spacer" />
        {cli &&
          (cli.ok ? (
            <div className="cli-status ok" title={cli.path ?? ''}>
              <Bot size={13} /> {cli.version ?? 'CLI'} найден
            </div>
          ) : (
            <div className="cli-status bad">
              <Bot size={13} /> CLI не найден
            </div>
          ))}
        <button className="btn" onClick={() => void detect()}>
          <RefreshCw size={14} /> Проверить CLI
        </button>
      </div>

      <div className="settings">
        <div className="settings-row">
          <label>Путь к CLI (необязательно)</label>
          <input
            type="text"
            value={settings.cliPath ?? ''}
            placeholder="Автоопределение из PATH / ~/.selenium-pw-migrator"
            onChange={(e) => void patchSettings({ cliPath: e.target.value })}
          />
          <div className="settings-actions">
            <button
              className="btn"
              onClick={() => {
                void patchSettings({ cliPath: undefined })
                void detect()
              }}
            >
              Авто
            </button>
          </div>
        </div>
        <div className="settings-row">
          <label>Вход: папка с Selenium-тестами</label>
          <input
            type="text"
            value={settings.inputDir ?? ''}
            placeholder="Например, C:\repos\app\src\Tests"
            onChange={(e) => void patchSettings({ inputDir: e.target.value })}
          />
          <div className="settings-actions">
            <button
              className="btn"
              title="Выбрать папку"
              onClick={() => void setPath('inputDir')}
            >
              <FolderOpen size={14} /> Папка
            </button>
          </div>
        </div>
        <div className="settings-row">
          <label>Adapter config (необязательно)</label>
          <input
            type="text"
            value={settings.configPath ?? ''}
            placeholder="Путь к adapter-config.json"
            onChange={(e) => void patchSettings({ configPath: e.target.value })}
          />
          <div className="settings-actions">
            <button
              className="btn"
              title="Выбрать файл конфига"
              onClick={() => void setPath('configPath')}
            >
              <FileJson size={14} /> Файл
            </button>
          </div>
        </div>
        <div className="settings-row">
          <label>Выход: папка артефактов</label>
          <input
            type="text"
            value={settings.outDir ?? ''}
            placeholder="Куда писать отчёты и сгенерированные файлы"
            onChange={(e) => void patchSettings({ outDir: e.target.value })}
          />
          <div className="settings-actions">
            <button className="btn" onClick={() => void setPath('outDir')}>
              <FolderOpen size={14} /> Папка
            </button>
            {settings.outDir ? (
              <button
                className="btn"
                onClick={() => void api.openFolder(settings.outDir!)}
              >
                В проводнике
              </button>
            ) : null}
          </div>
        </div>
        <div className="settings-row">
          <label>Опции запуска (опционально)</label>
          <select
            value={settings.target ?? ''}
            onChange={(e) => void patchSettings({ target: e.target.value })}
          >
            {TARGETS.map((t) => (
              <option key={t} value={t}>
                target: {t || '(default)'}
              </option>
            ))}
          </select>
          <select
            value={settings.targetTestFramework ?? ''}
            onChange={(e) => void patchSettings({ targetTestFramework: e.target.value })}
          >
            {TEST_FRAMEWORKS.map((t) => (
              <option key={t} value={t}>
                framework: {t || '(default)'}
              </option>
            ))}
          </select>
          <select
            value={settings.generationPolicy ?? ''}
            onChange={(e) => void patchSettings({ generationPolicy: e.target.value })}
          >
            {POLICIES.map((p) => (
              <option key={p} value={p}>
                policy: {p || '(default)'}
              </option>
            ))}
          </select>
          <input
            type="text"
            style={{ flex: 1, minWidth: 180 }}
            value={settings.advancedArgs ?? ''}
            placeholder="доп. флаги"
            onChange={(e) => void patchSettings({ advancedArgs: e.target.value })}
          />
        </div>
      </div>

      <div className="runbar">
        <div className="mode-tabs">
          {MODES.map((m) => (
            <button
              key={m.id}
              className={`mode-tab ${mode === m.id ? 'active' : ''}`}
              onClick={() => setMode(m.id)}
            >
              {m.label}
            </button>
          ))}
        </div>
        <button className="btn primary" disabled={running} onClick={() => void onRunStart()}>
          {running ? <Loader2 size={14} className="spin" /> : <Play size={14} />}
          {running ? 'Выполняется…' : 'Запустить'}
        </button>
        <button className="btn danger" disabled={!running} onClick={() => void api.runCancel()}>
          <Square size={14} /> Отмена
        </button>
        <span className="run-estimate" title={estimate}>
          {estimate}
        </span>
        <div className="run-spacer" />
        {lastExit && (
          <span className={`badge ${lastExit.ok ? 'ok' : 'bad'}`}>
            exit {lastExit.code} за {(lastExit.durationMs / 1000).toFixed(1)}s
          </span>
        )}
      </div>

      <div className="tabs">
        {(
          [
            ['coverage', 'Покрытие'],
            ['review', 'Ревью'],
            ['files', 'Файлы'],
            ['log', 'Лог'],
          ] as [Tab, string][]
        ).map(([id, label]) => (
          <button
            key={id}
            className={`tab ${tab === id ? 'active' : ''}`}
            onClick={() => setTab(id)}
          >
            {label}
          </button>
        ))}
        <div className="run-spacer" />
        <button className="btn" onClick={refreshButton}>
          <RefreshCw size={14} /> Обновить
        </button>
      </div>

      <div className="content">
        {tab === 'coverage' &&
          (report ? (
            <>
              <ReportHeader report={report} />
              <SummaryCards report={report} />
              <div className="section-title">Файлы</div>
              <FilesTable
                report={report}
                selected={selectedFile}
                onSelect={setSelectedFile}
                onOpenSource={(f) => openSource(f.RelativePath)}
              />
              <ExcludedList report={report} />
            </>
          ) : legacy ? (
            <LegacySummary
              summary={legacy}
              inputDir={settings.inputDir}
              onOpenSource={(p) => void api.openPath(p)}
            />
          ) : (
            <div className="placeholder">
              Нет coverage-report.json. Укажите выходную папку и запустите прогон, либо
              нажмите «Обновить».
            </div>
          ))}
        {tab === 'review' &&
          (report ? (
            <ReviewList
              items={reviewItems}
              reviewMap={reviewMap}
              fileFilter={selectedFile}
              onToggle={(k, v) => void toggleReview(k, v)}
              onNote={(k, v) => void noteReview(k, v)}
              onOpenSource={(p) => openSource(p)}
            />
          ) : (
            <div className="placeholder">
              Нет конструкторного отчёта покрытия — вкладка «Ревью» доступна с CLI, где есть
              coverage-report.json.
            </div>
          ))}
        {tab === 'files' && (
          <GeneratedList
            generated={generated}
            onOpen={(abs) => void api.openPath(abs)}
            onReveal={(abs) => void api.openFolder(abs)}
          />
        )}
        {tab === 'log' && (
          <div className="logview" ref={logRef}>
            {logs.length === 0 && (
              <div className="hint">Вывод CLI появится здесь после запуска.</div>
            )}
            {logs.map((l, i) => (
              <div key={i} className={l.stream}>
                {l.line}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
