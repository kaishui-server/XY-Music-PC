import { nextTick } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const createStorage = (initial: Record<string, string> = {}) => {
  const values = new Map(Object.entries(initial));
  return {
    getItem: vi.fn((key: string) => values.get(key) ?? null),
    setItem: vi.fn((key: string, value: string) => values.set(key, value)),
    removeItem: vi.fn((key: string) => values.delete(key)),
    clear: vi.fn(() => values.clear()),
    key: vi.fn((index: number) => [...values.keys()][index] ?? null),
    get length() {
      return values.size;
    },
  } satisfies Storage;
};

describe('useDownloadDialog extra downloads', () => {
  beforeEach(() => {
    vi.resetModules();
  });

  it('defaults both extra files to disabled and persists later choices', async () => {
    const storage = createStorage();
    vi.stubGlobal('localStorage', storage);

    const {
      DOWNLOAD_DIALOG_EXTRA_COVER_KEY,
      DOWNLOAD_DIALOG_EXTRA_LYRICS_KEY,
      useDownloadDialog,
    } = await import('./useDownloadDialog');
    const dialog = useDownloadDialog();

    expect(dialog.downloadExtraLyrics.value).toBe(false);
    expect(dialog.downloadExtraCover.value).toBe(false);

    dialog.downloadExtraLyrics.value = true;
    dialog.downloadExtraCover.value = true;
    await nextTick();

    expect(storage.setItem).toHaveBeenCalledWith(DOWNLOAD_DIALOG_EXTRA_LYRICS_KEY, 'true');
    expect(storage.setItem).toHaveBeenCalledWith(DOWNLOAD_DIALOG_EXTRA_COVER_KEY, 'true');
  });

  it('restores the last choices from the new storage keys', async () => {
    vi.stubGlobal('localStorage', createStorage({
      dl_dialog_extra_lyrics: 'true',
      dl_dialog_extra_cover: 'false',
      // Old checkbox state must not affect the new controls.
      dl_dialog_lyrics: 'false',
      dl_dialog_cover: 'true',
    }));

    const { useDownloadDialog } = await import('./useDownloadDialog');
    const dialog = useDownloadDialog();

    expect(dialog.downloadExtraLyrics.value).toBe(true);
    expect(dialog.downloadExtraCover.value).toBe(false);
  });
});
