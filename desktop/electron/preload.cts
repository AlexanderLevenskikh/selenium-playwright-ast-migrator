import { contextBridge, ipcRenderer } from 'electron'
import type {
  DesktopApi,
  ReviewEntry,
  RunExitEvent,
  RunLogEvent,
  RunRequest,
  Settings,
} from './types.js'

const api: DesktopApi = {
  platform: process.platform,
  getSettings: () => ipcRenderer.invoke('settings:get'),
  setSettings: (patch: Partial<Settings>) => ipcRenderer.invoke('settings:set', patch),
  detectCli: () => ipcRenderer.invoke('cli:detect'),
  runStart: (request: RunRequest) => ipcRenderer.invoke('run:start', request),
  runCancel: () => ipcRenderer.invoke('run:cancel'),
  readCoverage: (outDir: string) => ipcRenderer.invoke('artifact:coverage', outDir),
  readReport: (outDir: string) => ipcRenderer.invoke('artifact:report', outDir),
  listGenerated: (outDir: string) => ipcRenderer.invoke('artifact:generated', outDir),
  reviewGet: (outDir: string, coverageSha: string) =>
    ipcRenderer.invoke('review:get', outDir, coverageSha),
  reviewSet: (outDir: string, coverageSha: string, key: string, entry: ReviewEntry) =>
    ipcRenderer.invoke('review:set', outDir, coverageSha, key, entry),
  pickDirectory: () => ipcRenderer.invoke('dialog:pickDirectory'),
  pickFile: () => ipcRenderer.invoke('dialog:pickFile'),
  openPath: (p: string) => ipcRenderer.invoke('shell:openPath', p),
  openFolder: (p: string) => ipcRenderer.invoke('shell:openFolder', p),
  onRunLog: (cb: (e: RunLogEvent) => void) => {
    const listener = (_e: unknown, ev: RunLogEvent) => cb(ev)
    ipcRenderer.on('run:log', listener)
    return () => {
      ipcRenderer.removeListener('run:log', listener)
    }
  },
  onRunExit: (cb: (e: RunExitEvent) => void) => {
    const listener = (_e: unknown, ev: RunExitEvent) => cb(ev)
    ipcRenderer.on('run:exit', listener)
    return () => {
      ipcRenderer.removeListener('run:exit', listener)
    }
  },
  notifyReady: () => ipcRenderer.send('renderer:ready'),
}

contextBridge.exposeInMainWorld('migratorDesktop', api)
