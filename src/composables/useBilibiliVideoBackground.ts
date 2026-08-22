import { computed, ref } from 'vue';
import { convertFileSrc } from '@tauri-apps/api/core';

import {
  getStoredPlugins,
  pluginGetVideoSource,
  getPluginUserVariableValues,
  type PluginVideoSource,
} from '../services/pluginEngine';
import { pluginApi } from '../services/tauri/pluginApi';
import type { PluginSearchResult, Song } from '../types';

const BILIBILI_IDENTITY_PATTERN = /bilibili|哔哩哔哩|哔哩|b站/i;
const DEFAULT_BILIBILI_VIDEO_QUALITY = '720P';
const BILIBILI_720P_QUALITY_ID = 64;

/** B站画质 ID → 中文标签映射 */
const BILIBILI_QUALITY_LABELS: Record<number, string> = {
  6: '240P',
  16: '360P',
  32: '480P',
  64: '720P',
  74: '720P60',
  80: '1080P',
  112: '1080P+',
  116: '1080P60',
  120: '4K',
  125: 'HDR',
  126: '杜比视界',
  127: '8K',
};

const videoUrl = ref('');
const cachedVideoPath = ref('');
const sourceSongPath = ref('');
const loading = ref(false);
const error = ref('');
let requestVersion = 0;
const availableVideoQualities = ref<Array<{ qn: number; label: string }>>([]);
const currentVideoQuality = ref(BILIBILI_720P_QUALITY_ID);
let sourceSong: Song | null = null;

function nestedValue(value: unknown, key: string): unknown {
  if (!value || typeof value !== 'object') return undefined;
  const record = value as Record<string, unknown>;
  return record[key]
    ?? (record.rawData && typeof record.rawData === 'object'
      ? (record.rawData as Record<string, unknown>)[key]
      : undefined);
}

function firstString(...values: unknown[]): string {
  return values.find(value => typeof value === 'string' && value.trim())?.toString().trim() || '';
}

function extractBilibiliIdentity(song: Song): { bvid?: string; aid?: string; cid?: string } {
  const raw = song.rawData;
  const pathId = decodeURIComponent(song.path.split('/').pop() || '');
  const identityCandidates = [
    nestedValue(raw, 'bvid'),
    nestedValue(raw, 'id'),
    nestedValue(raw, 'aid'),
    pathId,
  ].map(value => String(value || ''));
  const identityText = identityCandidates.join(' ');
  const bvid = identityText.match(/BV[0-9A-Za-z]{10,}/i)?.[0];
  const aid = firstString(nestedValue(raw, 'aid'))
    || identityText.match(/(?:^|\W)av(\d+)(?:\W|$)/i)?.[1]
    || (!bvid ? identityCandidates.find(value => /^\d+$/.test(value)) : '');
  const cid = firstString(nestedValue(raw, 'cid'));
  return {
    ...(bvid ? { bvid } : {}),
    ...(aid ? { aid: aid.replace(/^av/i, '') } : {}),
    ...(cid ? { cid } : {}),
  };
}

function parseBilibiliResponse(responseBody: string, label: string): Record<string, any> {
  let payload: Record<string, any>;
  try {
    payload = JSON.parse(responseBody) as Record<string, any>;
  } catch {
    throw new Error(`${label}返回了无效数据`);
  }
  if (Number(payload.code) !== 0 || !payload.data) {
    throw new Error(`${label}失败${payload.message ? `：${payload.message}` : ''}`);
  }
  return payload.data as Record<string, any>;
}

/**
 * 旧版 Bilibili 插件只负责歌曲解析，没有 getMvSource 扩展。
 * 这里使用歌曲自身的 BV/AV 号补齐视频流，避免把“接口不存在”误报成插件损坏。
 */
async function resolveBilibiliVideoSource(song: Song, targetQualityId: number = BILIBILI_720P_QUALITY_ID): Promise<PluginVideoSource | null> {
  const identity = extractBilibiliIdentity(song);
  if (!identity.bvid && !identity.aid) return null;

  const identityQuery = identity.bvid
    ? `bvid=${encodeURIComponent(identity.bvid)}`
    : `aid=${encodeURIComponent(identity.aid || '')}`;
  let cid = identity.cid;
  if (!cid) {
    const viewResponse = await pluginApi.pluginHttpRequest(
      'GET',
      `https://api.bilibili.com/x/web-interface/view?${identityQuery}`,
      { Referer: 'https://www.bilibili.com/' },
    );
    const viewData = parseBilibiliResponse(viewResponse.body, 'Bilibili 视频信息解析');
    cid = String(viewData.cid || viewData.pages?.[0]?.cid || '');
  }
  if (!cid) throw new Error('Bilibili 视频信息中缺少 CID');

  // 从插件 Cookie Store 读取 B站登录态（SESSDATA 等），用于解锁 480P 以上画质
  // 直接读取原始 cookie store，用更宽松的域名匹配
  // （getCookies 的双向子串匹配无法匹配 passport.bilibili.com → api.bilibili.com）
  const cookieStore = JSON.parse(localStorage.getItem('__plugin_cookies') || '{}');
  const cookieParts: string[] = [];
  for (const [name, info] of Object.entries(cookieStore)) {
    const c = info as any;
    if (c.domain && c.domain.includes('bilibili.com')) {
      cookieParts.push(`${name}=${c.value}`);
    }
  }

  // 也检查插件 user variables 中可能存储的 B站 cookie / SESSDATA
  const pluginId = song.plugin_id || String(nestedValue(song.rawData, 'pluginId') || '');
  if (pluginId) {
    const userVars = getPluginUserVariableValues(pluginId);
    for (const [key, value] of Object.entries(userVars)) {
      if (!value) continue;
      const lowerKey = key.toLowerCase();
      if (lowerKey.includes('cookie') || lowerKey.includes('sessdata') || lowerKey.includes('access_key')) {
        if (value.includes('=') && value.includes(';')) {
          // 完整 cookie 字符串（如 "SESSDATA=xxx; bili_jct=yyy"）
          cookieParts.push(value);
        } else if (value.includes('=')) {
          // 单个 cookie 键值对（如 "SESSDATA=xxx"）
          cookieParts.push(value);
        } else if (lowerKey.includes('sessdata')) {
          // 纯 SESSDATA 值
          cookieParts.push(`SESSDATA=${value}`);
        }
      }
    }
  }

  const cookieHeader = cookieParts.join('; ');
  const bilibiliHeaders: Record<string, string> = {
    Referer: `https://www.bilibili.com/video/${identity.bvid || `av${identity.aid}`}`,
  };
  if (cookieHeader) bilibiliHeaders.Cookie = cookieHeader;

  const playResponse = await pluginApi.pluginHttpRequest(
    'GET',
    `https://api.bilibili.com/x/player/playurl?${identityQuery}&cid=${encodeURIComponent(cid)}&qn=${targetQualityId}&fnval=16&fourk=1`,
    bilibiliHeaders,
  );
  const playData = parseBilibiliResponse(playResponse.body, 'Bilibili 视频流解析');
  const dashVideos = Array.isArray(playData.dash?.video) ? playData.dash.video : [];
  const isAvc = (candidate: any) => String(candidate?.codecs || '').startsWith('avc1');
  const streamQn = (candidate: any) => Number(candidate?.id) || 0;

  // 解析可用画质列表
  const acceptQuality: number[] = Array.isArray(playData.accept_quality)
    ? playData.accept_quality.filter((q: unknown) => typeof q === 'number')
    : [];
  const availableStreamIds = new Set(dashVideos.map((v: any) => streamQn(v)));
  availableVideoQualities.value = acceptQuality
    .filter(qn => availableStreamIds.has(qn) && BILIBILI_QUALITY_LABELS[qn])
    .sort((a, b) => b - a)
    .map(qn => ({ qn, label: BILIBILI_QUALITY_LABELS[qn] }));
  currentVideoQuality.value = targetQualityId;

  const compatibleVideos = dashVideos
    .filter((candidate: any) => streamQn(candidate) <= targetQualityId)
    .sort((left: any, right: any) => streamQn(right) - streamQn(left));
  const video = dashVideos.find((candidate: any) => (
    streamQn(candidate) === targetQualityId && isAvc(candidate)
  ))
    || dashVideos.find((candidate: any) => streamQn(candidate) === targetQualityId)
    || compatibleVideos.find(isAvc)
    || compatibleVideos[0]
    || dashVideos.find(isAvc)
    || dashVideos[0];
  const directUrl = firstString(video?.baseUrl, video?.base_url, playData.durl?.[0]?.url);
  if (!directUrl) return null;

  const backupUrls = [
    ...(Array.isArray(video?.backupUrl) ? video.backupUrl : []),
    ...(Array.isArray(video?.backup_url) ? video.backup_url : []),
    ...(Array.isArray(playData.durl?.[0]?.backup_url) ? playData.durl[0].backup_url : []),
  ].filter((value): value is string => typeof value === 'string' && /^https?:\/\//i.test(value));

  return {
    url: directUrl,
    backupUrls,
    headers: { Referer: 'https://www.bilibili.com/' },
    videoQuality: DEFAULT_BILIBILI_VIDEO_QUALITY,
    codec: firstString(video?.codecs),
    mimeType: firstString(video?.mimeType, video?.mime_type, 'video/mp4'),
    width: Number(video?.width) || undefined,
    height: Number(video?.height) || undefined,
  };
}

export function isBilibiliPluginSong(song: Song | null | undefined): boolean {
  if (!song || !song.path.startsWith('plugin://')) return false;
  const raw = song.rawData;
  if (nestedValue(raw, 'bvid') || nestedValue(raw, 'aid')) return true;

  const identity = [
    song.path.split('/')[2],
    song.plugin_id,
    nestedValue(raw, 'platform'),
    nestedValue(raw, 'source'),
    nestedValue(raw, 'pluginId'),
  ].filter(Boolean).join(' ');
  return BILIBILI_IDENTITY_PATTERN.test(identity);
}

function toPluginSearchResult(song: Song): PluginSearchResult {
  const raw = song.rawData;
  if (raw && typeof raw === 'object' && 'pluginId' in raw && 'rawData' in raw) {
    return raw as PluginSearchResult;
  }

  const pathId = song.path.split('/').pop() || song.path;
  return {
    id: String(nestedValue(raw, 'id') || nestedValue(raw, 'bvid') || pathId),
    title: song.title || song.name,
    artist: song.artist || '',
    album: song.album || '',
    coverUrl: song.cover_thumb_path || '',
    duration: Math.max(0, Number(song.duration) || 0) * 1000,
    platform: String(nestedValue(raw, 'platform') || 'bilibili'),
    platformId: String(nestedValue(raw, 'id') || nestedValue(raw, 'bvid') || pathId),
    pluginId: song.plugin_id || String(nestedValue(raw, 'pluginId') || ''),
    rawData: raw || song,
  };
}

function withBilibiliHeaders(
  headers: Record<string, string> | undefined,
  userAgent: string | undefined,
): Record<string, string> {
  const merged = { ...(headers || {}) };
  const lowerKeys = new Set(Object.keys(merged).map(key => key.toLowerCase()));
  if (!lowerKeys.has('referer')) merged.Referer = 'https://www.bilibili.com/';
  if (!lowerKeys.has('origin')) merged.Origin = 'https://www.bilibili.com';
  if (userAgent && !lowerKeys.has('user-agent')) merged['User-Agent'] = userAgent;
  return merged;
}

async function removeCachedFile(path: string) {
  if (!path) return;
  await pluginApi.removeCachedBackgroundVideo(path).catch(() => {});
}

export function useBilibiliVideoBackground() {
  const active = computed(() => Boolean(videoUrl.value && sourceSongPath.value));
  const requested = computed(() => Boolean(sourceSongPath.value));

  const stop = async () => {
    requestVersion += 1;
    const previousPath = cachedVideoPath.value;
    videoUrl.value = '';
    cachedVideoPath.value = '';
    sourceSongPath.value = '';
    loading.value = false;
    error.value = '';
    availableVideoQualities.value = [];
    currentVideoQuality.value = BILIBILI_720P_QUALITY_ID;
    sourceSong = null;
    await removeCachedFile(previousPath);
  };

  const start = async (song: Song, qualityId?: number) => {
    if (!isBilibiliPluginSong(song)) {
      throw new Error('当前歌曲不是 Bilibili 插件歌曲');
    }

    const targetQualityId = qualityId ?? currentVideoQuality.value;
    sourceSong = song;
    const previousPath = cachedVideoPath.value;
    const requestId = ++requestVersion;
    videoUrl.value = '';
    cachedVideoPath.value = '';
    sourceSongPath.value = song.path;
    loading.value = true;
    error.value = '';
    void removeCachedFile(previousPath);

    const pluginId = song.plugin_id || String(nestedValue(song.rawData, 'pluginId') || '');
    const source = getStoredPlugins().find(plugin => plugin.id === pluginId);
    if (!source) {
      loading.value = false;
      sourceSongPath.value = '';
      throw new Error('未找到当前 Bilibili 插件');
    }

    let videoSource: PluginVideoSource | null;
    try {
      const qualityLabel = BILIBILI_QUALITY_LABELS[targetQualityId] || DEFAULT_BILIBILI_VIDEO_QUALITY;
      const pluginVideoSource = await pluginGetVideoSource(
        source,
        toPluginSearchResult(song),
        qualityLabel,
      );
      videoSource = pluginVideoSource?.url
        ? pluginVideoSource
        : await resolveBilibiliVideoSource(song, targetQualityId);
    } catch (resolutionError) {
      if (requestId === requestVersion) {
        loading.value = false;
        sourceSongPath.value = '';
        error.value = resolutionError instanceof Error
          ? resolutionError.message
          : String(resolutionError);
      }
      throw resolutionError;
    }
    if (requestId !== requestVersion) return false;
    if (!videoSource?.url) {
      loading.value = false;
      sourceSongPath.value = '';
      throw new Error('未能解析当前 Bilibili 视频');
    }

    const headers = withBilibiliHeaders(videoSource.headers, videoSource.userAgent);
    const candidates = [videoSource.url, ...(videoSource.backupUrls || [])];
    let downloadedPath = '';
    let lastDownloadError: unknown = null;
    for (const candidate of candidates) {
      try {
        downloadedPath = await pluginApi.downloadVideoToCache(candidate, headers);
        if (downloadedPath) break;
      } catch (downloadError) {
        lastDownloadError = downloadError;
      }
    }

    if (requestId !== requestVersion) {
      await removeCachedFile(downloadedPath);
      return false;
    }
    if (!downloadedPath) {
      loading.value = false;
      sourceSongPath.value = '';
      const message = lastDownloadError instanceof Error ? lastDownloadError.message : String(lastDownloadError || '');
      error.value = message;
      throw new Error(message ? `背景视频加载失败：${message}` : '背景视频加载失败');
    }

    cachedVideoPath.value = downloadedPath;
    videoUrl.value = convertFileSrc(downloadedPath);
    loading.value = false;
    return true;
  };

  const toggle = async (song: Song) => {
    if (requested.value) {
      await stop();
      return false;
    }
    return start(song);
  };

  const changeVideoQuality = async (qualityId: number) => {
    if (!sourceSong) return;
    await start(sourceSong, qualityId);
  };

  return {
    active,
    requested,
    loading,
    error,
    videoUrl,
    sourceSongPath,
    availableVideoQualities,
    currentVideoQuality,
    start,
    stop,
    toggle,
    changeVideoQuality,
  };
}
