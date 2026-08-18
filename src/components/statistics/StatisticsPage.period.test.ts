import { describe, expect, it } from 'vitest';

import source from './StatisticsPage.vue?raw';
import serviceSource from '../../services/leaderboardService.ts?raw';

describe('StatisticsPage leaderboard periods', () => {
  it('supports daily, weekly and total rankings without removing concept home modules', () => {
    expect(source).toContain("{ value: 'daily', label: t('home.daily') }");
    expect(source).toContain("{ value: 'weekly', label: t('home.weekly') }");
    expect(source).toContain("{ value: 'total', label: t('home.total') }");
    expect(source).toContain('HomeNowPlaying');
    expect(source).toContain('HomeHotComment');
    expect(serviceSource).toContain("export type LeaderboardPeriod = 'daily' | 'weekly' | 'total'");
  });
});
