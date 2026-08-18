import { ref, watch, onUnmounted } from 'vue';
import type { Song } from '../types';

const isDownloadDialogVisible = ref(false);
const currentDownloadSong = ref<Song | null>(null);

// 只记忆是否额外导出独立文件；音频本体始终下载并内嵌歌词、封面。
export const DOWNLOAD_DIALOG_EXTRA_LYRICS_KEY = 'dl_dialog_extra_lyrics';
export const DOWNLOAD_DIALOG_EXTRA_COVER_KEY = 'dl_dialog_extra_cover';

const readBool = (key: string, fallback: boolean): boolean => {
  const v = localStorage.getItem(key);
  return v === null ? fallback : v === 'true';
};

const downloadExtraLyrics = ref(readBool(DOWNLOAD_DIALOG_EXTRA_LYRICS_KEY, false));
const downloadExtraCover = ref(readBool(DOWNLOAD_DIALOG_EXTRA_COVER_KEY, false));

let watchRegistered = false;
let closeDialogTimer: ReturnType<typeof setTimeout> | null = null;

function registerPersistenceWatchers() {
  if (watchRegistered) return;
  watch(downloadExtraLyrics, (value) => {
    localStorage.setItem(DOWNLOAD_DIALOG_EXTRA_LYRICS_KEY, String(value));
  });
  watch(downloadExtraCover, (value) => {
    localStorage.setItem(DOWNLOAD_DIALOG_EXTRA_COVER_KEY, String(value));
  });
  watchRegistered = true;
}

export function useDownloadDialog() {
  registerPersistenceWatchers();

  onUnmounted(() => {
    if (closeDialogTimer) {
      clearTimeout(closeDialogTimer);
      closeDialogTimer = null;
    }
  });

  const openDownloadDialog = (song: Song) => {
    currentDownloadSong.value = song;
    isDownloadDialogVisible.value = true;
  };

  const closeDownloadDialog = () => {
    isDownloadDialogVisible.value = false;
    // 延迟清理对象以保持关闭动画过渡的平滑性
    if (closeDialogTimer) {
      clearTimeout(closeDialogTimer);
    }
    closeDialogTimer = setTimeout(() => {
      currentDownloadSong.value = null;
      closeDialogTimer = null;
    }, 300);
  };

  return {
    isDownloadDialogVisible,
    currentDownloadSong,
    openDownloadDialog,
    closeDownloadDialog,
    downloadExtraLyrics,
    downloadExtraCover,
  };
}
