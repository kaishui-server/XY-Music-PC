import { describe, expect, it } from 'vitest';

import source from './SettingsHome.vue?raw';
import homeItemsSource from '../../features/settings/homeItems.ts?raw';

describe('SettingsHome', () => {
  it('offers sortable visibility controls for all four home modules', () => {
    expect(source).toContain('首页管理');
    expect(homeItemsSource).toContain("label: '正在播放的歌曲'");
    expect(homeItemsSource).toContain("label: '热评推荐'");
    expect(homeItemsSource).toContain("label: '数据统计'");
    expect(homeItemsSource).toContain("label: '听歌排行榜'");
    expect(source).toContain('data-home-module-row');
    expect(source).toContain('@pointerdown="startDragging(index, $event)"');
  });

  it('prevents the last visible module from being disabled', () => {
    expect(source).toContain('enabledCount.value <= 1');
    expect(source).toContain("showToast('首页至少需要显示一个模块', 'info')");
    expect(source).toContain('isVisible(item.key) && enabledCount <= 1');
  });
});
