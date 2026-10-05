import type { CoverageReport, ConstructUnit, FileUnit } from '../../electron/types.js'

export interface ReviewItem {
  key: string
  file: FileUnit
  testName: string | null
  construct: ConstructUnit
}

export function reviewKey(
  file: FileUnit,
  testName: string | null,
  c: ConstructUnit
): string {
  return `${file.RelativePath}::${testName ?? 'setup'}::${c.Id}`
}

export function flattenReviewItems(report: CoverageReport): ReviewItem[] {
  const out: ReviewItem[] = []
  for (const file of report.FilesDetail) {
    for (const c of file.SetUp ?? []) {
      out.push({ key: reviewKey(file, null, c), file, testName: null, construct: c })
    }
    for (const t of file.Tests ?? []) {
      for (const c of t.Constructs ?? []) {
        out.push({ key: reviewKey(file, t.Name, c), file, testName: t.Name, construct: c })
      }
    }
  }
  return out
}
