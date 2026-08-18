import { describe, expect, it } from 'vitest';

import { applyAccentTheme, normalizeAccentThemeId } from './accentTheme';

const createRoot = (dark = false) => {
  const attributes = new Map<string, string>();
  const properties = new Map<string, string>();
  const root = {
    classList: {
      contains: (name: string) => name === 'dark' && dark,
    },
    setAttribute: (name: string, value: string) => attributes.set(name, value),
    style: {
      setProperty: (name: string, value: string) => properties.set(name, value),
    },
  } as unknown as HTMLElement;

  return { root, attributes, properties };
};

describe('accent theme', () => {
  it('falls back to the default accent for unknown persisted values', () => {
    const { root, attributes, properties } = createRoot();
    expect(normalizeAccentThemeId('unknown')).toBe('default');
    applyAccentTheme('unknown', undefined, root);

    expect(attributes.get('data-accent-theme')).toBe('default');
    expect(properties.get('--theme-accent')).toBe('#EC4141');
  });

  it('applies a selected color and its rgb channels', () => {
    const { root, attributes, properties } = createRoot();
    applyAccentTheme('blue', undefined, root);

    expect(attributes.get('data-accent-theme')).toBe('blue');
    expect(properties.get('--theme-accent')).toBe('#2878D0');
    expect(properties.get('--theme-accent-rgb')).toBe('40 120 208');
  });

  it('inverts the monochrome accent with the app color scheme', () => {
    const light = createRoot();
    applyAccentTheme('mono', undefined, light.root);
    expect(light.properties.get('--theme-accent')).toBe('#111111');
    expect(light.properties.get('--theme-accent-contrast')).toBe('#FFFFFF');

    const dark = createRoot(true);
    applyAccentTheme('mono', undefined, dark.root);
    expect(dark.properties.get('--theme-accent')).toBe('#FFFFFF');
    expect(dark.properties.get('--theme-accent-contrast')).toBe('#111111');
  });

  it('applies and normalizes a custom accent color', () => {
    const { root, attributes, properties } = createRoot();
    applyAccentTheme('custom', '#12abef', root);

    expect(attributes.get('data-accent-theme')).toBe('custom');
    expect(properties.get('--theme-accent')).toBe('#12ABEF');
    expect(properties.get('--theme-accent-rgb')).toBe('18 171 239');
  });
});
