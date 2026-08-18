import { describe, expect, it } from 'vitest';

import appSource from '../../App.vue?raw';
import hoverDetailsSource from '../../composables/useButtonHoverDetails.ts?raw';
import searchIndexSource from '../../features/settings/searchIndex.ts?raw';
import storeSource from '../../features/settings/store.ts?raw';
import settingsViewSource from '../../views/Settings.vue?raw';
import playerDetailSource from '../player/PlayerDetail.vue?raw';
import audioOutputSource from './SettingsAudioOutput.vue?raw';
import source from './SettingsMinimal.vue?raw';

describe('SettingsMinimal', () => {
  it('adds a persisted detail settings page and puts player-detail controls under playback', () => {
    expect(settingsViewSource).toContain("minimal: 'settings.tab.details'");
    expect(settingsViewSource).toContain("activeTab === 'minimal'");
    expect(source).toContain('settings.showButtonHoverDetails');
    expect(source).toContain('role="switch"');
    expect(source).toContain('鼠标悬停按钮时显示详情');
    expect(source).toContain('播放详情页封面');
    expect(source).toContain('设置每次打开歌曲播放详情页时的封面显示方式。');
    expect(source).toContain("patchSettings({ playerDetailCoverMode: value })");
    expect(source).toContain("{ value: 'show', label: '展示' }");
    expect(source).toContain("{ value: 'hide', label: '隐藏' }");
    expect(source).toContain("{ value: 'remember', label: '跟随上次选择' }");
    expect(source).toContain('detail-cover-mode-menu-enter-active');
    expect(storeSource).toContain('showButtonHoverDetails: true');
    expect(audioOutputSource).toContain('播放详情页设置');
    expect(audioOutputSource).toContain('歌曲无封面时默认显示封面');
    expect(audioOutputSource).toContain('importPlayerDetailFallbackCover');
    expect(audioOutputSource).toContain('恢复软件默认封面');
    expect(audioOutputSource).toContain('播放时阻止电脑睡眠');
    expect(audioOutputSource).toContain('settings.preventComputerSleepWhilePlaying');
    expect(storeSource).toContain('preventComputerSleepWhilePlaying: true');
    expect(searchIndexSource).toContain("label: '播放时阻止电脑睡眠'");
    expect(audioOutputSource).toContain("{ value: 'show', label: '展示' }");
    expect(audioOutputSource).toContain("{ value: 'hide', label: '隐藏' }");
    expect(audioOutputSource).toContain("{ value: 'remember', label: '跟随上次选择' }");
    expect(audioOutputSource).toContain('role="listbox"');
    expect(audioOutputSource).toContain('cover-mode-menu-enter-active');
    expect(audioOutputSource).toContain("coverModeMenuOpen ? 'rotate-180");
    expect(storeSource).toContain("playerDetailCoverMode: 'show'");
    expect(storeSource).toContain("playerDetailFallbackCoverPath: ''");
    expect(storeSource).toContain('playerDetailCoverLastHidden: false');
    expect(searchIndexSource).toContain("minimal: '细节'");
    expect(searchIndexSource).toContain("makeItems('audioOutput', '播放详情页设置'");
    expect(searchIndexSource).toContain("label: '播放详情页封面'");
    expect(searchIndexSource).toContain("label: '歌曲无封面时默认显示封面'");
  });

  it('applies the selected cover default and remembers manual choices', () => {
    expect(playerDetailSource).toContain("settings.value.playerDetailCoverMode === 'hide'");
    expect(playerDetailSource).toContain("settings.value.playerDetailCoverMode === 'remember'");
    expect(playerDetailSource).toContain('settings.value.playerDetailCoverLastHidden');
    expect(playerDetailSource).toContain('patchSettings({ playerDetailCoverLastHidden: coverHidden.value })');
    expect(playerDetailSource).toContain('coverHidden.value = visible ? resolveInitialCoverHidden() : false');
  });

  it('uses inline animated dropdowns for playback choices', () => {
    expect(audioOutputSource).toContain("type PlaybackDropdown = 'quality' | 'fallback' | 'failure' | 'device' | 'cover'");
    expect(audioOutputSource).toContain("toggleDropdown('quality')");
    expect(audioOutputSource).toContain("toggleDropdown('fallback')");
    expect(audioOutputSource).toContain("toggleDropdown('failure')");
    expect(audioOutputSource).toContain("toggleDropdown('device')");
    expect(audioOutputSource).toContain('playback-dropdown-enter-active');
    expect(audioOutputSource).toContain('localizeUi(option.label)');
    expect(audioOutputSource).not.toContain('showQualityModal');
    expect(audioOutputSource).not.toContain('showOutputDeviceModal');
  });

  it('suppresses native button titles while preserving setting hints', () => {
    expect(appSource).toContain('useButtonHoverDetails(() => settings.value.showButtonHoverDetails)');
    expect(hoverDetailsSource).toContain('button[title], [role="button"][title]');
    expect(hoverDetailsSource).toContain("element.matches('.setting-hint, [data-keep-hover-details]')");
    expect(hoverDetailsSource).toContain("attributeFilter: ['title']");
    expect(source).toContain('设置页灰色和黄色感叹号提示不受影响');
  });
});
