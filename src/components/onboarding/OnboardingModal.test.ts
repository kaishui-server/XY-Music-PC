import { describe, expect, it } from 'vitest';

import source from './OnboardingModal.vue?raw';
import mainShellSource from '../layout/MainShell.vue?raw';
import statisticsPageSource from '../statistics/StatisticsPage.vue?raw';
import homeSettingsSource from '../settings/SettingsHome.vue?raw';
import sidebarSettingsSource from '../settings/SettingsSidebar.vue?raw';
import footerSettingsSource from '../settings/SettingsFooterLayout.vue?raw';

describe('OnboardingModal splash', () => {
  it('continues when the splash is clicked anywhere', () => {
    expect(source).toContain('@click="continueFromSplash"');
    expect(source).toContain('点击任意位置以继续');
  });

  it('keeps a native window drag region available while onboarding covers the title bar', () => {
    expect(source).toContain('data-tauri-drag-region');
    expect(source).toContain('class="absolute left-1/4 right-1/4 top-0 z-[70] h-10"');
    expect(source).toContain('@click.stop');
  });

  it('keeps tall shortcut settings visible from the top at minimum window height', () => {
    expect(source).toContain('max-w-6xl mx-auto min-h-full');
    expect(source).not.toContain('max-w-6xl mx-auto h-full');
    expect(source).toContain('快捷按键');
  });

  it('waits for an explicit click instead of skipping the splash automatically', () => {
    expect(source).not.toContain('SPLASH_AUTO_ADVANCE_DELAY');
    expect(source).not.toContain('setTimeout(continueFromSplash');
  });

  it('does not render the old splash continue button', () => {
    expect(source.match(/@click="nextStep"/g)).toHaveLength(1);
  });

  it('selects window materials by clicking the option row without apply buttons', () => {
    expect(source).toContain("@click=\"selectWindowMaterial('none')\"");
    expect(source).toContain("@click=\"selectWindowMaterial('mica')\"");
    expect(source).toContain("@click=\"selectWindowMaterial('acrylic')\"");
    expect(source).toContain("@click=\"selectWindowMaterial('blur')\"");
    expect(source).not.toContain('@click="setMaterialToNone"');
    expect(source).not.toContain("@click=\"toggleWindowMaterial('mica')\"");
  });

  it('applies onboarding settings immediately', () => {
    expect(source).toContain('const { settings, patchSettings } = useSettings()');
    expect(source).toContain('patchSettings({ shortcuts: createDefaultShortcutSettings() })');
    expect(source).toContain('[actionId]: nextBinding');
    expect(source).toContain(':class="onboardingSurfaceClass"');
    expect(source).toContain("materialMode.value === 'none'");
  });

  it('places plugin management after shortcuts and allows deferring it', () => {
    expect(source).toContain("type Step = 'splash' | 'theme' | 'material' | 'minimal' | 'accent' | 'layout' | 'shortcuts' | 'plugins' | 'account'");
    expect(source).toContain("{ key: 'plugins', label: '插件' }");
    expect(source).toContain('添加或管理插件');
    expect(source).toContain("pluginManagerVisited ? '继续' : '稍后添加'");
    expect(source).not.toContain('sm:border-r border-black/10 dark:border-white/10');
  });

  it('branches from setup mode and keeps the accent palette in detailed setup', () => {
    expect(source).toContain("@click=\"selectSetupMode('simple')\"");
    expect(source).toContain("@click=\"selectSetupMode('detailed')\"");
    expect(source).toContain("goToStep(mode === 'simple' ? 'plugins' : 'accent')");
    expect(source).toContain("setupMode.value === 'simple'");
    expect(source).toContain("step === 'accent'");
    expect(source).toContain('快速开始');
    expect(source).toContain('个性化配置');
    expect(source).toContain('v-for="option in ACCENT_THEME_OPTIONS"');
    expect(source).toContain("patchTheme({ accentTheme: value })");
    expect(source).toContain('选择界面主色');
    expect(source).toContain('type="color"');
    expect(source).toContain("patchTheme({ accentTheme: 'custom', customAccentColor: input.value })");
    expect(source).not.toContain('{{ option.label }}</span>');
    expect(source).not.toContain('v-if="minimalMode"');
  });

  it('does not offer a completion action before a setup mode is selected', () => {
    expect(source).toContain("v-else-if=\"step === 'account'\"");
    expect(source).toContain("step !== 'account' && step !== 'minimal'");
    expect(source.match(/@click="handleComplete"/g)).toHaveLength(1);
  });

  it('adds the existing layout managers after accent for detailed setup only', () => {
    expect(source).toContain("{ key: 'accent' as const, label: '主题色' }");
    expect(source).toContain("{ key: 'layout' as const, label: '布局' }");
    expect(source).toContain("{ key: 'shortcuts' as const, label: '快捷键' }");
    expect(source).toContain("step === 'layout'");
    expect(source).toContain("() => import('../settings/SettingsHome.vue')");
    expect(source).toContain("() => import('../settings/SettingsSidebar.vue')");
    expect(source).toContain("() => import('../settings/SettingsFooterLayout.vue')");
    expect(source).toContain('<SettingsHome show-preview');
    expect(source).toContain('<SettingsSidebar show-preview');
    expect(source).toContain('<SettingsFooterLayout heading="底栏管理" show-preview');
  });

  it('uses the full onboarding width and switches the real shell into a home preview', () => {
    expect(source).not.toContain('安排你的空间');
    expect(source).not.toContain('按使用习惯调整首页内容、侧边栏入口和底部控制区');
    expect(source).toContain('key="layout" class="h-full min-h-0 w-full');
    expect(source.match(/@preview="emit\('update:layoutPreviewActive', true\)"/g)).toHaveLength(3);
    expect(source).toContain('visible && !layoutPreviewActive');
    expect(source).not.toContain('OnboardingLayoutPreview');
    expect(mainShellSource).toContain("() => import('../../views/Home.vue')");
    expect(mainShellSource).toContain('<GlobalBackground v-if="!isMiniMode" />');
    expect(mainShellSource).toContain(':preview-home="onboardingLayoutPreviewActive"');
    expect(mainShellSource).toContain('<TitleBar />');
    expect(mainShellSource).toContain('<Home v-if="onboardingLayoutPreviewActive" />');
    expect(mainShellSource).toContain('<PlayerFooter />');
    expect(mainShellSource).toContain("navigationStore.currentViewMode = 'statistics'");
    expect(mainShellSource).toContain('返回布局设置');
  });

  it('keeps only the real home content scrollable during layout preview', () => {
    expect(mainShellSource).toContain(":class=\"onboardingLayoutPreviewActive ? 'pointer-events-none' : ''\"");
    expect(mainShellSource).toContain(":class=\"[footerContainerClass, onboardingLayoutPreviewActive ? 'pointer-events-none' : '']\"");
    expect(mainShellSource).toContain('<main class="flex-1 overflow-hidden relative min-h-0">');
    expect(statisticsPageSource).toContain('statistics-page h-full overflow-y-auto custom-scrollbar');
  });

  it('shows preview actions beside every layout manager heading only when requested', () => {
    for (const settingsSource of [homeSettingsSource, sidebarSettingsSource, footerSettingsSource]) {
      expect(settingsSource).toContain('showPreview?: boolean');
      expect(settingsSource).toContain('v-if="showPreview"');
      expect(settingsSource).toContain("@click=\"$emit('preview')\"");
      expect(settingsSource).toContain('预览');
    }
  });

  it('lazy-loads the existing full plugin manager inside onboarding', () => {
    expect(source).toContain("() => import('../settings/SettingsPlugins.vue')");
    expect(source).toContain('<SettingsPlugins overlay-z-class="z-[10000]" />');
    expect(source).toContain('@click="closePluginManager"');
    expect(source).toContain('完成管理');
  });

  it('keeps the full plugin manager transparent without exposing the onboarding page below it', () => {
    expect(source).toContain('data-onboarding-plugin-manager-surface');
    expect(source).toContain('overflow-hidden');
    expect(source).toContain(":class=\"{ 'invisible pointer-events-none': stepContentHidden }\"");
    expect(source.match(/:class="onboardingSurfaceClass"/g)).toHaveLength(2);
  });
});
