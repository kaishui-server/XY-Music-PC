import { convertFileSrc } from '@tauri-apps/api/core';
import { computed } from 'vue';

import { useSettings } from '../features/settings/useSettings';

export function resolvePlayerDetailFallbackCoverUrl(
  path: string,
  convertLocalPath: (value: string) => string = convertFileSrc,
): string {
  const normalized = path.trim();
  if (!normalized) return '';
  if (/^(?:https?:|data:|asset:|blob:)/i.test(normalized)) return normalized;
  return convertLocalPath(normalized);
}

export function usePlayerDetailFallbackCover() {
  const { settings } = useSettings();
  return computed(() => resolvePlayerDetailFallbackCoverUrl(
    settings.value.playerDetailFallbackCoverPath,
  ));
}

// The setting originally lived under the player-detail section, but the selected
// image is the app-wide fallback artwork. Keep the old exports for compatibility
// while exposing names that describe its global use.
export const resolveDefaultCoverUrl = resolvePlayerDetailFallbackCoverUrl;
export const useDefaultCover = usePlayerDetailFallbackCover;
