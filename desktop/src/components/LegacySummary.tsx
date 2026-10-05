import type { ReportSummary } from '../../electron/types.js'

function Card({ label, value, tone }: { label: string; value: number; tone?: 'ok' | 'warn' | 'bad' | 'info' }) {
  return (
    <div className="card">
      <div className={`num ${tone ?? ''}`}>{value}</div>
      <div className="lbl">{label}</div>
    </div>
  )
}

export function LegacySummary({
  summary,
  onOpenSource,
  inputDir,
}: {
  summary: ReportSummary
  onOpenSource: (absPath: string) => void
  inputDir: string | undefined
}) {
  const sep = window.migratorDesktop.platform === 'win32' ? '\\' : '/'
  const base = (inputDir ?? '').replace(/[\\/]+$/, '')
  return (
    <div>
      <div className="badge warn" style={{ marginBottom: 12 }}>
        Сводный отчёт (report.json). Конструкторный coverage-report.json появится в CLI со
        следующего релиза — тогда включится вкладка «Покрытие» с деталями по конструкциям.
      </div>
      <div className="cards">
        <Card label="Files" value={summary.FilesProcessed} tone="info" />
        <Card label="Tests" value={summary.TestsFound} />
        <Card label="Actions" value={summary.ActionsFound} />
        <Card label="Mapped" value={summary.MappedTargets} tone="ok" />
        <Card label="Unmapped" value={summary.UnmappedTargets} tone="warn" />
        <Card label="TODO" value={summary.TodoComments} tone="warn" />
        <Card label="Unsupported" value={summary.UnsupportedActions} tone="bad" />
        <Card label="Generated files" value={summary.GeneratedFiles} />
      </div>
      {summary.PerFileReports && summary.PerFileReports.length > 0 && (
        <>
          <div className="section-title">Файлы</div>
          <table className="table">
            <thead>
              <tr>
                <th>File</th>
                <th>Tests</th>
                <th>Converted</th>
                <th>Unsupported</th>
                <th>Mapped</th>
                <th>Unmapped</th>
                <th>TODO</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {summary.PerFileReports.map((f) => (
                <tr key={f.SourceFilePath}>
                  <td className="mono">{f.SourceFilePath.split(/[\\/]/).pop()}</td>
                  <td>{f.TotalTests}</td>
                  <td>{f.SuccessfullyConvertedTests}</td>
                  <td>{f.UnsupportedCount}</td>
                  <td>{f.MappedTargets}</td>
                  <td>{f.UnmappedTargets}</td>
                  <td>{f.TodoComments}</td>
                  <td>
                    {inputDir ? (
                      <button
                        className="btn"
                        onClick={() => onOpenSource(`${base}${sep}${f.SourceFilePath.split(/[\\/]/).pop()}`)}
                      >
                        Открыть
                      </button>
                    ) : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
      {summary.TopUnmappedTargets && summary.TopUnmappedTargets.length > 0 && (
        <>
          <div className="section-title">Частые ненайденные цели</div>
          {summary.TopUnmappedTargets.map((t) => (
            <div key={t.SourceExpression} className="review-item">
              <div className="row1">
                <span className="mono">{t.SourceExpression}</span>
                <span className="badge">{t.Usages} использований</span>
              </div>
            </div>
          ))}
        </>
      )}
    </div>
  )
}
