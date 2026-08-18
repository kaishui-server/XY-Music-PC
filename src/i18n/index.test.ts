import { describe, expect, it } from 'vitest';

import { normalizeAppLanguage, translateForLanguage } from './index';
import { translateLegacyUiText } from './domLocalization';

describe('app language', () => {
  it('normalizes missing and unsupported languages to Chinese', () => {
    expect(normalizeAppLanguage(undefined)).toBe('zh-CN');
    expect(normalizeAppLanguage('fr-FR')).toBe('zh-CN');
    expect(normalizeAppLanguage('en-US')).toBe('en-US');
  });

  it('translates interface copy and interpolates values', () => {
    expect(translateForLanguage('zh-CN', 'settings.tab.general')).toBe('常规');
    expect(translateForLanguage('en-US', 'settings.tab.general')).toBe('General');
    expect(translateForLanguage('en-US', 'settings.search.found', { count: 3 }))
      .toBe('3 settings found');
  });

  it('translates legacy interface text without changing unknown user content', () => {
    expect(translateLegacyUiText('  删除歌单  ')).toBe('  Delete Playlist  ');
    expect(translateLegacyUiText('共 12 首歌曲')).toBe('12 songs');
    expect(translateLegacyUiText('上次：上传 2 个歌单，3 个错误'))
      .toBe('Last: 2 playlists uploaded, 3 errors');
    expect(translateLegacyUiText('更多菜单 4 项')).toBe('4 items in More');
    expect(translateLegacyUiText('已保存 8 张，已选择 2 张')).toBe('8 saved, 2 selected');
    expect(translateLegacyUiText('播放更低音质')).toBe('Use Lower Quality');
    expect(translateLegacyUiText('跳到下一首')).toBe('Skip to Next Song');
    expect(translateLegacyUiText('系统默认')).toBe('System Default');
    expect(translateLegacyUiText('跟随系统')).toBe('Follow System');
    expect(translateLegacyUiText('活力橙')).toBe('Vibrant Orange');
    expect(translateLegacyUiText('流光微调')).toBe('Flowing Light Tuning');
    expect(translateLegacyUiText('用户自己输入的歌名')).toBe('用户自己输入的歌名');
  });
});
