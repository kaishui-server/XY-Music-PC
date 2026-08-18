import { describe, expect, it } from 'vitest';

import authServiceSource from './authService.ts?raw';
import rustAuthSource from '../../../src-tauri/src/music/auth.rs?raw';

describe('official auth API transport', () => {
  it('uses HTTPS so captcha POST bodies survive the request', () => {
    expect(authServiceSource).toContain("DEFAULT_AUTH_BASE_URL = 'https://back.xymusic.cc/api'");
    expect(rustAuthSource).toContain('OFFICIAL_AUTH_BASE_URL: &str = "https://back.xymusic.cc/api"');
  });

  it('migrates saved official HTTP URLs without changing custom servers', () => {
    expect(authServiceSource).toContain("saved.replace('http://back.xymusic.cc', 'https://back.xymusic.cc')");
    expect(rustAuthSource).toContain('saved.replace(');
    expect(rustAuthSource).toContain('"http://back.xymusic.cc"');
    expect(rustAuthSource).toContain('"https://back.xymusic.cc"');
    expect(rustAuthSource).toContain('fs::write(&path, &upgraded)');
  });
});
