import type { HomeModuleKey, HomeSettings } from '../../types';

export const DEFAULT_HOME_MODULE_ORDER: HomeModuleKey[] = [
  'nowPlaying',
  'hotComment',
  'statistics',
  'leaderboard',
];

export const HOME_MODULE_ITEMS: Array<{
  key: HomeModuleKey;
  label: string;
  description: string;
  visibilityKey: keyof Pick<HomeSettings, 'showNowPlaying' | 'showHotComment' | 'showStatistics' | 'showLeaderboard'>;
}> = [
  {
    key: 'nowPlaying',
    label: '正在播放的歌曲',
    description: '显示歌曲、歌词、进度和播放控制',
    visibilityKey: 'showNowPlaying',
  },
  {
    key: 'hotComment',
    label: '热评推荐',
    description: '随机显示网易云热评，点击可搜索歌曲',
    visibilityKey: 'showHotComment',
  },
  {
    key: 'statistics',
    label: '数据统计',
    description: '显示音乐库和听歌数据摘要',
    visibilityKey: 'showStatistics',
  },
  {
    key: 'leaderboard',
    label: '听歌排行榜',
    description: '显示单日听歌时长排行',
    visibilityKey: 'showLeaderboard',
  },
];

export const normalizeHomeModuleOrder = (
  order: readonly HomeModuleKey[] | null | undefined,
): HomeModuleKey[] => {
  const valid = new Set<HomeModuleKey>(DEFAULT_HOME_MODULE_ORDER);
  const normalized: HomeModuleKey[] = [];

  for (const key of order ?? []) {
    if (valid.has(key) && !normalized.includes(key)) {
      normalized.push(key);
    }
  }

  // 兼容旧配置：首次加入热评模块时，默认插入“正在播放”下方。
  if (normalized.length > 0 && !normalized.includes('hotComment')) {
    const nowPlayingIndex = normalized.indexOf('nowPlaying');
    if (nowPlayingIndex >= 0) {
      normalized.splice(nowPlayingIndex + 1, 0, 'hotComment');
    }
  }

  for (const key of DEFAULT_HOME_MODULE_ORDER) {
    if (!normalized.includes(key)) normalized.push(key);
  }

  return normalized;
};

export const getHomeModuleVisibilityKey = (key: HomeModuleKey) => (
  HOME_MODULE_ITEMS.find(item => item.key === key)?.visibilityKey ?? 'showNowPlaying'
);
