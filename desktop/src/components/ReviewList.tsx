import { useState } from 'react'
import type { ReviewMap } from '../../electron/types.js'
import type { ReviewItem } from '../lib/flatten.js'

const FILTERS = ['all', 'requires_review', 'unsupported', 'ambiguous', 'parse_error'] as const
type Filter = (typeof FILTERS)[number]

export function ReviewList({
  items,
  reviewMap,
  fileFilter,
  onToggle,
  onNote,
  onOpenSource,
}: {
  items: ReviewItem[]
  reviewMap: ReviewMap
  fileFilter: string | null
  onToggle: (key: string, reviewed: boolean) => void
  onNote: (key: string, note: string) => void
  onOpenSource: (filePath: string) => void
}) {
  const [filter, setFilter] = useState<Filter>('all')
  const [hideDone, setHideDone] = useState(false)

  const filtered = items.filter((it) => {
    if (fileFilter && it.file.RelativePath !== fileFilter) return false
    if (filter !== 'all' && it.construct.State !== filter) return false
    if (hideDone && reviewMap[it.key]?.reviewed) return false
    return true
  })

  const total = items.length
  const done = items.filter((it) => reviewMap[it.key]?.reviewed).length

  return (
    <div>
      <div className="filterbar">
        <span>
          Просмотрено {done} из {total}
        </span>
        <select value={filter} onChange={(e) => setFilter(e.target.value as Filter)}>
          {FILTERS.map((f) => (
            <option key={f} value={f}>
              {f === 'all' ? 'все состояния' : f}
            </option>
          ))}
        </select>
        <label className="checkbox-label">
          <input
            type="checkbox"
            checked={hideDone}
            onChange={(e) => setHideDone(e.target.checked)}
          />
          скрыть просмотренные
        </label>
      </div>
      {filtered.length === 0 && <div className="placeholder">Пусто</div>}
      {filtered.map((it) => {
        const entry = reviewMap[it.key]
        const done = entry?.reviewed ?? false
        return (
          <div key={it.key} className={`review-item ${done ? 'done' : ''}`}>
            <div className="row1">
              <label className="checkbox-label">
                <input
                  type="checkbox"
                  checked={done}
                  onChange={(e) => onToggle(it.key, e.target.checked)}
                />
              </label>
              <span className={`state ${it.construct.State}`}>{it.construct.State}</span>
              <span className="mono">{it.construct.Kind}</span>
              {it.testName && <span className="badge">test: {it.testName}</span>}
              <span className="where">
                {it.file.RelativePath}:{it.construct.SourceLine}
              </span>
              <div className="actions">
                <button className="btn" onClick={() => onOpenSource(it.file.RelativePath)}>
                  Файл
                </button>
              </div>
            </div>
            {it.construct.Reason && (
              <div className="reason">Причина: {it.construct.Reason}</div>
            )}
            {it.construct.PolicySource && (
              <div className="reason mono">policy: {it.construct.PolicySource}</div>
            )}
            <input
              className="note-input"
              type="text"
              placeholder="Заметка (например, результат проверки)"
              defaultValue={entry?.note ?? ''}
              onBlur={(e) => onNote(it.key, e.target.value)}
            />
          </div>
        )
      })}
    </div>
  )
}
