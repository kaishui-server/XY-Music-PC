import { computed, type ComputedRef } from 'vue';

import { useSettings } from '../features/settings/useSettings';
import type { AppLanguage } from '../types';

export const APP_LANGUAGE_OPTIONS: ReadonlyArray<{
  value: AppLanguage;
  label: string;
  shortLabel: string;
}> = [
  { value: 'system', label: '跟随系统', shortLabel: 'SYS' },
  { value: 'zh-CN', label: '简体中文', shortLabel: '中' },
  { value: 'en-US', label: 'English', shortLabel: 'EN' },
];

const zhCN = {
  'language.section': '语言',
  'language.title': '软件语言',
  'language.description': '选择整个软件界面使用的语言。',
  'settings.tab.account': '账号',
  'settings.tab.general': '常规',
  'settings.tab.details': '细节',
  'settings.tab.plugins': '插件',
  'settings.tab.appearance': '外观',
  'settings.tab.playback': '播放',
  'settings.tab.download': '下载',
  'settings.tab.library': '音乐库',
  'settings.tab.toolbox': '工具箱',
  'settings.tab.desktopLyrics': '桌面歌词',
  'settings.tab.shortcuts': '快捷键',
  'settings.tab.advanced': '高级设置',
  'settings.tab.debug': '调试',
  'settings.tab.about': '关于',
  'settings.search.placeholder': '搜索设置',
  'settings.search.clear': '清除设置搜索',
  'settings.search.found': '找到 {count} 项设置',
  'settings.search.empty': '没有找到相关设置',
  'settings.search.suggestion': '试试搜索“音质”“歌词”或“缓存”',
  'settings.sidebar.resize': '按住拖拽调整侧边栏宽度，双击恢复默认',
  'settings.fallback.title': '施工中',
  'settings.fallback.description': '当前设置模块正在整理中。',
  'general.section': '常规与启动',
  'general.launchOnStartup': '开机自动运行',
  'general.checkUpdates': '启动检测更新',
  'general.gpuAcceleration': 'GPU 加速',
  'general.closeToTray': '关闭时最小化至托盘',
  'general.qualityBadges': '显示音质标识',
  'general.songComments': '显示歌曲注释',
  'general.scrollToTop': '打开一键回顶按钮',
  'general.taskbarControls': '启用任务栏快捷播控',
  'general.writeArtistAvatar': '修改歌手头像时同步写回音频标签',
  'general.writeArtistAvatarHint': '开启后，手动修改歌手头像时会同步修改本地音频文件（注意：多歌手合作歌曲、远程歌曲、CUE分轨、只读文件会被自动跳过）',
  'general.songClickAction': '歌曲播放触发方式',
  'general.singleClick': '单击',
  'general.doubleClick': '双击',
  'general.storage': '存储空间',
  'general.streamCacheLimit': '播放缓存上限',
  'general.streamCacheHint': '在线歌曲流式下载后缓存到本地，再次播放无需重新下载。缓存满后自动清理最久未播放的曲目。',
  'general.clearStreamCache': '清理在线播放缓存',
  'general.clearStreamCacheHint': '清理后正在播放的在线歌曲不受影响，但已缓存的其他曲目需重新下载。',
  'general.clearing': '清理中...',
  'general.clear': '清理',
  'general.resetData': '重置数据',
  'general.resetting': '重置中...',
  'general.unavailableScanning': '扫描中不可用',
  'general.reset': '重置',
  'general.resetConfirm': '此操作会清空媒体库、播放记录、收藏和设置，并恢复初始状态，但不会删除你的音乐文件。确定继续吗？',
  'general.gpuUpdated': 'GPU 加速设置已更新，重启软件后生效',
  'general.gpuFailed': 'GPU 加速设置保存失败',
  'general.cacheCleared': '在线播放缓存已清理',
  'general.cacheClearFailed': '清理在线播放缓存失败',
  'general.resetFailed': '清除所有数据失败，请重试',
  'sidebar.home': '首页',
  'sidebar.localMusic': '本地音乐',
  'sidebar.explore': '探索',
  'sidebar.artists': '歌手',
  'sidebar.albums': '专辑',
  'sidebar.favorites': '我的收藏',
  'sidebar.recent': '最近播放',
  'sidebar.folders': '文件夹',
  'sidebar.plugins': '插件管理',
  'sidebar.account': '账号',
  'title.back': '后退',
  'title.search': '搜索音乐...',
  'title.recognize': '听歌识曲',
  'title.searchHistory': '搜索历史',
  'title.clear': '清空',
  'title.lightText': '切换浅色字体',
  'title.darkText': '切换深色字体',
  'title.lightTheme': '切换浅色',
  'title.darkTheme': '切换深色',
  'title.announcement': '公告',
  'title.viewAnnouncement': '查看公告',
  'title.settings': '设置',
  'title.profile': '个人中心',
  'title.signIn': '登录 / 注册',
  'title.miniMode': 'Mini 模式',
  'title.fullscreenUnavailable': '全屏模式下不可用',
  'title.maximize': '最大化',
  'home.nowPlaying': '正在播放',
  'home.noCurrentSong': '暂无正在播放的歌曲',
  'home.unknownSinger': '未知歌手',
  'home.waitingToPlay': '等待播放',
  'home.onlineMusic': '在线音乐',
  'home.localMusic': '本地音乐',
  'home.lyricPlaceholder': '播放歌曲后，这里会显示当前歌词',
  'home.loadingLyrics': '正在加载歌词…',
  'home.lyricsUnavailable': '歌词不可用',
  'home.noSyncedLyrics': '暂无同步歌词',
  'home.instrumental': '纯音乐 / 暂无歌词',
  'home.seek': '调整播放进度',
  'home.pause': '暂停',
  'home.play': '播放',
  'home.next': '下一首',
  'home.hotComments': '热评推荐',
  'home.anotherComment': '换一下',
  'home.loadingHotComment': '正在加载热评',
  'home.searchSong': '搜索歌曲：{title}',
  'home.clickToSearch': '点击搜索',
  'home.totalListenDuration': '总听歌时长',
  'home.songTotalDuration': '歌曲总时长',
  'home.librarySize': '库大小',
  'home.losslessRatio': '无损占比',
  'home.totalSongs': '总歌曲',
  'home.playCount': '播放次数',
  'home.longestPlayed': '常听歌曲',
  'home.hourlyDistribution': '24小时播放分布',
  'home.leaderboard': '听歌排行榜',
  'home.loadFailed': '加载失败：',
  'home.retry': '重试',
  'home.unknownSong': '未知歌曲',
  'home.unknownArtist': '未知歌手',
  'home.deletedSong': '已删除歌曲',
  'home.you': '你',
  'home.dailyRanking': '单日听歌时长排行',
  'home.weeklyRanking': '本周听歌时长排行',
  'home.totalRanking': '累计听歌时长排行',
  'home.daily': '日榜',
  'home.weekly': '周榜',
  'home.total': '总榜',
  'home.hoursMinutes': '{hours}小时{minutes}分',
  'home.hours': '{hours}小时',
  'home.minutes': '{minutes}分钟',
  'home.times': '{count}次',
  'home.refresh': '刷新',
  'home.noRankingData': '暂无排行榜数据',
  'home.rankingFailed': '排行榜加载失败',
  'home.clickToRetry': '点击重试',
  'home.loginRankingLabel': '前往登录页面查看个人排名',
  'home.loginRankingTitle': '登录后查看个人排名',
  'home.notSignedIn': '未登录',
  'home.signInToViewRanking': '登录后查看个人排名',
  'home.signIn': '去登录',
} as const;

export type TranslationKey = keyof typeof zhCN;
export type TranslationParams = Record<string, string | number>;

const enUS: Record<TranslationKey, string> = {
  'language.section': 'Language',
  'language.title': 'App language',
  'language.description': 'Choose the language used throughout the app.',
  'settings.tab.account': 'Account',
  'settings.tab.general': 'General',
  'settings.tab.details': 'Details',
  'settings.tab.plugins': 'Plugins',
  'settings.tab.appearance': 'Appearance',
  'settings.tab.playback': 'Playback',
  'settings.tab.download': 'Downloads',
  'settings.tab.library': 'Music Library',
  'settings.tab.toolbox': 'Toolbox',
  'settings.tab.desktopLyrics': 'Desktop Lyrics',
  'settings.tab.shortcuts': 'Shortcuts',
  'settings.tab.advanced': 'Advanced',
  'settings.tab.debug': 'Debug',
  'settings.tab.about': 'About',
  'settings.search.placeholder': 'Search settings',
  'settings.search.clear': 'Clear settings search',
  'settings.search.found': '{count} settings found',
  'settings.search.empty': 'No matching settings',
  'settings.search.suggestion': 'Try “quality”, “lyrics”, or “cache”',
  'settings.sidebar.resize': 'Drag to resize the sidebar; double-click to reset',
  'settings.fallback.title': 'Coming soon',
  'settings.fallback.description': 'This settings section is being prepared.',
  'general.section': 'General & Startup',
  'general.launchOnStartup': 'Launch at startup',
  'general.checkUpdates': 'Check for updates at startup',
  'general.gpuAcceleration': 'GPU acceleration',
  'general.closeToTray': 'Minimize to tray when closing',
  'general.qualityBadges': 'Show audio quality badges',
  'general.songComments': 'Show song annotations',
  'general.scrollToTop': 'Show scroll-to-top button',
  'general.taskbarControls': 'Enable taskbar playback controls',
  'general.writeArtistAvatar': 'Write artist avatar changes to audio tags',
  'general.writeArtistAvatarHint': 'When enabled, manually changed artist avatars are written to local audio files. Collaborative tracks, remote tracks, CUE splits, and read-only files are skipped.',
  'general.songClickAction': 'Play songs with',
  'general.singleClick': 'Single click',
  'general.doubleClick': 'Double click',
  'general.storage': 'Storage',
  'general.streamCacheLimit': 'Playback cache limit',
  'general.streamCacheHint': 'Online tracks are cached locally after streaming, so they do not need to be downloaded again. The least recently played tracks are removed when the cache is full.',
  'general.clearStreamCache': 'Clear streaming cache',
  'general.clearStreamCacheHint': 'The current online track will keep playing, but other cached tracks will need to be downloaded again.',
  'general.clearing': 'Clearing...',
  'general.clear': 'Clear',
  'general.resetData': 'Reset app data',
  'general.resetting': 'Resetting...',
  'general.unavailableScanning': 'Unavailable while scanning',
  'general.reset': 'Reset',
  'general.resetConfirm': 'This clears the music library, listening history, favorites, and settings, then restores the initial state. Your music files will not be deleted. Continue?',
  'general.gpuUpdated': 'GPU acceleration updated. Restart the app to apply it.',
  'general.gpuFailed': 'Could not save the GPU acceleration setting.',
  'general.cacheCleared': 'Streaming cache cleared.',
  'general.cacheClearFailed': 'Could not clear the streaming cache.',
  'general.resetFailed': 'Could not clear app data. Please try again.',
  'sidebar.home': 'Home',
  'sidebar.localMusic': 'Local Music',
  'sidebar.explore': 'Explore',
  'sidebar.artists': 'Artists',
  'sidebar.albums': 'Albums',
  'sidebar.favorites': 'Favorites',
  'sidebar.recent': 'Recently Played',
  'sidebar.folders': 'Folders',
  'sidebar.plugins': 'Plugin Manager',
  'sidebar.account': 'Account',
  'title.back': 'Back',
  'title.search': 'Search music...',
  'title.recognize': 'Identify Music',
  'title.searchHistory': 'Search History',
  'title.clear': 'Clear',
  'title.lightText': 'Use light text',
  'title.darkText': 'Use dark text',
  'title.lightTheme': 'Switch to light theme',
  'title.darkTheme': 'Switch to dark theme',
  'title.announcement': 'Announcements',
  'title.viewAnnouncement': 'View announcements',
  'title.settings': 'Settings',
  'title.profile': 'Profile',
  'title.signIn': 'Sign in / Register',
  'title.miniMode': 'Mini Mode',
  'title.fullscreenUnavailable': 'Unavailable in fullscreen',
  'title.maximize': 'Maximize',
  'home.nowPlaying': 'Now Playing',
  'home.noCurrentSong': 'Nothing is playing',
  'home.unknownSinger': 'Unknown Artist',
  'home.waitingToPlay': 'Ready to Play',
  'home.onlineMusic': 'Online Music',
  'home.localMusic': 'Local Music',
  'home.lyricPlaceholder': 'Current lyrics will appear here after playback starts.',
  'home.loadingLyrics': 'Loading lyrics…',
  'home.lyricsUnavailable': 'Lyrics unavailable',
  'home.noSyncedLyrics': 'No synchronized lyrics',
  'home.instrumental': 'Instrumental / No lyrics',
  'home.seek': 'Seek playback position',
  'home.pause': 'Pause',
  'home.play': 'Play',
  'home.next': 'Next',
  'home.hotComments': 'Featured Comment',
  'home.anotherComment': 'Another One',
  'home.loadingHotComment': 'Loading featured comment',
  'home.searchSong': 'Search for: {title}',
  'home.clickToSearch': 'Search',
  'home.totalListenDuration': 'Total Listening Time',
  'home.songTotalDuration': 'Library Duration',
  'home.librarySize': 'Library Size',
  'home.losslessRatio': 'Lossless Share',
  'home.totalSongs': 'Total Songs',
  'home.playCount': 'Play Count',
  'home.longestPlayed': 'Most Played',
  'home.hourlyDistribution': 'Listening by Hour',
  'home.leaderboard': 'Listening Leaderboard',
  'home.loadFailed': 'Could not load: ',
  'home.retry': 'Retry',
  'home.unknownSong': 'Unknown Song',
  'home.unknownArtist': 'Unknown Artist',
  'home.deletedSong': 'Deleted Song',
  'home.you': 'You',
  'home.dailyRanking': 'Daily listening time',
  'home.weeklyRanking': 'This week’s listening time',
  'home.totalRanking': 'All-time listening time',
  'home.daily': 'Day',
  'home.weekly': 'Week',
  'home.total': 'All Time',
  'home.hoursMinutes': '{hours}h {minutes}m',
  'home.hours': '{hours}h',
  'home.minutes': '{minutes}m',
  'home.times': '{count} plays',
  'home.refresh': 'Refresh',
  'home.noRankingData': 'No leaderboard data yet',
  'home.rankingFailed': 'Could not load the leaderboard',
  'home.clickToRetry': 'Try again',
  'home.loginRankingLabel': 'Go to sign in to view your ranking',
  'home.loginRankingTitle': 'Sign in to view your ranking',
  'home.notSignedIn': 'Not signed in',
  'home.signInToViewRanking': 'Sign in to view your ranking',
  'home.signIn': 'Sign In',
};

const messages: Record<AppLanguage, Record<TranslationKey, string>> = {
  // 'system' 在运行时由 resolveLanguage 解析为实际语言，此处仅满足类型约束。
  'system': zhCN,
  'zh-CN': zhCN,
  'en-US': enUS,
};

/** 根据 navigator.language 推测系统语言，映射到支持的 AppLanguage。 */
function resolveSystemLanguage(): AppLanguage {
  if (typeof navigator === 'undefined') return 'zh-CN';
  const navLang = navigator.language || 'zh-CN';
  if (navLang.startsWith('en')) return 'en-US';
  return 'zh-CN';
}

/** 将 'system' 解析为实际语言，非 'system' 原样返回。 */
export const resolveLanguage = (lang: AppLanguage): AppLanguage => (
  lang === 'system' ? resolveSystemLanguage() : lang
);

/** Exact source-text translations used by the compatibility localizer for legacy components. */
export const CORE_ENGLISH_SOURCE_TEXT: Readonly<Record<string, string>> = Object.freeze(
  Object.fromEntries(
    (Object.keys(zhCN) as TranslationKey[]).map(key => [zhCN[key], enUS[key]]),
  ),
);

export const normalizeAppLanguage = (value: unknown): AppLanguage => (
  value === 'en-US' || value === 'system' ? (value as AppLanguage) : 'zh-CN'
);

export const translateForLanguage = (
  language: AppLanguage,
  key: TranslationKey,
  params: TranslationParams = {},
): string => {
  const resolved = resolveLanguage(language);
  const template = messages[resolved][key] ?? zhCN[key];
  return template.replace(/\{(\w+)\}/g, (match, name: string) => (
    Object.prototype.hasOwnProperty.call(params, name) ? String(params[name]) : match
  ));
};

export interface AppLanguageApi {
  language: ComputedRef<AppLanguage>;
  storedLanguage: ComputedRef<AppLanguage>;
  isEnglish: ComputedRef<boolean>;
  setLanguage: (language: AppLanguage) => void;
  t: (key: TranslationKey, params?: TranslationParams) => string;
}

export const useAppLanguage = (): AppLanguageApi => {
  const { settings, patchSettings } = useSettings();
  const storedLanguage = computed(() => normalizeAppLanguage(settings.value.language));
  const language = computed(() => resolveLanguage(storedLanguage.value));
  const isEnglish = computed(() => language.value === 'en-US');

  return {
    language,
    storedLanguage,
    isEnglish,
    setLanguage: nextLanguage => patchSettings({ language: nextLanguage }),
    t: (key, params) => translateForLanguage(language.value, key, params),
  };
};
