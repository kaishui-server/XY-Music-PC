<script setup lang="ts">
import { computed, onUnmounted, ref } from 'vue';
import { GripVertical } from 'lucide-vue-next';

import { useSettings } from '../../features/settings/useSettings';
import {
  HOME_MODULE_ITEMS,
  getHomeModuleVisibilityKey,
  normalizeHomeModuleOrder,
} from '../../features/settings/homeItems';
import type { HomeModuleKey, HomeSettings } from '../../types';
import { resolveDragTargetIndex } from '../../utils/dragSort';
import { useToast } from '../../composables/toast';
import SettingHint from './SettingHint.vue';

withDefaults(defineProps<{
  showPreview?: boolean;
}>(), {
  showPreview: false,
});

defineEmits<{
  (event: 'preview'): void;
}>();

const { settings, patchSettings } = useSettings();
const { showToast } = useToast();

const orderedItems = computed(() => normalizeHomeModuleOrder(settings.value.home?.order)
  .map(key => HOME_MODULE_ITEMS.find(item => item.key === key))
  .filter((item): item is (typeof HOME_MODULE_ITEMS)[number] => !!item));

const isVisible = (key: HomeModuleKey) => {
  const visibilityKey = getHomeModuleVisibilityKey(key);
  return settings.value.home?.[visibilityKey] !== false;
};

const enabledCount = computed(() => orderedItems.value.filter(item => isVisible(item.key)).length);

const toggleVisible = (key: HomeModuleKey) => {
  if (isVisible(key) && enabledCount.value <= 1) {
    showToast('首页至少需要显示一个模块', 'info');
    return;
  }

  const visibilityKey = getHomeModuleVisibilityKey(key);
  patchSettings({
    home: {
      [visibilityKey]: !isVisible(key),
    } as Partial<HomeSettings>,
  });
};

const listRef = ref<HTMLElement | null>(null);
const draggingIndex = ref<number | null>(null);

const applyOrder = (nextOrder: HomeModuleKey[]) => {
  patchSettings({ home: { order: normalizeHomeModuleOrder(nextOrder) } });
};

const moveItem = (from: number, to: number) => {
  if (from === to || from < 0 || to < 0) return;
  const order = orderedItems.value.map(item => item.key);
  if (from >= order.length || to >= order.length) return;
  const [moved] = order.splice(from, 1);
  order.splice(to, 0, moved);
  applyOrder(order);
};

const handlePointerMove = (event: PointerEvent) => {
  const currentIndex = draggingIndex.value;
  if (currentIndex === null) return;
  event.preventDefault();

  const target = resolveDragTargetIndex(
    listRef.value,
    '[data-home-module-row]',
    event.clientY,
    currentIndex,
  );
  if (target === null || target === currentIndex) return;
  moveItem(currentIndex, target);
  draggingIndex.value = target;
};

const stopDragging = () => {
  draggingIndex.value = null;
  window.removeEventListener('pointermove', handlePointerMove);
  window.removeEventListener('pointerup', stopDragging);
  window.removeEventListener('pointercancel', stopDragging);
};

const startDragging = (index: number, event: PointerEvent) => {
  if (event.button !== 0) return;
  event.preventDefault();
  draggingIndex.value = index;
  window.addEventListener('pointermove', handlePointerMove, { passive: false });
  window.addEventListener('pointerup', stopDragging);
  window.addEventListener('pointercancel', stopDragging);
};

onUnmounted(stopDragging);
</script>

<template>
  <section class="w-full space-y-3">
    <h2 class="flex items-center justify-between gap-4 text-sm font-bold text-gray-800 dark:text-gray-200">
      <span class="flex items-center gap-2">
        <span class="h-4 w-1 rounded-full bg-accent"></span>
        首页管理
      </span>
      <span class="flex items-center gap-2">
        <button
          v-if="showPreview"
          type="button"
          class="rounded-full border border-accent/25 bg-accent/10 px-3 py-1 text-xs font-semibold text-accent transition hover:bg-accent/20 active:scale-95"
          @click="$emit('preview')"
        >
          预览
        </button>
        <SettingHint text="拖动左侧手柄调整首页模块顺序。四个模块至少需要开启一个。" />
      </span>
    </h2>

    <div
      ref="listRef"
      class="flex flex-col overflow-hidden rounded-xl border border-gray-200/40 bg-white/20 dark:border-gray-800/40 dark:bg-black/10"
    >
      <TransitionGroup name="home-module-sort" tag="div" class="flex flex-col">
        <div
          v-for="(item, index) in orderedItems"
          :key="item.key"
          data-home-module-row
          class="flex items-center justify-between gap-4 p-4 transition-colors"
          :class="draggingIndex === index
            ? 'bg-accent/10 ring-1 ring-inset ring-accent/30'
            : 'hover:bg-white/40 dark:hover:bg-white/10'"
        >
          <div class="flex min-w-0 items-center gap-3">
            <GripVertical
              class="h-4 w-4 shrink-0 touch-none select-none text-gray-400 transition-colors hover:text-accent dark:text-white/35"
              :class="draggingIndex === index ? 'cursor-grabbing text-accent' : 'cursor-grab'"
              @pointerdown="startDragging(index, $event)"
            />
            <div class="min-w-0">
              <div class="truncate text-sm font-medium text-gray-800 dark:text-gray-200">{{ item.label }}</div>
              <div class="mt-0.5 truncate text-xs text-gray-500 dark:text-gray-400">{{ item.description }}</div>
            </div>
          </div>

          <button
            type="button"
            class="relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors focus:outline-none"
            :class="[
              isVisible(item.key) ? 'bg-accent' : 'bg-gray-300 dark:bg-gray-700',
              isVisible(item.key) && enabledCount <= 1 ? 'cursor-not-allowed opacity-60' : '',
            ]"
            :aria-pressed="isVisible(item.key)"
            :title="isVisible(item.key) && enabledCount <= 1 ? '首页至少需要显示一个模块' : undefined"
            @click="toggleVisible(item.key)"
          >
            <span
              class="inline-block h-4 w-4 rounded-full bg-white shadow-sm transition-transform duration-200 ease-in-out"
              :class="isVisible(item.key) ? 'translate-x-6' : 'translate-x-1'"
            />
          </button>
        </div>
      </TransitionGroup>
    </div>
  </section>
</template>

<style scoped>
.home-module-sort-move {
  transition: transform 280ms cubic-bezier(0.22, 1, 0.36, 1);
  will-change: transform;
}

@media (prefers-reduced-motion: reduce) {
  .home-module-sort-move {
    transition: none;
  }
}
</style>
