import type { GeneratedFileInfo } from '../../electron/types.js'

export function GeneratedList({
  generated,
  onOpen,
  onReveal,
}: {
  generated: GeneratedFileInfo[]
  onOpen: (abs: string) => void
  onReveal: (abs: string) => void
}) {
  if (generated.length === 0) {
    return <div className="placeholder">Сгенерированных файлов не найдено в выходной директории.</div>
  }
  return (
    <div>
      <div className="hint">
        Найдено файлов: {generated.length}. Генерируемые артефакты названы *Playwright.cs /
        *Playwright.ts.
      </div>
      {generated.map((g) => (
        <div key={g.absolutePath} className="generated-item">
          <span className="path" title={g.absolutePath}>
            {g.relativePath}
          </span>
          <button className="btn" onClick={() => onOpen(g.absolutePath)}>
            Открыть
          </button>
          <button className="btn" onClick={() => onReveal(g.absolutePath)}>
            В папке
          </button>
        </div>
      ))}
    </div>
  )
}
