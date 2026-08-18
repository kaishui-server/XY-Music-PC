import type { AccentThemeId } from '../types';

export interface AccentThemeOption {
  id: AccentThemeId;
  label: string;
  hint: string;
  swatch: string;
}

export const ACCENT_THEME_OPTIONS: readonly AccentThemeOption[] = [
  { id: 'default', label: '默认红', hint: '经典', swatch: '#EC4141' },
  { id: 'orange', label: '活力橙', hint: '温暖', swatch: '#EA580C' },
  { id: 'gold', label: '琥珀金', hint: '明亮', swatch: '#CA8A04' },
  { id: 'green', label: '青翠绿', hint: '清新', swatch: '#16A064' },
  { id: 'blue', label: '澄澈蓝', hint: '安静', swatch: '#2878D0' },
  { id: 'violet', label: '星云紫', hint: '深邃', swatch: '#7C5CD6' },
  { id: 'pink', label: '柔雾粉', hint: '轻盈', swatch: '#DB4F91' },
  {
    id: 'mono',
    label: '纯黑白',
    hint: '克制',
    swatch: 'linear-gradient(135deg, #111 0 48%, #fff 52% 100%)',
  },
  {
    id: 'custom',
    label: '自定义',
    hint: '调色板',
    swatch: 'conic-gradient(#ef4444, #f59e0b, #22c55e, #3b82f6, #8b5cf6, #ef4444)',
  },
] as const;

const COLOR_VALUES: Record<Exclude<AccentThemeId, 'mono' | 'custom'>, string> = {
  default: '#EC4141',
  orange: '#EA580C',
  gold: '#CA8A04',
  green: '#16A064',
  blue: '#2878D0',
  violet: '#7C5CD6',
  pink: '#DB4F91',
};

export const normalizeAccentThemeId = (value: unknown): AccentThemeId => (
  ACCENT_THEME_OPTIONS.some(option => option.id === value)
    ? value as AccentThemeId
    : 'default'
);

const hexToRgbTriplet = (hex: string) => {
  const value = hex.replace('#', '');
  return `${Number.parseInt(value.slice(0, 2), 16)} ${Number.parseInt(value.slice(2, 4), 16)} ${Number.parseInt(value.slice(4, 6), 16)}`;
};

export const normalizeCustomAccentColor = (value: unknown) => (
  typeof value === 'string' && /^#[0-9a-f]{6}$/i.test(value)
    ? value.toUpperCase()
    : '#EC4141'
);

export function applyAccentTheme(
  value: unknown,
  customColor: unknown = '#EC4141',
  root: HTMLElement = document.documentElement,
) {
  const accentTheme = normalizeAccentThemeId(value);
  const isDark = root.classList.contains('dark');
  const accent = accentTheme === 'mono'
    ? (isDark ? '#FFFFFF' : '#111111')
    : accentTheme === 'custom'
      ? normalizeCustomAccentColor(customColor)
      : COLOR_VALUES[accentTheme];
  const contrast = accentTheme === 'mono' && isDark ? '#111111' : '#FFFFFF';

  root.setAttribute('data-accent-theme', accentTheme);

  const style = root.style as CSSStyleDeclaration & Record<string, string>;
  const setProperty = (name: string, nextValue: string) => {
    if (typeof style.setProperty === 'function') {
      style.setProperty(name, nextValue);
    } else {
      style[name] = nextValue;
    }
  };

  setProperty('--theme-accent', accent);
  setProperty('--theme-accent-rgb', hexToRgbTriplet(accent));
  setProperty('--theme-accent-contrast', contrast);
}
