<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue';
import { open } from '@tauri-apps/plugin-dialog';
import { ChevronDown } from 'lucide-vue-next';

import { downloadToLocal } from '../../composables/useDownloadToLocal';
import { useDownloadDialog } from '../../composables/useDownloadDialog';
import { useSettings } from '../../features/settings/useSettings';
import { usePlaybackStore } from '../../features/playback/store';
import { getOnlineAvailableQualities } from '../../features/playback/onlinePlaybackResolver';
import { probeDownloadableQualities } from '../../services/downloadService';
import { downloadApi } from '../../services/tauri/downloadApi';
import { formatFileSize } from '../../utils/format';
import { ALL_QUALITY_KEYS, QUALITY_META } from '../../types';
import type { DownloadQuality, QualityKey, Song } from '../../types';

const props = defineProps<{ visible: boolean; song: Song | null }>();
const emit = defineEmits<{ (e: 'close'): void }>();

const { settings } = useSettings();
const playbackStore = usePlaybackStore();
const { downloadExtraLyrics, downloadExtraCover } = useDownloadDialog();

// 音质与目录每次打开都跟随下载设置；这里只记忆两个独立文件开关。
const selectedQuality = ref<DownloadQuality>('320k');
const downloadDir = ref('');
const availableQualities = ref<QualityKey[] | null>(null);
const declaredQualities = ref<QualityKey[] | null>(null);
const probedUrls = ref<Partial<Record<QualityKey, string>>>({});
const qualitySizes = ref<Partial<Record<QualityKey, number>>>({});
const isProbing = ref(false);
const isQualityMenuOpen = ref(false);
const qualitySelectorRef = ref<HTMLElement | null>(null);

/** 当前探测任务的中止控制器（弹窗关闭或切歌时中止，防止旧结果覆盖新歌） */
let probeController: AbortController | null = null;

const isCurrentPlaybackSong = (song: Song) => {
  const playingSong = playbackStore.currentSong;
  const targetSourcePath = song.cue_source_path || song.path;
  const playingSourcePath = playingSong?.cue_source_path || playingSong?.path;
  return playingSong?.path === song.path || playingSourcePath === targetSourcePath;
};

const getPlaybackAvailableQualities = (song: Song): QualityKey[] | null => {
  if (!isCurrentPlaybackSong(song)) return null;
  const qualities = playbackStore.currentAvailableQualities;
  return qualities && qualities.length > 0 ? [...qualities] : null;
};

const ensureSelectedQualityAvailable = (available: QualityKey[]) => {
  if (available.length > 0 && !available.includes(selectedQuality.value as QualityKey)) {
    selectedQuality.value = available[available.length - 1];
  }
};

const compactFileSize = (bytes: number) => formatFileSize(bytes)
  .replace(/\s*MB$/, 'M')
  .replace(/\s*GB$/, 'G')
  .replace(/\s*KB$/, 'K');

const getAudioExtLabel = (key: QualityKey, url?: string) => {
  if (url) {
    try {
      const match = new URL(url).pathname.toLowerCase().match(/\.([a-z0-9]+)$/);
      if (match?.[1]) return match[1].toUpperCase();
    } catch {
      const match = url.toLowerCase().match(/\.([a-z0-9]+)(?:[?#]|$)/);
      if (match?.[1]) return match[1].toUpperCase();
    }
  }
  return QUALITY_META[key]?.isLossless ? 'FLAC' : 'MP3';
};

const qualityExtraText = (key: QualityKey) => {
  const url = probedUrls.value[key];
  const size = qualitySizes.value[key];
  const ext = getAudioExtLabel(key, url);
  if (typeof size === 'number' && size > 0) return `${ext} · ${compactFileSize(size)}`;
  if (isProbing.value) return `${ext} · 探测中`;
  return `${ext} · 未知体积`;
};

const probeQualitySizes = async (
  urls: Partial<Record<QualityKey, string>>,
  signal: AbortSignal,
) => {
  const entries = Object.entries(urls) as Array<[QualityKey, string]>;
  await Promise.all(entries.map(async ([key, url]) => {
    try {
      const info = await downloadApi.probeUrlSize(url);
      if (!signal.aborted && typeof info?.size === 'number' && info.size > 0) {
        qualitySizes.value = { ...qualitySizes.value, [key]: info.size };
      }
    } catch (error: any) {
      if (!signal.aborted) {
        console.warn(`[DownloadDialog] ${key} 体积探测失败:`, error?.message || error);
      }
    }
  }));
};

const unsupportedQualityKeys = computed<QualityKey[]>(() => {
  if (isProbing.value || availableQualities.value === null) return [];
  return ALL_QUALITY_KEYS.filter(key => !availableQualities.value?.includes(key));
});

const orderedQualityKeys = computed<QualityKey[]>(() => {
  if (unsupportedQualityKeys.value.length === 0) return ALL_QUALITY_KEYS;

  const unsupported = new Set(unsupportedQualityKeys.value);
  return [
    ...ALL_QUALITY_KEYS.filter(key => !unsupported.has(key)),
    ...ALL_QUALITY_KEYS.filter(key => unsupported.has(key)),
  ];
});

const hasNoAvailableQuality = computed(() =>
  !isProbing.value
  && availableQualities.value !== null
  && availableQualities.value.length === 0,
);

const selectQuality = (key: QualityKey) => {
  if (unsupportedQualityKeys.value.includes(key)) return;
  selectedQuality.value = key;
  isQualityMenuOpen.value = false;
};

const handleDocumentPointerDown = (event: PointerEvent) => {
  if (!qualitySelectorRef.value?.contains(event.target as Node)) {
    isQualityMenuOpen.value = false;
  }
};

const abortProbe = () => {
  probeController?.abort();
  probeController = null;
  isProbing.value = false;
};

const probeQualities = async (song: Song) => {
  const songPath = song.cue_source_path || song.path;
  if (!songPath.startsWith('lx://') && !songPath.startsWith('plugin://')) return;

  abortProbe();
  const controller = new AbortController();
  probeController = controller;
  isProbing.value = true;

  try {
    try {
      declaredQualities.value = await getOnlineAvailableQualities(songPath, song);
    } catch {
      declaredQualities.value = null;
    }
    if (controller.signal.aborted) return;

    const result = await probeDownloadableQualities(song, declaredQualities.value, {
      signal: controller.signal,
    });
    if (controller.signal.aborted) return;

    availableQualities.value = result.available;
    probedUrls.value = result.resolvedUrls;
    void probeQualitySizes(result.resolvedUrls, controller.signal);
    ensureSelectedQualityAvailable(result.available);
  } catch (error: any) {
    if (!controller.signal.aborted) {
      console.warn('[DownloadDialog] 音质探测失败:', error?.message || error);
      availableQualities.value = null;
    }
  } finally {
    if (probeController === controller) {
      probeController = null;
      isProbing.value = false;
    }
  }
};

watch(
  () => [props.visible, props.song] as const,
  ([visible, song]) => {
    if (!visible) {
      abortProbe();
      isQualityMenuOpen.value = false;
      return;
    }

    selectedQuality.value = (settings.value.download.quality as DownloadQuality) ?? '320k';
    downloadDir.value = settings.value.download.downloadPath ?? '';
    availableQualities.value = null;
    declaredQualities.value = null;
    probedUrls.value = {};
    qualitySizes.value = {};

    if (song) {
      const playbackQualities = getPlaybackAvailableQualities(song);
      if (playbackQualities) {
        availableQualities.value = playbackQualities;
        declaredQualities.value = playbackQualities;
        ensureSelectedQualityAvailable(playbackQualities);
      }
      void probeQualities(song);
    }
  },
);

onMounted(() => document.addEventListener('pointerdown', handleDocumentPointerDown));
onUnmounted(() => {
  abortProbe();
  document.removeEventListener('pointerdown', handleDocumentPointerDown);
});

const chooseDir = async () => {
  const selected = await open({ directory: true, multiple: false, title: '选择下载目录' });
  if (selected && typeof selected === 'string') downloadDir.value = selected;
};

const handleDownload = async () => {
  if (!props.song) return;
  const song = props.song;
  const preResolvedUrls = probedUrls.value;
  emit('close');
  await downloadToLocal(song, {
    quality: selectedQuality.value,
    downloadDir: downloadDir.value || undefined,
    downloadAudio: true,
    downloadLyrics: downloadExtraLyrics.value,
    downloadCover: downloadExtraCover.value,
    lyricsFormat: 'lrc',
    embedLyrics: true,
    embedCover: true,
    preResolvedUrls,
  });
};
</script>

<template>
  <Teleport to="body">
    <Transition name="modal-pop">
      <div
        v-if="visible"
        class="fixed inset-0 z-[9999] flex items-center justify-center bg-black/30 backdrop-blur-sm"
        @click.self="emit('close')"
      >
        <div class="modal-content bg-white dark:bg-gray-900 rounded-xl shadow-2xl w-[520px] max-w-[90vw] overflow-hidden">
          <div class="px-5 py-3.5 border-b border-gray-100 dark:border-gray-800 flex justify-between items-center">
            <h3 class="font-bold text-gray-800 dark:text-gray-200 text-base">下载歌曲</h3>
            <button
              type="button"
              class="text-gray-400 hover:text-gray-600 dark:hover:text-gray-200 transition-colors"
              aria-label="关闭"
              @click="emit('close')"
            >
              ✕
            </button>
          </div>

          <div class="px-5 py-4 space-y-4 max-h-[70vh] overflow-y-auto custom-scrollbar">
            <div v-if="song" class="flex items-center gap-3">
              <div class="min-w-0 flex-1">
                <div class="text-sm font-medium text-gray-800 dark:text-gray-200 truncate">
                  {{ song.title || song.name || '未知歌曲' }}
                </div>
                <div class="text-xs text-gray-400 truncate">{{ song.artist || '未知歌手' }}</div>
              </div>
            </div>

            <div>
              <div class="text-xs font-semibold text-gray-500 dark:text-gray-400 mb-2">
                下载音质
                <span v-if="isProbing" class="text-gray-400 font-normal">（正在探测可用音质…）</span>
              </div>
              <div ref="qualitySelectorRef" class="relative">
                <button
                  type="button"
                  class="flex h-9 w-full items-center justify-between gap-3 rounded-md border border-gray-200/70 bg-gray-50 px-3 text-left text-xs text-gray-700 outline-none transition-colors hover:border-accent/40 focus:border-accent focus:ring-2 focus:ring-accent/15 disabled:cursor-wait disabled:opacity-60 dark:border-white/10 dark:bg-white/5 dark:text-gray-200"
                  :disabled="isProbing"
                  :aria-expanded="isQualityMenuOpen"
                  aria-haspopup="listbox"
                  @click="isQualityMenuOpen = !isQualityMenuOpen"
                  @keydown.esc="isQualityMenuOpen = false"
                >
                  <span class="min-w-0 truncate">
                    <span class="font-semibold">{{ QUALITY_META[selectedQuality].label }}</span>
                    <span class="ml-1.5 text-gray-400 dark:text-gray-500">
                      {{ QUALITY_META[selectedQuality].description }} · {{ qualityExtraText(selectedQuality) }}
                    </span>
                  </span>
                  <ChevronDown
                    class="h-3.5 w-3.5 shrink-0 text-gray-400 transition-transform duration-200"
                    :class="isQualityMenuOpen ? 'rotate-180' : ''"
                    aria-hidden="true"
                  />
                </button>

                <Transition name="quality-dropdown">
                  <div
                    v-if="isQualityMenuOpen"
                    role="listbox"
                    class="absolute inset-x-0 top-full z-30 mt-1 max-h-60 overflow-y-auto rounded-lg border border-gray-200/70 bg-white p-1 shadow-xl dark:border-white/10 dark:bg-gray-800"
                  >
                    <button
                      v-for="key in orderedQualityKeys"
                      :key="key"
                      type="button"
                      role="option"
                      class="flex w-full items-center justify-between gap-3 rounded-md px-2.5 py-1.5 text-left text-xs transition-colors"
                      :class="unsupportedQualityKeys.includes(key)
                        ? 'cursor-not-allowed text-gray-300 dark:text-gray-600'
                        : selectedQuality === key
                          ? 'bg-accent/10 text-accent'
                          : 'text-gray-700 hover:bg-gray-100 dark:text-gray-200 dark:hover:bg-white/10'"
                      :aria-selected="selectedQuality === key"
                      :disabled="unsupportedQualityKeys.includes(key)"
                      @click="selectQuality(key)"
                    >
                      <span class="font-medium">{{ QUALITY_META[key].label }}</span>
                      <span class="truncate text-[10px] opacity-70">
                        {{ QUALITY_META[key].description }} · {{ unsupportedQualityKeys.includes(key) ? '不可用' : qualityExtraText(key) }}
                      </span>
                    </button>
                  </div>
                </Transition>
              </div>

              <div
                v-if="hasNoAvailableQuality"
                class="mt-2 px-3 py-2.5 text-xs rounded-md bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-300"
              >
                未探测到可下载的音质，仍可点击下载尝试（会自动降级并使用后端兜底音源）。
              </div>

            </div>

            <div>
              <div class="text-xs font-semibold text-gray-500 dark:text-gray-400 mb-2">下载目录</div>
              <div class="flex items-center gap-2">
                <div
                  class="flex-1 min-w-0 px-3 py-2 text-xs rounded-md bg-gray-100 dark:bg-white/5 text-gray-600 dark:text-gray-300 truncate"
                  :title="downloadDir"
                >
                  {{ downloadDir || '未选择（点击右侧按钮选择）' }}
                </div>
                <button
                  type="button"
                  class="shrink-0 px-3 py-2 text-xs font-medium rounded-md bg-accent text-white hover:bg-accent-hover transition-colors"
                  @click="chooseDir"
                >
                  选择
                </button>
              </div>
            </div>

            <div>
              <div class="text-xs font-semibold text-gray-500 dark:text-gray-400 mb-2">额外下载</div>
              <div class="space-y-2">
                <button
                  type="button"
                  class="flex w-full items-center justify-between gap-4 rounded-lg bg-gray-50 px-3 py-2.5 text-left transition-colors hover:bg-gray-100 dark:bg-white/5 dark:hover:bg-white/10"
                  :aria-pressed="downloadExtraLyrics"
                  @click="downloadExtraLyrics = !downloadExtraLyrics"
                >
                  <span class="min-w-0">
                    <span class="block text-sm font-medium text-gray-700 dark:text-gray-200">额外下载独立歌词（LRC）</span>
                    <span class="block text-[11px] text-gray-400 dark:text-gray-500">在歌曲文件旁保存独立的歌词文件</span>
                  </span>
                  <span
                    class="relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors"
                    :class="downloadExtraLyrics ? 'bg-accent' : 'bg-gray-300 dark:bg-gray-700'"
                    aria-hidden="true"
                  >
                    <span
                      class="inline-block h-4 w-4 rounded-full bg-white shadow-sm transition-transform"
                      :class="downloadExtraLyrics ? 'translate-x-6' : 'translate-x-1'"
                    />
                  </span>
                </button>

                <button
                  type="button"
                  class="flex w-full items-center justify-between gap-4 rounded-lg bg-gray-50 px-3 py-2.5 text-left transition-colors hover:bg-gray-100 dark:bg-white/5 dark:hover:bg-white/10"
                  :aria-pressed="downloadExtraCover"
                  @click="downloadExtraCover = !downloadExtraCover"
                >
                  <span class="min-w-0">
                    <span class="block text-sm font-medium text-gray-700 dark:text-gray-200">额外下载封面</span>
                    <span class="block text-[11px] text-gray-400 dark:text-gray-500">在歌曲文件旁保存独立的封面图片</span>
                  </span>
                  <span
                    class="relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors"
                    :class="downloadExtraCover ? 'bg-accent' : 'bg-gray-300 dark:bg-gray-700'"
                    aria-hidden="true"
                  >
                    <span
                      class="inline-block h-4 w-4 rounded-full bg-white shadow-sm transition-transform"
                      :class="downloadExtraCover ? 'translate-x-6' : 'translate-x-1'"
                    />
                  </span>
                </button>
              </div>
              <p class="mt-2 text-[11px] text-gray-400 dark:text-gray-500">
                歌曲文件始终内嵌歌词与封面
              </p>
            </div>
          </div>

          <div class="px-5 py-3.5 border-t border-gray-100 dark:border-gray-800 flex justify-end gap-2">
            <button
              type="button"
              class="px-4 py-2 text-sm font-medium rounded-md text-gray-600 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-white/5 transition-colors"
              @click="emit('close')"
            >
              取消
            </button>
            <button
              type="button"
              class="px-4 py-2 text-sm font-medium rounded-md bg-accent text-white hover:bg-accent-hover transition-colors"
              @click="handleDownload"
            >
              下载
            </button>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.quality-dropdown-enter-active,
.quality-dropdown-leave-active {
  transform-origin: top center;
  transition: opacity 160ms ease, transform 180ms cubic-bezier(0.2, 0.8, 0.2, 1);
}

.quality-dropdown-enter-from,
.quality-dropdown-leave-to {
  opacity: 0;
  transform: translateY(-6px) scaleY(0.96);
}
</style>
