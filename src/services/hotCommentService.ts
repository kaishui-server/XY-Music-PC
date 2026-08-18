import { pluginHttpRequest } from './tauri/pluginApi';

const HOT_COMMENT_API = 'https://api.fuchenboke.cn/api/wangyi.php';

export interface HotComment {
  comment: string;
  songTitle: string | null;
}

const WRAPPING_QUOTE_PAIRS: ReadonlyArray<readonly [string, string]> = [
  ['“', '”'],
  ['‘', '’'],
  ['「', '」'],
  ['『', '』'],
  ['"', '"'],
  ["'", "'"],
];

export function formatHotCommentForDisplay(comment: string): string {
  const normalized = comment.trim();
  const alreadyWrapped = WRAPPING_QUOTE_PAIRS.some(([opening, closing]) => (
    normalized.startsWith(opening)
    && normalized.endsWith(closing)
    && normalized.length >= opening.length + closing.length
  ));

  return alreadyWrapped ? normalized : `“${normalized}”`;
}

export function parseHotComment(raw: string): HotComment {
  const normalized = raw.replace(/^\uFEFF/u, '').trim();
  if (!normalized) throw new Error('热评内容为空');

  const matches = [...normalized.matchAll(/《([^《》]+)》/gu)];
  const titleMatch = matches[matches.length - 1];
  const songTitle = titleMatch?.[1]?.trim() || null;

  if (!titleMatch || titleMatch.index === undefined) {
    return { comment: normalized, songTitle };
  }

  const comment = normalized
    .slice(0, titleMatch.index)
    .replace(/(?:[—–-]{1,2}\s*)?(?:网易云热评)?\s*$/u, '')
    .trim();

  return {
    comment: comment || normalized,
    songTitle,
  };
}

export async function fetchHotComment(): Promise<HotComment> {
  const cacheBuster = `${Date.now()}-${Math.random().toString(36).slice(2)}`;
  const response = await pluginHttpRequest(
    'GET',
    `${HOT_COMMENT_API}?_=${cacheBuster}`,
    { Accept: 'text/plain, */*' },
    undefined,
    12,
    3,
  );

  if (response.status < 200 || response.status >= 300) {
    throw new Error(`热评接口请求失败（HTTP ${response.status}）`);
  }

  return parseHotComment(response.body);
}
