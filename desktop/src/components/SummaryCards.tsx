import type { CoverageReport } from '../../electron/types.js'

function Card({
  label,
  value,
  tone,
}: {
  label: string
  value: number
  tone?: 'ok' | 'warn' | 'bad' | 'info'
}) {
  return (
    <div className="card">
      <div className={`num ${tone ?? ''}`}>{value}</div>
      <div className="lbl">{label}</div>
    </div>
  )
}

export function SummaryCards({ report }: { report: CoverageReport }) {
  return (
    <div className="cards">
      <Card label="Files" value={report.Files} tone="info" />
      <Card label="Tests" value={report.Tests} />
      <Card label="Constructs" value={report.Constructs} />
      <Card label="Transformed" value={report.Transformed} tone="ok" />
      <Card label="Requires review" value={report.RequiresReview} tone="warn" />
      <Card label="Unsupported" value={report.Unsupported} tone="bad" />
      <Card label="Ambiguous" value={report.Ambiguous} tone="info" />
      <Card label="Parse errors" value={report.ParseError} tone="bad" />
      <Card label="Excluded files" value={report.ExcludedFiles} />
      <Card label="Unexplained residual" value={report.UnexplainedResidual} tone="warn" />
    </div>
  )
}

export function ReportHeader({ report }: { report: CoverageReport }) {
  const surveyBadge =
    report.SurveyStatus === 'complete' ? (
      <span className="badge ok">survey: complete</span>
    ) : (
      <span className="badge warn">survey: partial</span>
    )
  const transformBadge =
    report.TransformationStatus === 'complete' ? (
      <span className="badge ok">transformation: complete</span>
    ) : (
      <span className="badge warn">transformation: incomplete</span>
    )
  const detectionBadge = report.DetectionComplete ? (
    <span className="badge ok">detection: complete</span>
  ) : (
    <span className="badge bad">detection: incomplete</span>
  )
  const classificationBadge = report.ClassificationComplete ? (
    <span className="badge ok">classification: complete</span>
    ) : (
    <span className="badge bad">classification: incomplete</span>
  )
  return (
    <div className="report-header">
      {report.SourceRootRelative && <span className="mono">{report.SourceRootRelative}</span>}
      {report.TargetBackend && <span className="badge info">{report.TargetBackend}</span>}
      {surveyBadge}
      {transformBadge}
      {detectionBadge}
      {classificationBadge}
      <span className="mono" title={report.Id}>
        id {report.Id.slice(0, 8)}
      </span>
      <span className="mono" title="CoverageSha256">
        sha {report.CoverageSha256.slice(0, 8)}
      </span>
    </div>
  )
}
