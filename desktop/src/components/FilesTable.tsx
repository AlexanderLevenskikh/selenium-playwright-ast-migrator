import type { CoverageReport, FileUnit } from '../../electron/types.js'

export function FilesTable({
  report,
  selected,
  onSelect,
  onOpenSource,
}: {
  report: CoverageReport
  selected: string | null
  onSelect: (path: string | null) => void
  onOpenSource: (file: FileUnit) => void
}) {
  return (
    <table className="table">
      <thead>
        <tr>
          <th>File</th>
          <th>State</th>
          <th>Tests</th>
          <th>T</th>
          <th>RR</th>
          <th>US</th>
          <th>AM</th>
          <th>PE</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        {report.FilesDetail.map((f) => (
          <tr
            key={f.RelativePath}
            className={`clickable ${selected === f.RelativePath ? 'selected' : ''}`}
            onClick={() => onSelect(selected === f.RelativePath ? null : f.RelativePath)}
          >
            <td className="mono">{f.RelativePath}</td>
            <td>
              <span className={`state ${f.State}`}>{f.State}</span>
            </td>
            <td>{f.Tests?.length ?? 0}</td>
            <td>{f.Transformed}</td>
            <td>{f.RequiresReview}</td>
            <td>{f.Unsupported}</td>
            <td>{f.Ambiguous}</td>
            <td>{f.ParseError}</td>
            <td>
              <button
                className="btn"
                onClick={(e) => {
                  e.stopPropagation()
                  onOpenSource(f)
                }}
              >
                Открыть
              </button>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

export function ExcludedList({ report }: { report: CoverageReport }) {
  if (!report.Excluded?.length) return null
  return (
    <>
      <div className="section-title">
        Excluded files ({report.Excluded.length})
      </div>
      <table className="table">
        <thead>
          <tr>
            <th>File</th>
            <th>Reason</th>
            <th>Policy source</th>
          </tr>
        </thead>
        <tbody>
          {report.Excluded.map((e) => (
            <tr key={e.Id + e.RelativePath}>
              <td className="mono">{e.RelativePath}</td>
              <td>{e.Reason}</td>
              <td className="mono">{e.PolicySource ?? ''}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  )
}
