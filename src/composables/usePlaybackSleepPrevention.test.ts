import { effectScope, nextTick, ref } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { setSleepPrevention } = vi.hoisted(() => ({
  setSleepPrevention: vi.fn(() => Promise.resolve()),
}));

vi.mock('../services/tauri/playbackApi', () => ({
  playbackApi: { setSleepPrevention },
}));

import { usePlaybackSleepPrevention } from './usePlaybackSleepPrevention';

const flushSyncQueue = async () => {
  await nextTick();
  await Promise.resolve();
  await Promise.resolve();
};

describe('usePlaybackSleepPrevention', () => {
  beforeEach(() => {
    setSleepPrevention.mockClear();
  });

  it('仅在播放且设置开启时防休眠，并在作用域释放时恢复', async () => {
    const playing = ref(false);
    const enabled = ref(true);
    const scope = effectScope();

    scope.run(() => {
      usePlaybackSleepPrevention(() => playing.value && enabled.value);
    });
    await flushSyncQueue();
    expect(setSleepPrevention).toHaveBeenLastCalledWith(false);

    playing.value = true;
    await flushSyncQueue();
    expect(setSleepPrevention).toHaveBeenLastCalledWith(true);

    enabled.value = false;
    await flushSyncQueue();
    expect(setSleepPrevention).toHaveBeenLastCalledWith(false);

    enabled.value = true;
    await flushSyncQueue();
    scope.stop();
    await flushSyncQueue();
    expect(setSleepPrevention).toHaveBeenLastCalledWith(false);
  });
});
