import { describe, expect, it } from 'vitest';

import source from './SettingsAbout.vue?raw';

describe('SettingsAbout concept actions', () => {
  it('shows a maintenance notice for all concept-edition actions', () => {
    expect(source).toContain("showToast('维护中，暂不可用', 'info')");
    expect(source).toContain("if (sectionId === 'concept')");
    expect(source).toContain('@click="handleUpdateClick(currentSection.id)"');
    // 4 个链接按钮（官网、开源地址、参考项目、加入群组）在概念版点击均走维护提示
    expect(source.match(/@click="handleAboutLinkClick\(currentSection\.id, aboutConfig\.(officialSiteUrl|projectUrl|referenceProjectUrl|joinGroupUrl)\)"/g)).toHaveLength(4);
  });

  it('keeps official-edition links and update checking available', () => {
    expect(source).toContain('void openExternal(url)');
    expect(source).toContain('await openUrl(normalized)');
    expect(source).toContain('void checkUpdateManual()');
  });

  it('provides top-right page switching between concept and formal about pages', () => {
    expect(source).toContain("isConceptPage ? '关于正式版' : '关于概念版'");
    expect(source).toContain('function switchAboutPage()');
  });
});
