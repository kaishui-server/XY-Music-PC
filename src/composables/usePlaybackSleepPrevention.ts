import { onScopeDispose, watch } from 'vue';

import { playbackApi } from '../services/tauri/playbackApi';

/** 播放状态快速切换时，串行同步系统防休眠状态，确保最后一次选择生效。 */
export function usePlaybackSleepPrevention(shouldPreventSleep: () => boolean) {
  let syncQueue = Promise.resolve();
  let lastRequestedState: boolean | null = null;

  const requestState = (enabled: boolean) => {
    if (lastRequestedState === enabled) return;
    lastRequestedState = enabled;
    syncQueue = syncQueue
      .catch(() => undefined)
      .then(() => playbackApi.setSleepPrevention(enabled))
      .catch((error) => {
        console.warn('[Playback] 同步系统防休眠状态失败:', error);
      });
  };

  const stop = watch(shouldPreventSleep, requestState, { immediate: true });

  onScopeDispose(() => {
    stop();
    requestState(false);
  });
}
