// Fork: small helpers for the host policy / session API (see FORK.md).

async function json(path, body) {
  const opts = { credentials: 'include' }
  if (body !== undefined) {
    opts.method = 'POST'
    opts.headers = { 'Content-Type': 'application/json' }
    opts.body = JSON.stringify(body)
  }
  const r = await fetch(path, opts)
  if (!r.ok) throw new Error(`${path}: HTTP ${r.status}`)
  return r.json()
}

export const getSession = () => json('./api/session')
export const getClients = () => json('./api/clients/list')

/**
 * Change the desk monitor / audio. While something runs it applies to that session now and is
 * remembered for its device; when idle it sets the defaults for the next stream.
 */
export const setPolicy = (live, change) => json(live ? './api/session/host-policy' : './api/host-policy', change)

/** Per-device override: each is 'inherit' or a value (touch_scale: 'off' or '150'). */
export const setClientPolicy = (uuid, display, muteWavelink, touchScale = 'inherit') =>
  json('./api/clients/host-policy', { uuid, display, mute_wavelink: muteWavelink, touch_scale: touchScale })

export const pair = (pin, name) => json('./api/pin', { pin, name })
export const endStream = () => json('./api/apps/close', {})

const CODEC_LABEL = { 'H.264': 'H.264', HEVC: 'HEVC', AV1: 'AV1' }

/** "1280×720 · 60 fps · HEVC · 10 Mbps · 12 min" */
export function describeStream(s, now = Date.now()) {
  const mins = Math.floor((now - s.started_at_ms) / 60000)
  const dur = mins < 1 ? 'just now' : mins < 60 ? `${mins} min` : `${Math.floor(mins / 60)} h ${mins % 60} min`
  const rate = s.bitrate_kbps >= 1000 ? `${(s.bitrate_kbps / 1000).toFixed(s.bitrate_kbps % 1000 ? 1 : 0)} Mbps` : `${s.bitrate_kbps} kbps`
  return [`${s.width}×${s.height}`, `${s.fps} fps`, (CODEC_LABEL[s.codec] || s.codec) + (s.hdr ? ' HDR' : ''), rate, dur].join(' · ')
}
