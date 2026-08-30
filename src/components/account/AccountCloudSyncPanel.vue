<script setup lang="ts">
import { computed, ref } from 'vue';
import { useAuthStore } from '../../features/auth/store';
import { useSettingsStore } from '../../features/settings/store';
import { usePlaylistSync } from '../../composables/usePlaylistSync';

const authStore = useAuthStore();
const settingsStore = useSettingsStore();
const playlistSync = usePlaylistSync();
const expanded = ref(false);
type UploadKey = 'playlists' | 'plugins' | 'settings';
const uploadItems: Array<{ key: UploadKey; label: string }> = [
  { key: 'playlists', label: '歌单与收藏' },
  { key: 'plugins', label: '插件配置' },
  { key: 'settings', label: '本地设置' },
];

const isSyncing = computed(() => (
  playlistSync.syncing.value
  || playlistSync.pluginSyncing.value
  || playlistSync.settingsSyncing.value
));

const lastSyncText = computed(() => {
  const timestamps = [
    playlistSync.lastSyncTime.value,
    playlistSync.lastPluginSyncTime.value,
    playlistSync.lastSettingsSyncTime.value,
  ].filter((value): value is number => typeof value === 'number');
  if (timestamps.length === 0) return '尚未同步';
  return new Date(Math.max(...timestamps)).toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
});

const syncErrorDetails = computed(() => [
  ...(playlistSync.lastSyncResult.value?.errors ?? []).map(error => ({ category: '歌单', error })),
  ...(playlistSync.lastPluginSyncResult.value?.errors ?? []).map(error => ({ category: '插件', error })),
  ...(playlistSync.lastSettingsSyncResult.value?.errors ?? []).map(error => ({ category: '设置', error })),
]);

const syncErrorCount = computed(() => syncErrorDetails.value.length);

const syncStatusText = computed(() => {
  if (playlistSync.syncing.value) return playlistSync.syncProgress.value || '正在同步歌单…';
  if (playlistSync.pluginSyncing.value) return playlistSync.pluginSyncProgress.value || '正在同步插件…';
  if (playlistSync.settingsSyncing.value) return playlistSync.settingsSyncProgress.value || '正在同步设置…';
  if (syncErrorCount.value > 0) return `上次同步有 ${syncErrorCount.value} 个问题，展开查看详情`;
  return `上次同步：${lastSyncText.value}`;
});

const intervalOptions = [
  { value: 1800, label: '30 分钟' },
  { value: 3600, label: '1 小时' },
  { value: 10800, label: '3 小时' },
  { value: 21600, label: '6 小时' },
  { value: 43200, label: '12 小时' },
  { value: 86400, label: '1 天' },
];

function handleSync() {
  void playlistSync.syncAll();
}

function toggleUpload(key: keyof typeof settingsStore.settings.upload) {
  settingsStore.patchSettings({
    upload: {
      ...settingsStore.settings.upload,
      [key]: !settingsStore.settings.upload[key],
    },
  });
}

function toggleAutoSync() {
  playlistSync.patchAutoSyncConfig({
    enabled: !settingsStore.settings.autoSync.enabled,
  });
}

function updateInterval(event: Event) {
  const seconds = Number((event.target as HTMLSelectElement).value);
  if (!Number.isFinite(seconds) || seconds <= 0) return;
  playlistSync.patchAutoSyncConfig({ syncIntervalSeconds: seconds });
}
</script>

<template>
  <section class="account-cloud-sync px-[clamp(1.5rem,2.8vw,3.5rem)] py-[clamp(0.75rem,1.2vw,1.25rem)] animate-fade-in-up">
    <button
      type="button"
      class="-mx-3 flex w-full items-center justify-between gap-3 px-3 py-2 text-left transition-colors hover:bg-black/5 dark:hover:bg-white/10"
      :aria-expanded="expanded"
      @click="expanded = !expanded"
    >
      <span class="min-w-0">
          <span class="block truncate text-[clamp(1.05rem,1.5vw,1.25rem)] font-medium tracking-wider text-black dark:text-white">账号云同步</span>
          <span class="mt-1 block truncate text-xs text-black/55 dark:text-white/55">{{ syncStatusText }}</span>
      </span>
      <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4 shrink-0 text-black/50 transition-transform duration-300 dark:text-white/50" :class="{ 'rotate-180': expanded }" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2.2">
        <path stroke-linecap="round" stroke-linejoin="round" d="M19 9l-7 7-7-7" />
      </svg>
    </button>

    <transition name="account-cloud-sync-panel">
      <div v-if="expanded" class="px-3 pb-3 pt-2">
        <p class="mb-3 text-xs text-black/55 dark:text-white/55">
          当前账号：<span class="font-semibold text-black/80 dark:text-white/85">@{{ authStore.user?.ciyuanxi_id || authStore.user?.username }}</span>
          · 在电脑、手机等设备间同步歌单、收藏、插件配置和本地设置。
        </p>

        <button
          type="button"
          class="mb-3 inline-flex h-9 items-center gap-1.5 rounded-full bg-accent px-4 text-xs font-semibold text-white shadow-sm transition hover:bg-accent-hover active:scale-95 disabled:cursor-not-allowed disabled:opacity-60"
          :disabled="isSyncing"
          @click.stop="handleSync"
        >
          <svg v-if="isSyncing" class="h-3.5 w-3.5 animate-spin" viewBox="0 0 24 24" fill="none">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
            <path class="opacity-90" fill="currentColor" d="M4 12a8 8 0 0 1 8-8v4a4 4 0 0 0-4 4H4Z" />
          </svg>
          <svg v-else xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
            <path stroke-linecap="round" stroke-linejoin="round" d="M4 4v5h5M20 20v-5h-5M5.5 15a7 7 0 0011.95 1.95L20 14M4 10l2.55-2.95A7 7 0 0118.5 9" />
          </svg>
          {{ isSyncing ? '同步中…' : '立即同步' }}
        </button>

        <div class="grid gap-2 sm:grid-cols-3">
          <button
            v-for="item in uploadItems"
            :key="item.key"
            type="button"
            class="flex items-center justify-between rounded-lg px-1 py-2 text-left transition-colors hover:bg-black/5 dark:hover:bg-white/10"
            @click="toggleUpload(item.key)"
          >
            <span class="text-xs font-medium text-black/75 dark:text-white/80">{{ item.label }}</span>
            <span class="relative h-5 w-9 rounded-full bg-black/15 transition dark:bg-white/15" :class="{ 'bg-accent': settingsStore.settings.upload[item.key] }">
              <span class="absolute left-0.5 top-0.5 h-4 w-4 rounded-full bg-white shadow-sm transition-transform" :class="{ 'translate-x-4': settingsStore.settings.upload[item.key] }"></span>
            </span>
          </button>
        </div>

        <div class="mt-2 flex items-center justify-between gap-3 px-1 py-2">
          <div class="min-w-0">
            <div class="text-xs font-medium text-black/75 dark:text-white/80">自动同步</div>
            <div class="mt-0.5 text-[11px] text-black/45 dark:text-white/45">应用运行期间同步，不上传本地音乐文件</div>
          </div>
          <div class="flex shrink-0 items-center gap-2">
            <select
              :value="settingsStore.settings.autoSync.syncIntervalSeconds"
              class="h-7 rounded-lg border border-black/10 bg-transparent px-2 text-[11px] text-gray-700 outline-none dark:border-white/10 dark:text-white/80"
              :disabled="!settingsStore.settings.autoSync.enabled"
              @change="updateInterval"
            >
              <option v-for="option in intervalOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
            </select>
            <button
              type="button"
              role="switch"
              :aria-checked="settingsStore.settings.autoSync.enabled"
              class="relative h-5 w-9 rounded-full bg-black/15 transition dark:bg-white/15"
              :class="{ 'bg-accent': settingsStore.settings.autoSync.enabled }"
              @click="toggleAutoSync"
            >
              <span class="absolute left-0.5 top-0.5 h-4 w-4 rounded-full bg-white shadow-sm transition-transform" :class="{ 'translate-x-4': settingsStore.settings.autoSync.enabled }"></span>
            </button>
          </div>
        </div>

        <div v-if="syncErrorDetails.length > 0" class="mt-2 rounded-lg bg-rose-500/8 px-3 py-2.5 text-xs text-rose-700 dark:bg-rose-400/10 dark:text-rose-200">
          <div class="font-semibold">同步遇到以下问题：</div>
          <ul class="mt-1.5 space-y-1">
            <li v-for="(item, index) in syncErrorDetails" :key="`${item.category}-${index}-${item.error}`" class="break-words">
              <span class="font-medium">{{ item.category }}：</span>{{ item.error }}
            </li>
          </ul>
        </div>
      </div>
    </transition>
  </section>
</template>

<style scoped>
.account-cloud-sync-panel-enter-active,
.account-cloud-sync-panel-leave-active {
  overflow: hidden;
  transition: opacity 0.25s ease, max-height 0.25s ease, transform 0.25s ease;
  max-height: 720px;
}

.account-cloud-sync-panel-enter-from,
.account-cloud-sync-panel-leave-to {
  max-height: 0;
  opacity: 0;
  transform: translateY(-6px);
}
</style>
