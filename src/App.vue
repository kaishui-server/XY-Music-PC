<script setup lang="ts">
import { getCurrentWindow } from '@tauri-apps/api/window';
import { getVersion } from '@tauri-apps/api/app';
import { isTauri } from '@tauri-apps/api/core';
import { appApi } from './services/tauri/appApi';
import { defineAsyncComponent, nextTick, ref, watch, onBeforeUnmount, onMounted } from 'vue';
import { storeToRefs } from 'pinia';

import { registerImportedLyricsFonts } from './composables/lyrics';
import { useToast } from './composables/toast';
import { DESKTOP_LYRICS_WINDOW_LABEL } from './features/desktopLyrics/shared';
import { MINI_PLAYER_WINDOW_LABEL, VOLUME_POPOVER_WINDOW_LABEL } from './features/miniPlayer/shared';
import { TASKBAR_PLAYER_WINDOW_LABEL } from './features/taskbarPlayer/shared';
import { useSettings } from './features/settings/useSettings';
import { TRAY_MENU_WINDOW_LABEL } from './features/tray/actions';
import { loadPlugins, checkAllPluginUpdates, performPluginUpdate, getStoredPlugins } from './services/pluginEngine';
import { configureApplicationLogger } from './services/applicationLogger';
import { reportAppOpen } from './services/usageStats';
import { useUiStore } from './shared/stores/ui';
import { clearHeavyImageCaches } from './caches/imageCaches';
import { clearPaletteCache } from './composables/colorExtraction';
import { clearPreblurredBackgroundCache } from './composables/preblurredBackgroundCache';
import { useCoverCache } from './composables/useCoverCache';
import { setMainWindowRenderingSnapshot } from './composables/renderingPower';
import { applyAccentTheme } from './composables/accentTheme';
import { useButtonHoverDetails } from './composables/useButtonHoverDetails';
import { usePlaybackSleepPrevention } from './composables/usePlaybackSleepPrevention';
import { usePlaybackStore } from './features/playback/store';
import { useAppLanguage } from './i18n';
import { useDomLocalization } from './i18n/domLocalization';

const currentWindowLabel = (() => {
  try {
    return getCurrentWindow().label;
  } catch {
    return 'main';
  }
})();
const runningInTauri = isTauri();

const isDesktopLyricsWindow = currentWindowLabel === DESKTOP_LYRICS_WINDOW_LABEL;
const isMiniPlayerWindow = currentWindowLabel === MINI_PLAYER_WINDOW_LABEL;
const isTrayMenuWindow = currentWindowLabel === TRAY_MENU_WINDOW_LABEL;
const isTaskbarPlayerWindow = currentWindowLabel === TASKBAR_PLAYER_WINDOW_LABEL;
const isVolumePopoverWindow = currentWindowLabel === VOLUME_POPOVER_WINDOW_LABEL;
const isMainShellSleeping = ref(false);
const MainShell = defineAsyncComponent(() => import('./components/layout/MainShell.vue'));
const MiniPlayerWindow = defineAsyncComponent(() => import('./components/layout/MiniPlayerWindow.vue'));
const DesktopLyricsWindow = defineAsyncComponent(() => import('./components/player/DesktopLyricsWindow.vue'));
const TrayMenuWindow = defineAsyncComponent(() => import('./components/layout/TrayMenuWindow.vue'));
const TaskbarControlWindow = defineAsyncComponent(() => import('./components/layout/TaskbarControlWindow.vue'));
const VolumePopoverWindow = defineAsyncComponent(() => import('./components/layout/VolumePopoverWindow.vue'));

const { settings } = useSettings();
const { language } = useAppLanguage();
useDomLocalization(language);
useButtonHoverDetails(() => settings.value.showButtonHoverDetails);
let accentThemeObserver: MutationObserver | null = null;
watch(
  () => [settings.value.theme.accentTheme, settings.value.theme.customAccentColor] as const,
  ([accentTheme, customAccentColor]) => applyAccentTheme(accentTheme, customAccentColor),
  { immediate: true },
);
watch(
  () => ({ ...settings.value.logging }),
  logging => configureApplicationLogger(logging),
  { immediate: true },
);
watch(
  () => settings.value.customLyricsFonts,
  (fonts) => registerImportedLyricsFonts(fonts),
  { deep: true, immediate: true },
);

// 沉浸全屏时给 body 添加 class，CSS 全局禁用所有 data-tauri-drag-region 的指针事件，
// 防止全屏窗口被拖动（主页 TitleBar/SidebarBrand、歌词页顶栏等）。
const uiStore = useUiStore();
const { isImmersiveFullscreen, mainWindowUiSleepRequested } = storeToRefs(uiStore);
watch(isImmersiveFullscreen, (fs) => {
  document.body.classList.toggle('immersive-fullscreen', fs);
}, { immediate: true });

onMounted(() => {
  // Windows 任务栏可能继续使用旧版本 EXE 的缓存图标；在每个 Tauri 窗口
  // 创建后显式设置当前发布资源，确保主窗口和任务栏播控窗口都使用新 logo。
  if (runningInTauri) {
    try {
      void getCurrentWindow().setIcon('/logo.png').catch((error) => {
        console.warn('[App] 设置窗口图标失败:', error);
      });
    } catch (error) {
      console.warn('[App] 获取当前窗口失败:', error);
    }
  }
  accentThemeObserver = new MutationObserver(() => {
    applyAccentTheme(
      settings.value.theme.accentTheme,
      settings.value.theme.customAccentColor,
    );
  });
  accentThemeObserver.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ['class'],
  });
});

onBeforeUnmount(() => {
  accentThemeObserver?.disconnect();
  accentThemeObserver = null;
});

if (currentWindowLabel === 'main') {
  const { showToast } = useToast();
  const { clearCoverCaches } = useCoverCache();
  const playbackStore = usePlaybackStore();
  usePlaybackSleepPrevention(() => (
    settings.value.preventComputerSleepWhilePlaying && playbackStore.isPlaying
  ));
  let handleDevtoolsKeyDown: ((event: KeyboardEvent) => void) | null = null;
  let unlistenCloseRequested: (() => void) | null = null;
  let unlistenFocusChanged: (() => void) | null = null;
  let isUnmounted = false;

  const disposeTauriListener = (
    unlisten: (() => void) | null,
    label: string,
  ) => {
    if (!unlisten) return;
    // Tauri 的 UnlistenFn 类型标注为 void，但运行时会返回 Promise。
    // HMR 或窗口销毁时监听可能已由后端移除，必须同时捕获同步和异步错误，
    // 否则 unhandledrejection 会触发全局致命错误页并清空主内容区。
    void Promise.resolve()
      .then(() => unlisten())
      .catch((error) => {
        console.warn(`[App] 清理 ${label} 监听失败:`, error);
      });
  };

  const releaseHiddenMainWindowResources = () => {
    clearPreblurredBackgroundCache();
    clearCoverCaches();
    clearHeavyImageCaches();
    clearPaletteCache();
  };

  const enterTraySleep = async () => {
    mainWindowUiSleepRequested.value = true;
    await enterMainWindowSleep();
  };

  const enterMainWindowSleep = async () => {
    if (isMainShellSleeping.value) return;

    isMainShellSleeping.value = true;
    setMainWindowRenderingSnapshot({
      documentHidden: true,
      windowFocused: false,
      windowVisible: false,
      windowMinimized: false,
    });
    await nextTick();
    releaseHiddenMainWindowResources();
  };

  const leaveTraySleep = () => {
    mainWindowUiSleepRequested.value = false;
  };

  const leaveMainWindowSleep = () => {
    if (!isMainShellSleeping.value) return;
    isMainShellSleeping.value = false;
    setMainWindowRenderingSnapshot({
      documentHidden: document.hidden,
      windowFocused: true,
      windowVisible: true,
      windowMinimized: false,
    });
  };

  const handleDocumentVisibilityChange = () => {
    if (!document.hidden) {
      leaveTraySleep();
    }
  };

  watch(mainWindowUiSleepRequested, (sleepRequested) => {
    if (sleepRequested) {
      void enterMainWindowSleep();
    } else {
      leaveMainWindowSleep();
    }
  });

  onMounted(async () => {
    // 上报软件打开事件（fire-and-forget，失败静默），用于后台"软件打开次数/设备连接数"统计
    reportAppOpen();

    try {
      const version = await getVersion();
      showToast(`欢迎使用XY Music，当前版本 v${version}`, 'info');
    } catch (error) {
      console.error('Failed to get version for welcome toast:', error);
    }

    if (runningInTauri) {
      try {
        const closeRequestedUnlisten = await getCurrentWindow().onCloseRequested(async (event) => {
          if (settings.value.closeToTray) {
            event.preventDefault();
            await enterTraySleep();
            await getCurrentWindow().hide();
          }
        });
        if (isUnmounted) {
          disposeTauriListener(closeRequestedUnlisten, 'close-requested');
        } else {
          unlistenCloseRequested = closeRequestedUnlisten;
        }
      } catch (error) {
        console.warn('[App] 注册 close-requested 监听失败:', error);
      }

      try {
        const focusChangedUnlisten = await getCurrentWindow().onFocusChanged(({ payload }) => {
          if (payload) {
            leaveTraySleep();
          }
        });
        if (isUnmounted) {
          disposeTauriListener(focusChangedUnlisten, 'focus-changed');
        } else {
          unlistenFocusChanged = focusChangedUnlisten;
        }
      } catch (error) {
        console.warn('[App] 注册 focus-changed 监听失败:', error);
      }
    }

    // 启动时加载插件（尊重懒加载设置）
    const pluginConfig = settings.value.plugins;
    void loadPlugins(pluginConfig.lazyLoad).then(async () => {
      // 启动时自动检查并更新插件
      if (!pluginConfig.autoUpdateOnStartup) return;
      try {
        const results = await checkAllPluginUpdates();
        let updated = 0;
        for (const [id, result] of results) {
          if (result.hasUpdate && result.newScript) {
            const plugin = getStoredPlugins().find(p => p.id === id);
            if (plugin) {
              const updateResult = await performPluginUpdate(plugin, result);
              if (updateResult.success) updated++;
            }
          }
        }
        if (updated > 0) {
          showToast(`已自动更新 ${updated} 个插件`, 'success');
        }
      } catch (error) {
        console.error('[AutoUpdate] 插件自动更新失败:', error);
      }
    });

    // F12 打开 DevTools（WebView2 已禁用浏览器快捷键，需通过自定义命令恢复）
    // 生产构建中 open_devtools 命令返回错误，invoke 被 catch 静默处理
    handleDevtoolsKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'F12') {
        event.preventDefault();
        void appApi.openDevtools().catch(() => { /* 生产环境无 DevTools */ });
      }
    };
    window.addEventListener('keydown', handleDevtoolsKeyDown);
    window.addEventListener('focus', leaveTraySleep);
    document.addEventListener('visibilitychange', handleDocumentVisibilityChange);
  });

  onBeforeUnmount(() => {
    isUnmounted = true;

    if (handleDevtoolsKeyDown) {
      window.removeEventListener('keydown', handleDevtoolsKeyDown);
      handleDevtoolsKeyDown = null;
    }

    disposeTauriListener(unlistenCloseRequested, 'close-requested');
    unlistenCloseRequested = null;
    disposeTauriListener(unlistenFocusChanged, 'focus-changed');
    unlistenFocusChanged = null;
    window.removeEventListener('focus', leaveTraySleep);
    document.removeEventListener('visibilitychange', handleDocumentVisibilityChange);
  });
}
</script>

<template>
  <DesktopLyricsWindow v-if="isDesktopLyricsWindow" />
  <MiniPlayerWindow v-else-if="isMiniPlayerWindow" />
  <TrayMenuWindow v-else-if="isTrayMenuWindow" />
  <TaskbarControlWindow v-else-if="isTaskbarPlayerWindow" />
  <VolumePopoverWindow v-else-if="isVolumePopoverWindow" />
  <MainShell v-else :sleep="isMainShellSleeping" />
</template>

<style>
html,
body,
#app {
  -webkit-user-select: none;
  user-select: none;
}

input,
textarea,
[contenteditable="true"] {
  -webkit-user-select: text;
  user-select: text;
}

/* 沉浸全屏时禁用所有拖动区域，防止窗口被拖动。
   仅禁用 drag-region 元素自身的指针事件以阻止 Tauri 原生拖动，
   子元素（按钮、输入框等）恢复 pointer-events: auto 保持可交互。 */
body.immersive-fullscreen [data-tauri-drag-region] {
  pointer-events: none !important;
}
body.immersive-fullscreen [data-tauri-drag-region] * {
  pointer-events: auto;
}

/* 沉浸全屏切换动画：主页容器与歌词页同步播放 scale 动画，
   盖住原生 maximize→SetWindowPos 的尺寸跳变。全局样式供 MainShell 与 PlayerDetail 共用。 */
.fs-entering {
  animation: fs-enter 320ms cubic-bezier(0.22, 1, 0.36, 1);
  transform-origin: center center;
}

.fs-exiting {
  animation: fs-exit 320ms cubic-bezier(0.22, 1, 0.36, 1);
  transform-origin: center center;
}

@keyframes fs-enter {
  0% {
    transform: scale(0.94);
    opacity: 0.82;
  }
  100% {
    transform: scale(1);
    opacity: 1;
  }
}

@keyframes fs-exit {
  0% {
    transform: scale(1);
    opacity: 1;
  }
  100% {
    transform: scale(0.94);
    opacity: 0.82;
  }
}
</style>
