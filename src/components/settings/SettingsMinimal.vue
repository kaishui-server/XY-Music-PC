<script setup lang="ts">
import { Check, ChevronDown } from 'lucide-vue-next';
import { computed, onMounted, onScopeDispose, ref } from 'vue';

import { useSettings } from '../../features/settings/useSettings';
import type { PlayerDetailCoverMode } from '../../types';

const { settings, patchSettings } = useSettings();
const coverModeMenuOpen = ref(false);
const coverModeMenuRef = ref<HTMLElement | null>(null);

const coverModeOptions: Array<{ value: PlayerDetailCoverMode; label: string }> = [
  { value: 'show', label: '展示' },
  { value: 'hide', label: '隐藏' },
  { value: 'remember', label: '跟随上次选择' },
];

const currentCoverModeLabel = computed(() => (
  coverModeOptions.find(option => option.value === settings.value.playerDetailCoverMode)?.label
  ?? coverModeOptions[0].label
));

const selectPlayerDetailCoverMode = (value: PlayerDetailCoverMode) => {
  patchSettings({ playerDetailCoverMode: value });
  coverModeMenuOpen.value = false;
};

const handleCoverModeClickOutside = (event: MouseEvent) => {
  if (!coverModeMenuRef.value?.contains(event.target as Node)) {
    coverModeMenuOpen.value = false;
  }
};

onMounted(() => document.addEventListener('click', handleCoverModeClickOutside));
onScopeDispose(() => document.removeEventListener('click', handleCoverModeClickOutside));
</script>

<template>
  <div class="w-full space-y-8">
    <section class="space-y-3">
      <h2 class="flex items-center gap-2 text-sm font-bold text-gray-800 dark:text-gray-200">
        <span class="h-4 w-1 rounded-full bg-accent"></span>
        细节
      </h2>

      <div class="flex items-center justify-between gap-5 rounded-xl border border-gray-200/40 bg-white/20 p-4 dark:border-gray-800/40 dark:bg-black/10">
        <div class="min-w-0">
          <div class="text-sm font-medium text-gray-800 dark:text-gray-200">鼠标悬停按钮时显示详情</div>
          <div class="mt-1 text-xs leading-5 text-gray-500 dark:text-white/45">
            关闭后，设置等图标按钮不再显示文字说明；设置页灰色和黄色感叹号提示不受影响。
          </div>
        </div>
        <button
          type="button"
          role="switch"
          class="relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors focus:outline-none"
          :class="settings.showButtonHoverDetails ? 'bg-accent' : 'bg-gray-300 dark:bg-gray-700'"
          :aria-checked="settings.showButtonHoverDetails"
          aria-label="鼠标悬停按钮时显示详情"
          @click="settings.showButtonHoverDetails = !settings.showButtonHoverDetails"
        >
          <span
            class="inline-block h-4 w-4 rounded-full bg-white shadow-sm transition-transform duration-200 ease-in-out"
            :class="settings.showButtonHoverDetails ? 'translate-x-6' : 'translate-x-1'"
          />
        </button>
      </div>

      <div class="flex items-center justify-between gap-5 overflow-visible rounded-xl border border-gray-200/40 bg-white/20 p-4 dark:border-gray-800/40 dark:bg-black/10">
        <div class="min-w-0 flex-1">
          <div class="text-sm font-medium text-gray-800 dark:text-gray-200">播放详情页封面</div>
          <div class="mt-1 text-xs leading-5 text-gray-500 dark:text-white/45">
            设置每次打开歌曲播放详情页时的封面显示方式。
          </div>
        </div>

        <div ref="coverModeMenuRef" class="relative z-30 shrink-0">
          <button
            type="button"
            aria-label="播放详情页封面"
            aria-haspopup="listbox"
            :aria-expanded="coverModeMenuOpen"
            class="group flex min-w-40 items-center justify-between gap-3 rounded-xl border px-3.5 py-2.5 text-sm font-medium outline-none transition-all duration-200"
            :class="coverModeMenuOpen
              ? 'border-accent/60 bg-accent/10 text-accent shadow-lg shadow-accent/10 ring-2 ring-accent/10'
              : 'border-gray-200/60 bg-white/55 text-gray-700 shadow-sm hover:border-accent/35 hover:bg-white/80 dark:border-white/10 dark:bg-white/[0.06] dark:text-gray-200 dark:hover:bg-white/10'"
            @click.stop="coverModeMenuOpen = !coverModeMenuOpen"
            @keydown.esc.stop="coverModeMenuOpen = false"
          >
            <span>{{ currentCoverModeLabel }}</span>
            <ChevronDown
              class="h-4 w-4 transition-transform duration-300 ease-out"
              :class="coverModeMenuOpen ? 'rotate-180 text-accent' : 'text-gray-400 group-hover:text-accent'"
            />
          </button>

          <Transition name="detail-cover-mode-menu">
            <div
              v-if="coverModeMenuOpen"
              role="listbox"
              aria-label="播放详情页封面选项"
              class="absolute right-0 top-[calc(100%+0.5rem)] w-48 overflow-hidden rounded-xl border border-gray-200/60 bg-white/95 p-1.5 shadow-xl shadow-black/10 backdrop-blur-xl dark:border-white/10 dark:bg-[#252525]/95 dark:shadow-black/30"
            >
              <button
                v-for="option in coverModeOptions"
                :key="option.value"
                type="button"
                role="option"
                :aria-selected="settings.playerDetailCoverMode === option.value"
                class="group/option flex w-full items-center justify-between rounded-lg px-3 py-2.5 text-left text-sm transition-all duration-150"
                :class="settings.playerDetailCoverMode === option.value
                  ? 'bg-accent/12 font-medium text-accent'
                  : 'text-gray-700 hover:translate-x-0.5 hover:bg-black/5 dark:text-gray-200 dark:hover:bg-white/[0.07]'"
                @click="selectPlayerDetailCoverMode(option.value)"
              >
                <span>{{ option.label }}</span>
                <Check
                  class="h-4 w-4 transition-all duration-200"
                  :class="settings.playerDetailCoverMode === option.value ? 'scale-100 opacity-100' : 'scale-75 opacity-0'"
                />
              </button>
            </div>
          </Transition>
        </div>
      </div>
    </section>
  </div>
</template>

<style scoped>
.detail-cover-mode-menu-enter-active,
.detail-cover-mode-menu-leave-active {
  transform-origin: top right;
  transition: opacity 180ms ease, transform 220ms cubic-bezier(0.16, 1, 0.3, 1);
}

.detail-cover-mode-menu-enter-from,
.detail-cover-mode-menu-leave-to {
  opacity: 0;
  transform: translateY(-6px) scale(0.96);
}
</style>
