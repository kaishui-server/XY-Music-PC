import { describe, expect, it } from 'vitest';

import { mergeHomeSettings } from './store';
import {
  DEFAULT_HOME_MODULE_ORDER,
  normalizeHomeModuleOrder,
} from './homeItems';
import type { HomeSettings } from '../../types';

const base: HomeSettings = {
  showNowPlaying: true,
  showHotComment: true,
  showStatistics: true,
  showLeaderboard: true,
  order: [...DEFAULT_HOME_MODULE_ORDER],
};

describe('home module settings', () => {
  it('normalizes duplicate, invalid and incomplete orders', () => {
    expect(normalizeHomeModuleOrder([
      'leaderboard',
      'leaderboard',
      'invalid' as any,
    ])).toEqual(['leaderboard', 'nowPlaying', 'hotComment', 'statistics']);
  });

  it('inserts the new hot comment module below now playing for legacy orders', () => {
    expect(normalizeHomeModuleOrder([
      'nowPlaying',
      'statistics',
      'leaderboard',
    ])).toEqual(['nowPlaying', 'hotComment', 'statistics', 'leaderboard']);
  });

  it('never allows all home modules to become hidden', () => {
    const merged = mergeHomeSettings(base, {
      showNowPlaying: false,
      showHotComment: false,
      showStatistics: false,
      showLeaderboard: false,
      order: ['statistics', 'leaderboard', 'nowPlaying', 'hotComment'],
    });

    expect(merged.showStatistics).toBe(true);
    expect(merged.showNowPlaying).toBe(false);
    expect(merged.showHotComment).toBe(false);
    expect(merged.showLeaderboard).toBe(false);
  });
});
