import { beforeEach, describe, expect, it, vi } from 'vitest';

const { signedRequestMock, getCiyuanxiIdMock } = vi.hoisted(() => ({
  signedRequestMock: vi.fn(),
  getCiyuanxiIdMock: vi.fn(),
}));

vi.mock('./auth/authService', () => ({ signedRequest: signedRequestMock }));
vi.mock('./playlistSync', () => ({ getCiyuanxiId: getCiyuanxiIdMock }));

import { fetchLeaderboard } from './leaderboardService';

describe('fetchLeaderboard', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    signedRequestMock.mockResolvedValue({
      leaderboard: [{
        rank: 1,
        username: 'listener',
        nickname: 'Listener',
        avatar: '',
        duration: 3600,
        is_me: false,
      }],
      me: null,
      total_users: 1,
    });
  });

  it('loads the public ranking without a logged-in user', async () => {
    getCiyuanxiIdMock.mockReturnValue(null);

    const result = await fetchLeaderboard(50, 600);

    expect(signedRequestMock).toHaveBeenCalledTimes(1);
    expect(signedRequestMock).toHaveBeenCalledWith(
      'get_leaderboard',
      { limit: 50, period: 'total' },
      expect.any(Object),
    );
    expect(result.leaderboard).toHaveLength(1);
    expect(result.me).toBeNull();
  });

  it('reports period totals instead of letting the server infer daily increments', async () => {
    getCiyuanxiIdMock.mockReturnValue('xy-user-1');

    await fetchLeaderboard(15, {
      daily: 1_200,
      weekly: 8_400,
      total: 36_000,
    }, 'daily');

    expect(signedRequestMock).toHaveBeenNthCalledWith(
      1,
      'report_listen_stats',
      {
        xymusic_id: 'xy-user-1',
        duration: 36_000,
        daily_duration: 1_200,
        weekly_duration: 8_400,
        unique_songs_count: 0,
      },
      expect.any(Object),
    );
    expect(signedRequestMock).toHaveBeenNthCalledWith(
      2,
      'get_leaderboard',
      { xymusic_id: 'xy-user-1', limit: 15, period: 'daily' },
      expect.any(Object),
    );
  });

  it('shares one listen-stat report across concurrent period requests', async () => {
    getCiyuanxiIdMock.mockReturnValue('xy-user-1');
    const reportStarted = new Promise<void>(resolve => {
      setTimeout(resolve, 0);
    });
    signedRequestMock.mockImplementation(async (action: string) => {
      if (action === 'report_listen_stats') await reportStarted;
      return {
        leaderboard: [],
        me: null,
        total_users: 0,
      };
    });

    await Promise.all([
      fetchLeaderboard(15, { daily: 10, weekly: 10, total: 10 }, 'daily'),
      fetchLeaderboard(15, { daily: 10, weekly: 10, total: 10 }, 'weekly'),
    ]);

    expect(signedRequestMock.mock.calls.filter(([action]) => action === 'report_listen_stats')).toHaveLength(1);
  });
});
