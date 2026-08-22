<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue';
import { openUrl } from '@tauri-apps/plugin-opener';
import { APP_VERSION } from '../../../version';
import { useUpdateCheck } from '../../composables/useUpdateCheck';
import { useToast } from '../../composables/toast';
import { useDeveloperMode } from '../../features/settings/developerMode';
import { aboutConfig, startAboutConfigPolling, stopAboutConfigPolling } from '../../utils/aboutConfig';

const appVersion = APP_VERSION;
const aboutSections = [
  {
    id: 'concept',
    title: 'XY Music',
    versionLabel: `v${appVersion}`,
    developerModeEntry: true,
    developers: [
      { label: '@ShenYichenCN', url: 'https://github.com/ShenYichenCN' },
      { label: '@知难辞', url: 'https://github.com/88541' },
      { label: '@绛狐', url: 'https://github.com/kaishui-server' },
      { label: '@TaXiaoQi', url: 'https://github.com/TaXiaoQi' },
    ],
  },
  {
    id: 'xianyu-music',
    title: '弦予音乐',
    versionLabel: '正式版',
    developerModeEntry: false,
    developers: [
      { label: '@ShenYichenCN', url: 'https://github.com/ShenYichenCN' },
      { label: '@知难辞', url: 'https://github.com/88541' },
      { label: '@绛狐', url: 'https://github.com/kaishui-server' },
      { label: '@TaXiaoQi', url: 'https://github.com/TaXiaoQi' },
    ],
  },
] as const;

const currentAboutPage = ref<'concept' | 'formal'>('concept');
const currentSection = computed(() =>
  currentAboutPage.value === 'concept' ? aboutSections[0] : aboutSections[1],
);
const isConceptPage = computed(() => currentAboutPage.value === 'concept');

const DEVELOPER_MODE_CLICK_COUNT = 5;
const DEVELOPER_MODE_CLICK_INTERVAL = 1500;
const developerModeClickCount = ref(0);
let lastDeveloperModeClickAt = 0;

const { isDeveloperMode, enableDeveloperMode } = useDeveloperMode();
const { showToast } = useToast();

function handleDeveloperModeClick() {
  if (isDeveloperMode.value) return;

  const now = Date.now();
  if (now - lastDeveloperModeClickAt > DEVELOPER_MODE_CLICK_INTERVAL) {
    developerModeClickCount.value = 0;
  }
  lastDeveloperModeClickAt = now;
  developerModeClickCount.value += 1;

  if (developerModeClickCount.value >= DEVELOPER_MODE_CLICK_COUNT) {
    developerModeClickCount.value = 0;
    enableDeveloperMode();
    showToast('已进入开发者模式', 'success');
  }
}

// 检查更新走 Rust 服务端统一 API，由全局 useUpdateCheck 单例管理弹窗
const { isCheckingUpdate, checkUpdateManual } = useUpdateCheck();

const showConceptMaintenance = () => {
  showToast('维护中，暂不可用', 'info');
};

function normalizeExternalUrl(url: string): string {
  const trimmed = url.trim();
  if (!trimmed) return '';
  return /^[a-zA-Z][a-zA-Z0-9+.-]*:/.test(trimmed) ? trimmed : `https://${trimmed}`;
}

async function openExternal(url: string) {
  const normalized = normalizeExternalUrl(url);
  if (!normalized) return;
  try {
    await openUrl(normalized);
  } catch (error) {
    console.error('[SettingsAbout] 外部链接打开失败，尝试浏览器回退', error);
    window.open(normalized, '_blank', 'noopener,noreferrer');
  }
}

const handleUpdateClick = (sectionId: string) => {
  if (sectionId === 'concept') {
    showConceptMaintenance();
    return;
  }
  void checkUpdateManual();
};

const handleAboutLinkClick = (sectionId: string, url: string) => {
  if (sectionId === 'concept') {
    showConceptMaintenance();
    return;
  }
  void openExternal(url);
};

// 概念版开源地址指向概念版仓库，正式版沿用远程配置
const CONCEPT_PROJECT_URL = 'https://github.com/kaishui-server/XY-Music-Concept.git';
const currentProjectUrl = computed(() =>
  isConceptPage.value ? CONCEPT_PROJECT_URL : aboutConfig.value.projectUrl,
);
const handleProjectUrlClick = () => {
  void openExternal(currentProjectUrl.value);
};

onMounted(() => {
  startAboutConfigPolling();
});

onUnmounted(stopAboutConfigPolling);
</script>

<template>
  <div class="relative min-w-0">
    <section
      class="flex h-[calc(100vh-8rem)] min-w-0 flex-col items-center overflow-hidden"
    >
    <Transition name="about-slide" mode="out-in">
    <div :key="currentAboutPage" class="flex w-full flex-1 flex-col items-center justify-center gap-5 py-3">
      <div class="flex min-w-0 flex-col items-center gap-3 text-center">
      <div class="flex items-center justify-center">
        <img
          src="/logo.png"
          alt="Logo"
          class="h-28 w-28 object-contain dark:invert"
        />
      </div>

      <div class="space-y-1">
        <h1 class="text-2xl font-bold tracking-tight text-gray-800 dark:text-white">{{ currentSection.title }}</h1>
        <p class="text-sm font-medium text-gray-600 dark:text-white/60">{{ currentSection.versionLabel }}</p>
      </div>

      <p
        class="max-w-sm select-none text-sm text-gray-600 dark:text-gray-300"
        @click="currentSection.developerModeEntry && handleDeveloperModeClick()"
      >
        将音乐给予你
      </p>

      <div class="flex flex-wrap items-center justify-center gap-x-2 gap-y-1 text-sm text-gray-500 dark:text-white/50">
        <span class="font-medium">开发者名单（排名不分先后）：</span>
        <a
          v-for="developer in currentSection.developers"
          :key="developer.url"
          :href="developer.url"
          target="_blank"
          rel="noreferrer"
          class="cursor-pointer no-underline font-medium text-inherit transition-colors hover:text-accent dark:hover:text-white/80"
        >{{ developer.label }}</a>
      </div>
    </div>

    <div class="flex max-w-full flex-wrap items-center justify-center gap-2.5">
      <button
        v-if="aboutConfig.updateEnabled"
        type="button"
        :disabled="!isConceptPage && isCheckingUpdate"
        @click="handleUpdateClick(currentSection.id)"
        class="flex shrink-0 items-center gap-2 whitespace-nowrap rounded-xl bg-accent px-5 py-2.5 text-sm font-medium text-white shadow-lg shadow-accent/20 transition active:scale-95 hover:bg-accent-hover disabled:cursor-not-allowed disabled:opacity-70"
      >
        <svg v-if="!isConceptPage && isCheckingUpdate" class="h-4 w-4 shrink-0 animate-spin" viewBox="0 0 24 24" fill="none">
          <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
          <path class="opacity-90" fill="currentColor" d="M4 12a8 8 0 0 1 8-8v4a4 4 0 0 0-4 4H4Z" />
        </svg>
        <svg v-else xmlns="http://www.w3.org/2000/svg" class="h-4 w-4 shrink-0" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M10 18a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm1-11a1 1 0 1 0-2 0v2H7a1 1 0 1 0 0 2h2v2a1 1 0 1 0 2 0v-2h2a1 1 0 1 0 0-2h-2V7Z" clip-rule="evenodd" /></svg>
        {{ !isConceptPage && isCheckingUpdate ? '检查中...' : aboutConfig.updateText }}
      </button>

      <button
        v-if="aboutConfig.officialSiteUrl"
        type="button"
        @click="handleAboutLinkClick(currentSection.id, aboutConfig.officialSiteUrl)"
        class="flex shrink-0 cursor-pointer items-center gap-2 whitespace-nowrap rounded-xl bg-accent px-5 py-2.5 text-sm font-medium text-white no-underline shadow-lg shadow-accent/20 transition active:scale-95 hover:bg-accent-hover"
      >
        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4 shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <circle cx="12" cy="12" r="10" />
          <path d="M2 12h20" />
          <path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10Z" />
        </svg>
        {{ aboutConfig.officialSiteText }}
      </button>

      <button
        v-if="currentProjectUrl"
        type="button"
        @click="handleProjectUrlClick"
        class="flex shrink-0 cursor-pointer items-center gap-2 whitespace-nowrap rounded-xl bg-white/20 backdrop-blur-md border border-gray-200/40 px-5 py-2.5 text-sm font-medium text-gray-800 no-underline transition active:scale-95 shadow-sm hover:bg-white/30 hover:border-accent/35 dark:bg-black/10 dark:border-gray-800/40 dark:text-white dark:hover:bg-white/10"
      >
        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4 shrink-0" viewBox="0 0 24 24" fill="currentColor"><path d="M12 0C5.373 0 0 5.373 0 12c0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23A11.49 11.49 0 0 1 12 5.797c1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576C20.566 21.8 24 17.302 24 12c0-6.627-5.373-12-12-12Z" /></svg>
        {{ aboutConfig.projectText }}
      </button>

      <button
        v-if="aboutConfig.referenceProjectUrl"
        type="button"
        @click="handleAboutLinkClick(currentSection.id, aboutConfig.referenceProjectUrl)"
        class="flex shrink-0 cursor-pointer items-center gap-2 whitespace-nowrap rounded-xl bg-white/20 backdrop-blur-md border border-gray-200/40 px-5 py-2.5 text-sm font-medium text-gray-800 no-underline transition active:scale-95 shadow-sm hover:bg-white/30 hover:border-accent/35 dark:bg-black/10 dark:border-gray-800/40 dark:text-white dark:hover:bg-white/10"
      >
        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4 shrink-0" viewBox="0 0 24 24" fill="currentColor"><path d="M12 0C5.373 0 0 5.373 0 12c0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23A11.49 11.49 0 0 1 12 5.797c1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576C20.566 21.8 24 17.302 24 12c0-6.627-5.373-12-12-12Z" /></svg>
        {{ aboutConfig.referenceProjectText }}
      </button>

      <button
        v-if="aboutConfig.joinGroupUrl"
        type="button"
        @click="handleAboutLinkClick(currentSection.id, aboutConfig.joinGroupUrl)"
        class="flex shrink-0 cursor-pointer items-center gap-2 whitespace-nowrap rounded-xl bg-white/20 backdrop-blur-md border border-gray-200/40 px-5 py-2.5 text-sm font-medium text-gray-800 transition active:scale-95 shadow-sm hover:bg-white/30 hover:border-accent/35 dark:bg-black/10 dark:border-gray-800/40 dark:text-white dark:hover:bg-white/10"
      >
        {{ aboutConfig.joinGroupText }}
      </button>
      </div>

      <div class="max-w-full shrink-0 text-center text-xs leading-relaxed text-gray-400 dark:text-white/40">
        Copyright © 2026 XY-Music-Desktop Developer. Licensed under AGPL-3.0-only.
      </div>
    </div>
    </Transition>
    </section>
  </div>
</template>

<style scoped>
.about-slide-enter-active,
.about-slide-leave-active {
  transition: opacity 0.25s ease, transform 0.25s ease;
}

.about-slide-enter-from {
  opacity: 0;
  transform: translateX(24px);
}

.about-slide-leave-to {
  opacity: 0;
  transform: translateX(-24px);
}
</style>
