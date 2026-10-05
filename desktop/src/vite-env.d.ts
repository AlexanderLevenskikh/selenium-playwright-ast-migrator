/// <reference types="vite/client" />
import type { DesktopApi } from '../electron/types.js'

declare global {
  interface Window {
    migratorDesktop: DesktopApi
  }
}

export {}
