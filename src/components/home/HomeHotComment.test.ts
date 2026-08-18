import { describe, expect, it } from 'vitest';

import source from './HomeHotComment.vue?raw';
import statisticsSource from '../statistics/StatisticsPage.vue?raw';

describe('HomeHotComment', () => {
  it('uses persistent recommendation state and lets the user refresh it', () => {
    expect(source).toContain('useHotCommentRecommendation()');
    expect(source).toContain('ensureHotCommentRecommendation()');
    expect(source).toContain('@click="refreshHotComment"');
    expect(source).toContain("t('home.anotherComment')");
    expect(source).not.toContain('fetchHotComment()');
    expect(source).not.toContain('onUnmounted');
    expect(source).toContain('text-base font-semibold tracking-[0.22em] text-gray-900 dark:text-white');
    expect(source).not.toContain('tracking-[0.22em] text-accent');
    expect(source).not.toContain('<Quote');
  });

  it('searches the song from the clicked comment', () => {
    expect(source).toContain('navigationStore.setSearch(songTitle)');
    expect(source).toContain('navigationStore.addSearchHistory(songTitle)');
    expect(source).toContain("router.push('/search')");
    expect(source).toContain('@click="searchSong"');
  });

  it('is rendered as a sortable home module', () => {
    expect(statisticsSource).toContain("moduleKey === 'hotComment'");
    expect(statisticsSource).toContain('<HomeHotComment');
  });
});
