import { describe, expect, it } from 'vitest';

import aboutSource from '../../components/settings/SettingsAbout.vue?raw';
import advancedSource from '../../components/settings/SettingsAdvanced.vue?raw';
import debugSource from '../../components/settings/SettingsDebug.vue?raw';
import logExportSource from '../../components/settings/LogExportActions.vue?raw';
import settingsSource from '../../views/Settings.vue?raw';
import { disableDeveloperMode, enableDeveloperMode, useDeveloperMode } from './developerMode';

describe('developer mode settings entry', () => {
  it('persists a shared developer mode state', () => {
    disableDeveloperMode();
    expect(useDeveloperMode().isDeveloperMode.value).toBe(false);
    enableDeveloperMode();
    expect(useDeveloperMode().isDeveloperMode.value).toBe(true);
    disableDeveloperMode();
  });

  it('requires five consecutive clicks on the about-page phrase', () => {
    expect(aboutSource).toContain('DEVELOPER_MODE_CLICK_COUNT = 5');
    expect(aboutSource).toContain('@click="section.developerModeEntry && handleDeveloperModeClick()"');
    expect(aboutSource).toContain('将音乐给予你');
    expect(aboutSource).toContain("showToast('已进入开发者模式', 'success')");
  });

  it('separates concept-edition and XianYu Music developer credits', () => {
    const conceptCredits = aboutSource.slice(
      aboutSource.indexOf("id: 'concept'"),
      aboutSource.indexOf("id: 'xianyu-music'"),
    );
    const xianyuMusicCredits = aboutSource.slice(
      aboutSource.indexOf("id: 'xianyu-music'"),
      aboutSource.indexOf('] as const;'),
    );

    expect(conceptCredits).toContain("title: '弦予音乐概念版'");
    expect(conceptCredits).toContain("label: '@绛狐'");
    expect(conceptCredits).not.toContain("label: '@ShenYichenCN'");
    expect(conceptCredits).not.toContain("label: '@知难辞'");
    expect(conceptCredits).not.toContain("label: '@TaXiaoQi'");
    expect(xianyuMusicCredits).toContain("title: '弦予音乐'");
    expect(xianyuMusicCredits).toContain("label: '@ShenYichenCN'");
    expect(xianyuMusicCredits).toContain("label: '@知难辞'");
    expect(xianyuMusicCredits).toContain("label: '@绛狐'");
    expect(xianyuMusicCredits).toContain("label: '@TaXiaoQi'");
  });

  it('shows Debug only in developer mode and allows exiting it', () => {
    expect(settingsSource).toContain("{ id: 'debug' as const, name: t('settings.tab.debug') }");
    expect(settingsSource).toContain("activeTab === 'debug'");
    expect(settingsSource).toContain('if (!isDeveloperMode.value) return base;');
    expect(debugSource).toContain('@click="disableDeveloperMode"');
    expect(debugSource).toContain('退出开发者模式');
    expect(debugSource).toContain('播放初始化动画');
    expect(debugSource).toContain('@click="triggerOnboarding"');
    expect(advancedSource).toContain('showDeleteConfirmation');
    expect(advancedSource).toContain('确认删除全部日志');
    expect(advancedSource).toContain('const entryCount = ref');
    expect(advancedSource).toContain("{ flush: 'post' }");
    expect(advancedSource).not.toContain('{ deep: true }');
  });

  it('shows advanced settings to regular users and keeps log export there', () => {
    expect(settingsSource).toContain("advanced: 'settings.tab.advanced'");
    expect(settingsSource).toContain("activeTab === 'advanced'");
    expect(advancedSource).toContain('<LogExportActions />');
    expect(debugSource).not.toContain('<LogExportActions />');
    expect(logExportSource).toContain('导出全部日志');
    expect(logExportSource).toContain('导出错误日志');
    expect(advancedSource).toContain('删除全部日志');
    expect(advancedSource).toContain('应用备份');
    expect(advancedSource).toContain('showDeleteConfirmation');
    expect(advancedSource).toContain('从 BakaMusic 或 MusicFree 软件导入歌单');
    expect(advancedSource).toContain('preparePluginBackupFile');
    expect(advancedSource).toContain('<BackupImportResultModal');
  });
});
