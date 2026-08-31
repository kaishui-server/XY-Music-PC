import { open } from '@tauri-apps/plugin-dialog';
import { ref } from 'vue';

import { normalizeForegroundStyle } from '../features/settings/store';
import { playerStorage } from '../services/storage/playerStorage';
import { toolboxApi } from '../services/tauri/toolboxApi';
import { useThemeSettings } from './useThemeSettings';

export function useCustomThemeModal() {
  const { settings, theme, patchTheme } = useThemeSettings();
  const preview = ref({
    ...theme.value.customBackground,
    foregroundStyle: normalizeForegroundStyle(theme.value.customBackground.foregroundStyle),
  });

  const handleSelectImage = async () => {
    try {
      const selected = await open({
        multiple: false,
        filters: [{ name: 'Image', extensions: ['png', 'jpg', 'jpeg', 'webp'] }],
      });

      if (selected && typeof selected === 'string') {
        // 不直接保存用户原始路径，避免原图被移动或删除后重启无法恢复。
        preview.value.imagePath = await toolboxApi.importWallpaperFile(selected);
      }
    } catch {
      // Ignore dialog cancellation.
    }
  };

  const handleSave = () => {
    if (!preview.value.imagePath) {
      return;
    }

    patchTheme({
      mode: 'custom',
      dynamicBgType: 'none',
      windowMaterial: 'none',
      customBackground: { ...preview.value },
    });
    // 自定义壁纸是本地设置，保存后立即落盘，不依赖播放模块的延迟持久化。
    playerStorage.writeSettings(settings.value);
  };

  return {
    preview,
    handleSelectImage,
    handleCancel: () => undefined,
    handleSave,
  };
}
