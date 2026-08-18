import { describe, expect, it, vi } from 'vitest';

import { resolvePlayerDetailFallbackCoverUrl } from './usePlayerDetailFallbackCover';

describe('resolvePlayerDetailFallbackCoverUrl', () => {
  it('keeps directly displayable URLs unchanged', () => {
    expect(resolvePlayerDetailFallbackCoverUrl('data:image/png;base64,abc')).toBe('data:image/png;base64,abc');
    expect(resolvePlayerDetailFallbackCoverUrl('asset://cover.png')).toBe('asset://cover.png');
  });

  it('converts an imported local path to an asset URL', () => {
    const convert = vi.fn((path: string) => `asset://${path}`);
    expect(resolvePlayerDetailFallbackCoverUrl('D:\\covers\\fallback.png', convert))
      .toBe('asset://D:\\covers\\fallback.png');
    expect(convert).toHaveBeenCalledWith('D:\\covers\\fallback.png');
  });

  it('returns an empty value when the software default should be used', () => {
    expect(resolvePlayerDetailFallbackCoverUrl('')).toBe('');
  });
});
