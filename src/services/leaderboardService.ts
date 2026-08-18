/**
 * 听歌排行榜服务
 *
 * 调用后端 `api/index.php` 的 `get_leaderboard` 接口，
 * 获取听歌时长排行榜数据（Top N + 当前用户排名）。
 *
 * 在获取排行榜前，先将本地统计的听歌时长上报到后端（report_listen_stats），
 * 确保云端数据与本地一致。
 *
 * 复用 authService 的签名机制（MD5）。
 */

import { signedRequest } from './auth/authService';
import { getCiyuanxiId } from './playlistSync';
import { statisticsApi } from './tauri/statisticsApi';

/** 日志前缀 */
const LOG = '[Leaderboard]';
const RESET_AT_KEY = 'listen_stats_last_reset_at';

/** 排行榜样条目 */
export interface LeaderboardEntry {
  rank: number;
  username: string;
  nickname: string;
  avatar?: string;
  /** 听歌时长（秒） */
  duration: number;
  /** 是否为当前登录用户 */
  isMe?: boolean;
}

/** 排行榜 API 响应 */
export interface LeaderboardData {
  /** Top N 排行列表 */
  leaderboard: LeaderboardEntry[];
  /** 当前用户的排名信息（可能不在 Top N 中） */
  me: LeaderboardEntry | null;
  /** 参与排行的总用户数 */
  totalUsers: number;
}

/**
 * 上报本地听歌时长到后端（report_listen_stats）
 * 后端采用「最大值覆盖」策略，只增不减。
 *
 * @param listenDuration 本地累计听歌时长（秒）
 * @param uniqueSongsCount 本地累计聆听新歌数（可选）
 */
async function reportListenDuration(
  listenDuration: number,
  uniqueSongsCount = 0,
): Promise<{ reset_at?: string } | null> {
  const ciyuanxiId = getCiyuanxiId();
  if (!ciyuanxiId) return null;

  try {
    const data = await signedRequest<{ reset_at?: string }>('report_listen_stats', {
      ciyuanxi_id: ciyuanxiId,
      duration: Math.floor(listenDuration),
      unique_songs_count: uniqueSongsCount,
    }, {
      fetchTimeoutMs: 8_000,
      timeoutMs: 10_000,
    });
    console.log(`${LOG} 上报听歌时长成功: ${Math.floor(listenDuration)}秒`);
    return data ?? null;
  } catch (e) {
    // 上报失败不阻断排行榜获取
    const msg = e instanceof Error ? e.message : String(e);
    console.warn(`${LOG} 上报听歌时长失败（不影响排行榜获取）: ${msg}`);
    return null;
  }
}

async function handleResetSignal(resetAt: string): Promise<void> {
  try {
    await statisticsApi.resetLocalStatistics();
    localStorage.setItem(RESET_AT_KEY, resetAt);
    console.log(`${LOG} 已处理服务端统计重置信号: ${resetAt}`);
  } catch (error) {
    console.error(`${LOG} 重置本地统计数据失败:`, error);
  }
}

async function reportAndHandleReset(localDuration: number): Promise<boolean> {
  const result = await reportListenDuration(localDuration);
  if (!result?.reset_at) return false;
  const lastResetAt = localStorage.getItem(RESET_AT_KEY);
  if (lastResetAt && result.reset_at <= lastResetAt) return false;
  await handleResetSignal(result.reset_at);
  await reportListenDuration(0, 0);
  return true;
}

export async function checkForResetSignal(localDuration: number): Promise<boolean> {
  return getCiyuanxiId() ? reportAndHandleReset(localDuration) : false;
}

/**
 * 获取听歌排行榜
 *
 * 会先上报本地听歌时长到后端，再获取排行榜数据。
 *
 * @param limit 返回的排行数量，默认 50
 * @param localDuration 本地统计的听歌时长（秒），上报到后端用于排行榜
 * @param period 排行榜时间周期：daily（日榜）、weekly（周榜）、total（总榜）
 */
export type LeaderboardPeriod = 'daily' | 'weekly' | 'total';

export async function fetchLeaderboard(
  limit = 50,
  localDuration?: number,
  period: LeaderboardPeriod = 'total',
): Promise<LeaderboardData & { resetApplied?: boolean }> {
  const ciyuanxiId = getCiyuanxiId();
  let resetApplied = false;

  // 只有登录用户才上报个人听歌时长；公共排行榜无需登录即可获取。
  if (ciyuanxiId) resetApplied = await reportAndHandleReset(localDuration ?? 0);

  try {
    const data = await signedRequest<{
      leaderboard: Array<{
        rank: number;
        username: string;
        nickname: string;
        avatar: string;
        duration: number;
        is_me: boolean;
      }>;
      me: {
        rank: number;
        username: string;
        nickname: string;
        avatar: string;
        duration: number;
        is_me: boolean;
      } | null;
      total_users: number;
      period?: string;
    }>('get_leaderboard', {
      ...(ciyuanxiId ? { ciyuanxi_id: ciyuanxiId } : {}),
      limit,
      period,
    }, {
      fetchTimeoutMs: 12_000,
      timeoutMs: 15_000,
    });

    // 映射后端 snake_case → 前端 camelCase
    const leaderboard: LeaderboardEntry[] = (data.leaderboard ?? []).map(item => ({
      rank: item.rank,
      username: item.username,
      nickname: item.nickname || item.username,
      avatar: item.avatar || undefined,
      duration: item.duration,
      isMe: Boolean(ciyuanxiId && item.is_me),
    }));

    let me: LeaderboardEntry | null = null;
    if (ciyuanxiId && data.me) {
      me = {
        rank: data.me.rank,
        username: data.me.username,
        nickname: data.me.nickname || data.me.username,
        avatar: data.me.avatar || undefined,
        duration: data.me.duration,
        isMe: true,
      };
    }

    return {
      leaderboard,
      me,
      totalUsers: data.total_users ?? leaderboard.length,
      resetApplied,
    };
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e);
    console.error(`${LOG} 获取排行榜失败:`, msg);
    throw e;
  }
}
