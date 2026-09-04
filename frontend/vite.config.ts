import { readFileSync } from 'node:fs'
import { homedir } from 'node:os'
import { join } from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

/**
 * The API token, read from disk at dev-server startup.
 *
 * A browser cannot read %LOCALAPPDATA%\devlog\api-token.txt, and it should not
 * be able to — that file is the only thing standing between any page you visit
 * and your entire activity log. So in development the proxy below attaches the
 * header server-side and the frontend never handles the secret at all.
 *
 * In the shipped app the collector serves the built page and injects the token
 * into the HTML, which is why api/client.ts sends one when it finds one and
 * relies on this proxy when it does not.
 */
function readApiToken(): string | null {
  const candidates = [
    process.env.DEVLOG_TOKEN_PATH,
    process.env.LOCALAPPDATA ? join(process.env.LOCALAPPDATA, 'devlog', 'api-token.txt') : null,
    join(homedir(), 'AppData', 'Local', 'devlog', 'api-token.txt'),
  ].filter((p): p is string => Boolean(p))

  for (const path of candidates) {
    try {
      const token = readFileSync(path, 'utf8').trim()
      if (token) {
        console.log(`[devlog] api token loaded from ${path}`)
        return token
      }
    } catch {
      // Try the next candidate. A missing file is normal until the collector
      // has run once.
    }
  }

  console.warn(
    '[devlog] no api-token.txt found — /v1 requests will 401.\n' +
      '         Start the collector once to generate it, then restart this dev server.',
  )
  return null
}

const token = readApiToken()

export default defineConfig({
  plugins: [react(), tailwindcss()],

  // Built straight into the collector's wwwroot, so the shipped app is served
  // from the same origin as the API it calls and production needs no CORS at
  // all. Gitignored -- this is build output; the source of truth is src/.
  build: {
    outDir: '../backend/src/Devlog.Host/wwwroot',
    emptyOutDir: true,
  },

  server: {
    port: 5173,
    proxy: {
      '/v1': {
        target: 'http://127.0.0.1:5111',
        changeOrigin: false,
        configure: (proxy) => {
          proxy.on('proxyReq', (proxyReq) => {
            if (token) {
              proxyReq.setHeader('X-Devlog-Token', token)
            }
          })
        },
      },
      '/health': { target: 'http://127.0.0.1:5111', changeOrigin: false },
    },
  },
})
