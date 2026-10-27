<template>
  <nav class="fl-nav" :aria-label="$t('fork.nav_label')">
    <a class="fl-brand" href="./" title="Apollo">
      <span class="fl-brand-mark" aria-hidden="true">
        <svg width="20" height="20" viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="1.6"><circle cx="10" cy="10" r="3.2"/><path d="M10 1.8v2.6M10 15.6v2.6M1.8 10h2.6M15.6 10h2.6M4.2 4.2l1.8 1.8M14 14l1.8 1.8M4.2 15.8 6 14M14 6l1.8-1.8"/></svg>
      </span>
      <span class="fl-brand-text">
        <span class="fl-brand-title d-block">Apollo</span>
        <span class="fl-brand-sub d-block">{{ hostName || $t('fork.this_pc') }}</span>
      </span>
    </a>

    <a v-for="item in items" :key="item.href" class="fl-item" :class="{ active: item.active }"
       :href="item.href" :aria-current="item.active ? 'page' : null">
      <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.3" aria-hidden="true" v-html="item.icon"></svg>
      <span>{{ item.label }}</span>
    </a>

    <div class="fl-spacer"></div>
    <div class="fl-theme">
      <ThemeToggle/>
    </div>
  </nav>
</template>

<script>
import ThemeToggle from './ThemeToggle.vue'

// Inline Fluent-style stroke icons (16px grid).
const ICONS = {
  home: '<path d="M2.5 7.2 8 2.5l5.5 4.7V13a.5.5 0 0 1-.5.5H9.5V10h-3v3.5H3a.5.5 0 0 1-.5-.5z"/>',
  devices: '<rect x="1.5" y="3" width="9" height="7" rx="1"/><path d="M4 12.5h4"/><rect x="11" y="5" width="3.5" height="8" rx="0.8"/>',
  apps: '<rect x="2" y="2" width="5" height="5" rx="1"/><rect x="9" y="2" width="5" height="5" rx="1"/><rect x="2" y="9" width="5" height="5" rx="1"/><rect x="9" y="9" width="5" height="5" rx="1"/>',
  settings: '<circle cx="8" cy="8" r="2.2"/><path d="M8 1.5v2M8 12.5v2M1.5 8h2M12.5 8h2M3.4 3.4l1.4 1.4M11.2 11.2l1.4 1.4M3.4 12.6l1.4-1.4M11.2 4.8l1.4-1.4"/>',
  account: '<circle cx="8" cy="5.5" r="2.8"/><path d="M2.5 14a5.5 5.5 0 0 1 11 0"/>',
  troubleshoot: '<path d="M9.5 2.2a3.3 3.3 0 0 0 4.3 4.3L7 13.3a1.5 1.5 0 0 1-2.2-2.1z"/>',
}

export default {
  components: { ThemeToggle },
  data() {
    return { hostName: '' }
  },
  computed: {
    page() {
      // Pages are served without an extension: /, /pin, /apps, /config, /password, /troubleshooting
      const p = document.location.pathname.replace(/\/+$/, '').split('/').pop()
      return p === '' || p === 'index.html' ? 'home' : p.replace(/\.html$/, '')
    },
    items() {
      const t = (k) => this.$t(k)
      return [
        { href: './', label: t('navbar.home'), icon: ICONS.home, active: this.page === 'home' },
        { href: './pin', label: t('fork.nav_devices'), icon: ICONS.devices, active: this.page === 'pin' },
        { href: './apps', label: t('fork.nav_apps'), icon: ICONS.apps, active: this.page === 'apps' },
        { href: './config', label: t('fork.nav_settings'), icon: ICONS.settings, active: this.page === 'config' },
        { href: './password', label: t('fork.nav_account'), icon: ICONS.account, active: this.page === 'password' },
        { href: './troubleshooting', label: t('navbar.troubleshoot'), icon: ICONS.troubleshoot, active: this.page === 'troubleshooting' },
      ]
    },
  },
  async mounted() {
    try {
      const r = await (await fetch('./api/session', { credentials: 'include' })).json()
      this.hostName = r.host_name || ''
    } catch (e) {
      // Not logged in yet (login/welcome pages): keep the generic label.
    }
  },
}
</script>

<style>
.fl-theme .bd-mode-toggle .nav-link {
  display: flex;
  align-items: center;
  gap: 14px;
  height: 40px;
  padding: 0 12px;
  border-radius: var(--fl-radius);
  color: var(--fl-text);
  font-size: 14px;
}

.fl-theme .bd-mode-toggle .nav-link:hover { background: var(--fl-sel); }

@media (max-width: 900px) {
  .fl-theme #bd-theme-text { display: none; }
}
</style>
