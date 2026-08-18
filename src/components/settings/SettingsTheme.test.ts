import { describe, expect, it } from 'vitest';

import source from './SettingsTheme.vue?raw';

describe('SettingsTheme custom skin entry', () => {
  it('opens the editor without applying an empty custom background first', () => {
    expect(source).toContain('@click="openCustomModal"');
    expect(source).not.toContain("@click=\"setColorScheme('custom'); openCustomModal()\"");
  });

  it('localizes appearance option values and accent choices', () => {
    expect(source).toContain('const TEXT = computed(() => localizeCopy(TEXT_SOURCE));');
    expect(source).toContain('const FLOW_TEXT = computed(() => localizeCopy(FLOW_TEXT_SOURCE));');
    expect(source).toContain('const BLUR_TEXT = computed(() => localizeCopy(BLUR_TEXT_SOURCE));');
    expect(source).toContain('const accentThemeOptions = computed');
    expect(source).toContain('v-for="option in accentThemeOptions"');
  });
});
