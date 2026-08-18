import {pluginApi} from '../services/tauri/pluginApi';

/** 需要走后端代理的封面域名（WebView2 直连常 403/加载失败） */
const PROXY_COVER_DOMAINS = [
  'hdslb.com',
  'bilivideo.com',
  'y.gtimg.cn',
  'qpic.cn',
  'sycdn.kuwo.cn',
  // 网易云 CDN 在 WebView2 内直连经常 403/加载失败（应用 Origin 不在其白名单），
  // 必须走后端 proxy_image（带 Referer: https://music.163.com/）才能稳定显示
  'music.126.net',
  '163.com',
];

const coverProxyCache = new Map<string, string>();
const pendingCallbacks = new Map<string, Set<(dataUrl: string) => void>>();
const inFlightUrls = new Set<string>();
/** 已尝试代理且失败的 URL（避免对失败项重复请求） */
const coverProxyAttempted = new Set<string>();

function needsBilibiliReferer(url: string): boolean {
  try {
    const hostname = new URL(url).hostname.toLowerCase();
    return hostname === 'hdslb.com'
      || hostname.endsWith('.hdslb.com')
      || hostname === 'bilivideo.com'
      || hostname.endsWith('.bilivideo.com');
  } catch {
    return false;
  }
}

/** http 封面和需要特殊 Referer 的 B 站封面统一通过后端加载。 */
export function needsCoverProxy(url: string): boolean {
  if (!url || url.startsWith('data:') || url.startsWith('asset:')) return false;
  if (url.startsWith('http://')) return true;
  if (needsBilibiliReferer(url)) return true;
  return PROXY_COVER_DOMAINS.some(domain => url.includes(domain));
}

/** 为给定 URL 推荐合适的 Referer（B 站、网易云等防盗链域名）。 */
function getRefererFor(url: string): string | undefined {
  if (needsBilibiliReferer(url)) return 'https://www.bilibili.com';
  try {
    const hostname = new URL(url).hostname.toLowerCase();
    if (hostname.includes('music.126.net') || hostname.includes('163.com')) {
      return 'https://music.163.com/';
    }
  } catch { /* ignore */ }
  return undefined;
}

/**
 * 获取可直接用于 <img src> 的封面 URL。
 *
 * 缓存检查在最前面：无论是否需要代理，已缓存的 data: URL 优先返回。
 *
 * - 已有缓存：返回缓存的 data: URL。
 * - 无需代理且无缓存：原样返回，不触发回调。
 * - 需要代理且无缓存：返回 ''（不渲染 <img>，显示占位 SVG），
 *   同时异步发起代理，完成后调用 onResolved(dataUrl) 刷新视图。
 *   代理成功后 getDisplayCoverUrl 命中缓存返回 data: URL，<img> 才渲染。
 *   代理失败后返回 '' 永久占位。
 *
 * 返回 '' 而非原始 URL 的原因：代理域名（music.126.net 等）直连必 403，
 * 返回原始 URL 会导致 <img> 渲染后加载失败、显示破碎图标且反复触发 @error。
 *
 * @param url 原始封面 URL
 * @param onResolved 代理完成回调（仅在需要代理且异步成功时触发）
 * @returns 当前可用于 <img> 的 URL
 */
export function getDisplayCoverUrl(
  url: string,
  onResolved?: (dataUrl: string) => void,
): string {
  if (!url) return '';

  // 缓存检查在最前面：无论是否需要代理，已缓存的 data: URL 优先返回
  const cached = coverProxyCache.get(url);
  if (cached) return cached;

  if (!needsCoverProxy(url)) return url;

  // 代理中或已失败：返回 '' 显示占位，不渲染 <img> 避免破碎图标
  if (inFlightUrls.has(url) || coverProxyAttempted.has(url)) return '';

  if (onResolved) {
    const callbacks = pendingCallbacks.get(url) ?? new Set<(dataUrl: string) => void>();
    callbacks.add(onResolved);
    pendingCallbacks.set(url, callbacks);
  }

  if (!inFlightUrls.has(url)) {
    inFlightUrls.add(url);
    const referer = getRefererFor(url);
    void pluginApi.proxyImage(url, referer)
      .then((dataUrl) => {
        if (!dataUrl) return;
        coverProxyCache.set(url, dataUrl);
        pendingCallbacks.get(url)?.forEach(callback => callback(dataUrl));
      })
      .catch(error => {
        coverProxyAttempted.add(url);
        console.warn('[Cover] 在线封面代理失败:', error);
      })
      .finally(() => {
        inFlightUrls.delete(url);
        pendingCallbacks.delete(url);
      });
  }

  return '';
}

/**
 * 尝试通过后端代理加载图片 URL（供 @error 兜底使用）。
 * 成功后返回 data: URL，失败返回 null。
 * 已在代理中或已失败的 URL 不会重复请求。
 */
export async function tryProxyImage(url: string): Promise<string | null> {
  if (!url || url.startsWith('data:') || url.startsWith('asset:')) return null;

  // 先查缓存
  const cached = coverProxyCache.get(url);
  if (cached) return cached;

  // 已在代理中或已失败，不重复请求
  if (inFlightUrls.has(url) || coverProxyAttempted.has(url)) return null;

  inFlightUrls.add(url);
  try {
    const referer = getRefererFor(url);
    const dataUrl = await pluginApi.proxyImage(url, referer);
    if (dataUrl) {
      coverProxyCache.set(url, dataUrl);
      return dataUrl;
    }
    coverProxyAttempted.add(url);
    return null;
  } catch {
    coverProxyAttempted.add(url);
    return null;
  } finally {
    inFlightUrls.delete(url);
  }
}

/** 清除代理缓存和失败记录（用于切换搜索/重新搜索时重置状态） */
export function clearCoverProxyCache(): void {
  coverProxyCache.clear();
  coverProxyAttempted.clear();
  inFlightUrls.clear();
  pendingCallbacks.clear();
}
