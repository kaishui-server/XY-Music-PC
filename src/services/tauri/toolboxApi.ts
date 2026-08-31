import { tauriInvoke } from './invoke';
import type { RenameConfig, RenameOperation } from './contracts';

export const toolboxApi = {
  previewRename: (rootPath: string, config: RenameConfig) =>
    tauriInvoke('preview_rename', { rootPath, config }),
  applyRename: (operations: RenameOperation[]) =>
    tauriInvoke('apply_rename', { operations }),
  setGpuAcceleration: (enabled: boolean) =>
    tauriInvoke('set_gpu_acceleration', { enabled }),
  downloadWallpaper: (url: string, filename: string) =>
    tauriInvoke('download_wallpaper', { url, filename }),
  importWallpaperFile: (sourcePath: string) =>
    tauriInvoke('import_wallpaper_file', { sourcePath }),
  importPlayerDetailFallbackCover: (sourcePath: string) =>
    tauriInvoke('import_player_detail_fallback_cover', { sourcePath }),
  clearPlayerDetailFallbackCover: () =>
    tauriInvoke('clear_player_detail_fallback_cover'),
  deleteWallpaperFile: (localPath: string) =>
    tauriInvoke('delete_wallpaper_file', { localPath }),
};
