import { describe, expect, it } from 'vitest';

import authServiceSource from './authService.ts?raw';
import rustAuthSource from '../../../src-tauri/src/music/auth.rs?raw';

describe('official auth API transport', () => {
  it('uses HTTPS so captcha POST bodies survive the request', () => {
    expect(authServiceSource).toContain("DEFAULT_AUTH_BASE_URL = 'https://cosn.xymusic.cc:8081/api'");
    expect(rustAuthSource).toContain('OFFICIAL_AUTH_BASE_URL: &str = "https://cosn.xymusic.cc:8081/api"');
  });

  it('migrates saved official HTTP URLs without changing custom servers', () => {
    expect(authServiceSource).toContain("'http://156.233.228.213:8081/api'");
    expect(authServiceSource).toContain("'http://back.xymusic.cc/api'");
    expect(rustAuthSource).toContain('LEGACY_OFFICIAL_AUTH_BASE_URLS');
    expect(rustAuthSource).toContain('"http://156.233.228.213:8081/api"');
    expect(rustAuthSource).toContain('"http://back.xymusic.cc/api"');
    expect(rustAuthSource).toContain('"https://back.xymusic.cc/api"');
    expect(rustAuthSource).toContain('fs::write(&path, &upgraded)');
  });
});
