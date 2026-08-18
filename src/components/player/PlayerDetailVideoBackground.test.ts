import { describe, expect, it } from 'vitest';

import backgroundSource from './PlayerDetailBackground.vue?raw';
import detailSource from './PlayerDetail.vue?raw';
import menuSource from '../overlays/PlayerDetailContextMenu.vue?raw';
import engineSource from '../../services/pluginEngine.ts?raw';
import workerSource from '../../services/pluginSandbox.worker.ts?raw';

describe('player-detail Bilibili video background wiring', () => {
  it('shows a reversible Bilibili-only context-menu action', () => {
    expect(menuSource).toContain("isBilibiliPluginSong(props.song)");
    expect(menuSource).toContain("'播放视频为背景'");
    expect(menuSource).toContain("'关闭背景视频'");
    expect(menuSource).toContain("emit('toggle-video-background')");
    expect(detailSource).toContain('@toggle-video-background="toggleVideoBackground"');
  });

  it('renders a muted video and follows playback time and pause state', () => {
    expect(backgroundSource).toContain('<video');
    expect(backgroundSource).toContain('muted');
    expect(backgroundSource).toContain('class="h-full w-full object-cover select-none"');
    expect(backgroundSource).not.toContain('scale-105 object-cover');
    expect(backgroundSource).toContain('Math.abs(video.currentTime - target) > 0.8');
    expect(backgroundSource).toContain('watch([isPlaying, () => props.active, showBackgroundVideo]');
  });

  it('clears background blur after video startup and explains where to restore it', () => {
    expect(detailSource).toContain('lyricsSettings.backgroundBlur = 0');
    expect(detailSource).toContain('模糊度已调整为 0%');
    expect(detailSource).toContain('底栏“页面样式”→“背景样式”');
  });

  it('allows getMvSource through both plugin proxy boundaries', () => {
    expect(engineSource).toContain("'search', 'getMediaSource', 'getMvSource'");
    expect(workerSource).toContain("'search', 'getMediaSource', 'getMvSource'");
  });
});
