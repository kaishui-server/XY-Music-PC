<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue';
import { Pause, Play, SkipForward } from 'lucide-vue-next';

import { useLyrics } from '../../composables/lyrics';
import { usePlaybackController } from '../../features/playback';
import { getSongSourceLabel, isRemoteSong } from '../../utils/remoteSong';
import { useAppLanguage } from '../../i18n';

const {
  currentSong,
  currentTime,
  formatDuration,
  isPlaying,
  nextSong,
  seekTo,
  togglePlay,
} = usePlaybackController();
const { currentLyricIndex, currentLyricLine, parsedLyrics } = useLyrics();
const { t } = useAppLanguage();

const songTitle = computed(() => {
  const song = currentSong.value;
  if (!song) return t('home.noCurrentSong');
  return song.title || song.name?.replace(/\.[^/.]+$/, '') || t('home.unknownSong');
});

const songArtist = computed(() => currentSong.value?.artist || t('home.unknownSinger'));

const titleViewportRef = ref<HTMLElement | null>(null);
const titleTextRef = ref<HTMLElement | null>(null);
const shouldScrollTitle = ref(false);
const titleScrollDuration = ref(12);
let titleResizeObserver: ResizeObserver | null = null;
let titleCheckFrame: number | null = null;

const checkTitleOverflow = () => {
  void nextTick(() => {
    if (titleCheckFrame !== null) cancelAnimationFrame(titleCheckFrame);
    titleCheckFrame = requestAnimationFrame(() => {
      titleCheckFrame = null;
      const viewport = titleViewportRef.value;
      const text = titleTextRef.value;
      if (!viewport || !text) {
        shouldScrollTitle.value = false;
        return;
      }

      const textWidth = text.getBoundingClientRect().width;
      const viewportWidth = viewport.getBoundingClientRect().width;
      shouldScrollTitle.value = textWidth > viewportWidth + 1;
      titleScrollDuration.value = Math.max(10, Math.min(36, textWidth / 35));
    });
  });
};

const setupTitleOverflowObserver = () => {
  titleResizeObserver?.disconnect();
  if (typeof ResizeObserver !== 'undefined') {
    titleResizeObserver = new ResizeObserver(checkTitleOverflow);
    if (titleViewportRef.value) titleResizeObserver.observe(titleViewportRef.value);
    if (titleTextRef.value) titleResizeObserver.observe(titleTextRef.value);
  }
  checkTitleOverflow();
};

watch(songTitle, checkTitleOverflow);

const sourceLabel = computed(() => {
  const song = currentSong.value;
  if (!song) return t('home.waitingToPlay');
  const path = song.path || '';
  if (path.startsWith('lx://') || path.startsWith('plugin://') || isRemoteSong(song)) {
    return getSongSourceLabel(song);
  }
  if (/^https?:\/\//i.test(path)) return t('home.onlineMusic');
  return t('home.localMusic');
});

const duration = computed(() => Math.max(0, Number(currentSong.value?.duration) || 0));
const progressBarRef = ref<HTMLElement | null>(null);
const isDraggingProgress = ref(false);
const dragTime = ref(0);
const displayedTime = computed(() => (
  isDraggingProgress.value ? dragTime.value : currentTime.value
));
const progressPercent = computed(() => {
  if (duration.value <= 0) return 0;
  return Math.min(100, Math.max(0, (displayedTime.value / duration.value) * 100));
});

const lyricText = computed(() => {
  if (!currentSong.value) return t('home.lyricPlaceholder');
  const text = currentLyricLine.value?.text?.trim();
  if (!text || text === '···') return '···';
  if (text === 'Loading lyrics...') return t('home.loadingLyrics');
  if (text === 'Lyrics unavailable') return t('home.lyricsUnavailable');
  if (text === 'No synchronized lyrics') return t('home.noSyncedLyrics');
  if (text === 'Instrumental / No lyrics') return t('home.instrumental');
  return text;
});

const lyricTranslation = computed(() => {
  if (!currentSong.value || currentLyricIndex.value < 0) return '';
  const line = parsedLyrics.value[currentLyricIndex.value];
  if (!line) return '';

  const candidates = [line.translation, ...(line.secondary ?? [])]
    .map(text => text?.trim())
    .filter((text): text is string => !!text && text !== lyricText.value);
  if (candidates.length === 0) return '';

  // 优先显示含中文的翻译，兼容部分歌词源把中文放在 secondary 中的情况。
  return candidates.find(text => /[\u3400-\u9fff]/u.test(text)) ?? candidates[0];
});

const updateProgressFromPointer = (clientX: number) => {
  if (!progressBarRef.value || !currentSong.value || duration.value <= 0) return;
  const rect = progressBarRef.value.getBoundingClientRect();
  if (rect.width <= 0) return;
  const ratio = Math.min(1, Math.max(0, (clientX - rect.left) / rect.width));
  dragTime.value = ratio * duration.value;
};

const startProgressDrag = (event: PointerEvent) => {
  if (!currentSong.value || duration.value <= 0) return;
  if (event.pointerType === 'mouse' && event.button !== 0) return;
  event.preventDefault();
  (event.currentTarget as HTMLElement | null)?.setPointerCapture?.(event.pointerId);
  isDraggingProgress.value = true;
  updateProgressFromPointer(event.clientX);
};

const finishProgressDrag = (commit: boolean) => {
  if (!isDraggingProgress.value) return;
  const targetTime = dragTime.value;
  isDraggingProgress.value = false;
  if (commit) void seekTo(targetTime);
};

const onGlobalPointerMove = (event: PointerEvent) => {
  if (!isDraggingProgress.value) return;
  event.preventDefault();
  updateProgressFromPointer(event.clientX);
};

const handleProgressKeydown = (event: KeyboardEvent) => {
  if (!currentSong.value || duration.value <= 0) return;

  let targetTime: number | null = null;
  if (event.key === 'ArrowLeft') targetTime = currentTime.value - 5;
  if (event.key === 'ArrowRight') targetTime = currentTime.value + 5;
  if (event.key === 'Home') targetTime = 0;
  if (event.key === 'End') targetTime = duration.value;
  if (targetTime === null) return;

  event.preventDefault();
  void seekTo(Math.min(duration.value, Math.max(0, targetTime)));
};

const onGlobalPointerUp = () => finishProgressDrag(true);
const onGlobalPointerCancel = () => finishProgressDrag(false);

onMounted(() => {
  window.addEventListener('pointermove', onGlobalPointerMove);
  window.addEventListener('pointerup', onGlobalPointerUp);
  window.addEventListener('pointercancel', onGlobalPointerCancel);
  window.addEventListener('resize', checkTitleOverflow);
  void nextTick(setupTitleOverflowObserver);
});

onUnmounted(() => {
  window.removeEventListener('pointermove', onGlobalPointerMove);
  window.removeEventListener('pointerup', onGlobalPointerUp);
  window.removeEventListener('pointercancel', onGlobalPointerCancel);
  window.removeEventListener('resize', checkTitleOverflow);
  titleResizeObserver?.disconnect();
  titleResizeObserver = null;
  if (titleCheckFrame !== null) {
    cancelAnimationFrame(titleCheckFrame);
    titleCheckFrame = null;
  }
});
</script>

<template>
  <section class="home-now-playing animate-fade-in-up">
    <div class="flex min-h-[240px] flex-col justify-between gap-8">
      <div class="min-w-0">
        <div class="mb-4 text-base font-semibold tracking-[0.22em] text-gray-900 dark:text-white">{{ t('home.nowPlaying') }}</div>
        <h2
          ref="titleViewportRef"
          class="min-h-[1.28em] overflow-hidden whitespace-nowrap py-[0.08em] text-[clamp(2rem,5vw,4.5rem)] font-black leading-[1.16] tracking-[-0.04em] text-gray-900 dark:text-white"
        >
          <span
            class="home-title-track inline-flex w-max"
            :class="{ 'home-title-track--scrolling': shouldScrollTitle }"
            :style="shouldScrollTitle ? { '--home-title-duration': `${titleScrollDuration}s` } : undefined"
          >
            <span ref="titleTextRef" class="shrink-0 pr-[0.35em]">{{ songTitle }}</span>
            <span v-if="shouldScrollTitle" aria-hidden="true" class="shrink-0 pr-[0.35em]">{{ songTitle }}</span>
          </span>
        </h2>
        <div class="mt-3 flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 text-sm text-gray-600 dark:text-white/60">
          <span class="truncate font-semibold">{{ songArtist }}</span>
          <span aria-hidden="true">·</span>
          <span>{{ sourceLabel }}</span>
        </div>
      </div>

      <div class="space-y-3">
        <div
          ref="progressBarRef"
          role="slider"
          :tabindex="currentSong && duration > 0 ? 0 : -1"
          :aria-label="t('home.seek')"
          aria-valuemin="0"
          :aria-valuemax="Math.round(duration)"
          :aria-valuenow="Math.round(displayedTime)"
          :aria-valuetext="`${formatDuration(displayedTime)} / ${formatDuration(duration)}`"
          :aria-disabled="!currentSong || duration <= 0"
          class="group relative block h-5 w-full select-none [touch-action:none]"
          :class="currentSong && duration > 0 ? 'cursor-pointer' : 'cursor-default'"
          @pointerdown="startProgressDrag"
          @keydown="handleProgressKeydown"
        >
          <span
            class="absolute inset-x-0 top-1/2 h-2 -translate-y-1/2 overflow-visible rounded-full bg-black/10 dark:bg-white/15"
          >
            <span
              class="relative block h-full rounded-full bg-accent group-hover:brightness-105"
              :class="isDraggingProgress ? '' : 'transition-[width] duration-150 ease-linear'"
              :style="{ width: `${progressPercent}%` }"
            >
              <span
                class="absolute right-0 top-1/2 h-3 w-3 -translate-y-1/2 translate-x-1/2 rounded-full border border-black/10 bg-white/60 shadow-sm transition-[opacity,transform] dark:border-white/15 dark:bg-white/60"
                :class="isDraggingProgress
                  ? 'scale-100 opacity-100'
                  : 'scale-75 opacity-0 group-hover:scale-100 group-hover:opacity-100'"
              />
            </span>
          </span>
        </div>

        <div class="flex items-center justify-between gap-4">
          <div class="text-xs tabular-nums text-gray-500 dark:text-white/50">
            {{ formatDuration(displayedTime) }} / {{ formatDuration(duration) }}
          </div>
          <div class="flex items-center gap-2">
            <button
              type="button"
              class="grid h-11 w-11 place-items-center rounded-full bg-accent text-white shadow-lg shadow-accent/20 transition hover:bg-accent-hover active:scale-95 disabled:cursor-not-allowed disabled:opacity-40"
              :disabled="!currentSong"
              :aria-label="isPlaying ? t('home.pause') : t('home.play')"
              @click="togglePlay"
            >
              <Pause v-if="isPlaying" class="h-5 w-5" fill="currentColor" />
              <Play v-else class="ml-0.5 h-5 w-5" fill="currentColor" />
            </button>
            <button
              type="button"
              class="grid h-11 w-11 place-items-center rounded-full bg-black/5 text-gray-700 transition hover:bg-black/10 active:scale-95 disabled:cursor-not-allowed disabled:opacity-40 dark:bg-white/10 dark:text-white dark:hover:bg-white/15"
              :disabled="!currentSong"
              :aria-label="t('home.next')"
              @click="nextSong"
            >
              <SkipForward class="h-5 w-5" fill="currentColor" />
            </button>
          </div>
        </div>
      </div>

      <div class="min-h-[3.4em]">
        <p class="line-clamp-2 text-[clamp(1rem,1.8vw,1.35rem)] font-medium leading-relaxed text-gray-700 dark:text-white/75">
          {{ lyricText }}
        </p>
        <p
          v-if="lyricTranslation"
          class="mt-1 line-clamp-2 text-[clamp(0.85rem,1.35vw,1.05rem)] leading-relaxed text-gray-500 dark:text-white/50"
        >
          {{ lyricTranslation }}
        </p>
      </div>
    </div>
  </section>
</template>

<style scoped>
.home-now-playing {
  padding: clamp(0.75rem, 2vw, 1.75rem) clamp(1rem, 2.5vw, 3rem);
}

@keyframes home-title-marquee {
  from { transform: translateX(0); }
  to { transform: translateX(-50%); }
}

.home-title-track--scrolling {
  animation: home-title-marquee var(--home-title-duration, 12s) linear infinite;
  will-change: transform;
}

@media (prefers-reduced-motion: reduce) {
  .home-title-track--scrolling {
    animation: none;
  }
}
</style>
