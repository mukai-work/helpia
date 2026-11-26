// 解析エンジン（C#）が書き出す JSON の形。Helpia.Core と揃える。
export interface GroupStats {
  commits: number
  codeCommits: number
  testTouchRate: number | null
  reworkRate: number | null
  medianLinesChanged: number | null
  largeCommits: number
  tools: Record<string, number>
}

export interface HistoryReport {
  totalCommits: number
  from: string | null
  to: string | null
  reworkWindowDays: number
  largeCommitLines: number
  ai: GroupStats
  human: GroupStats
  aiShare: number | null
}

export type CheckStatus = 'Ok' | 'Warn' | 'Missing'

export interface ReadinessCheck {
  id: string
  title: string
  status: CheckStatus
  detail: string
  advice: string
}

export interface Report {
  id: string
  name: string
  scannedAt: string
  history: HistoryReport
  readiness: ReadinessCheck[]
}
