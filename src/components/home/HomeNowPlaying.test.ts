import { describe, expect, it } from 'vitest';

import source from './HomeNowPlaying.vue?raw';
import statisticsSource from '../statistics/StatisticsPage.vue?raw';

describe('HomeNowPlaying', () => {
  it('shows the requested song information and playback controls', () => {
    expect(source).toContain('{{ songTitle }}');
    expect(source).toContain('{{ songArtist }}');
    expect(source).toContain('{{ sourceLabel }}');
    expect(source).toContain('{{ lyricText }}');
    expect(source).toContain('{{ lyricTranslation }}');
    expect(source).toContain('line.translation');
    expect(source).toContain("line.secondary ?? []");
    expect(source).toContain('@click="togglePlay"');
    expect(source).toContain('@click="nextSong"');
    expect(source).toContain('@pointerdown="startProgressDrag"');
    expect(source).toContain("window.addEventListener('pointermove', onGlobalPointerMove)");
    expect(source).toContain("window.addEventListener('pointerup', onGlobalPointerUp)");
    expect(source).toContain('isDraggingProgress.value ? dragTime.value : currentTime.value');
    expect(source).toContain('void seekTo(targetTime)');
    expect(source).toContain('role="slider"');
    expect(source).toContain('@keydown="handleProgressKeydown"');
  });

  it('uses a frameless layout and leaves enough line height for title descenders', () => {
    expect(source).not.toContain('border: 1px solid rgb(var(--theme-accent-rgb)');
    expect(source).not.toContain('.home-now-playing::after');
    expect(source).not.toContain('box-shadow: 0 22px 70px');
    expect(source).toContain('min-h-[1.28em]');
    expect(source).toContain('leading-[1.16]');
  });

  it('scrolls long song titles and rechecks overflow when the layout changes', () => {
    expect(source).toContain('textWidth > viewportWidth + 1');
    expect(source).toContain('new ResizeObserver(checkTitleOverflow)');
    expect(source).toContain("watch(songTitle, checkTitleOverflow)");
    expect(source).toContain("window.addEventListener('resize', checkTitleOverflow)");
    expect(source).toContain("'home-title-track--scrolling': shouldScrollTitle");
    expect(source).toContain('@keyframes home-title-marquee');
    expect(source).toContain('v-if="shouldScrollTitle" aria-hidden="true"');
  });

  it('keeps the section label independent from the accent theme', () => {
    expect(source).toContain("tracking-[0.22em] text-gray-900 dark:text-white\">{{ t('home.nowPlaying') }}");
    expect(source).not.toContain("tracking-[0.22em] text-accent\">{{ t('home.nowPlaying') }}");
  });

  it('is rendered according to the persisted home module order', () => {
    expect(statisticsSource).toContain('v-for="moduleKey in visibleHomeModules"');
    expect(statisticsSource).toContain("moduleKey === 'nowPlaying'");
    expect(statisticsSource).toContain("moduleKey === 'hotComment'");
    expect(statisticsSource).toContain("moduleKey === 'statistics'");
    expect(statisticsSource).toContain("moduleKey === 'leaderboard'");
  });
});
