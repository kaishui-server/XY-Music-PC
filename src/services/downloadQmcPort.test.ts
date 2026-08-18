import { describe, expect, it } from 'vitest';

import downloadSource from './downloadService.ts?raw';
import downloadApiSource from './tauri/downloadApi.ts?raw';
import toolboxSource from '../../src-tauri/src/toolbox.rs?raw';

describe('ported encrypted download support', () => {
  it('passes media headers and QMC keys through the download stack', () => {
    expect(downloadSource).toContain('resolved.ekey');
    expect(downloadSource).toContain('resolved.headers');
    expect(downloadSource).toContain('decryptQmcFile(destPath, song.remote_ekey)');
    expect(downloadApiSource).toContain("tauriInvoke('decrypt_qmc_file'");
    expect(toolboxSource).toContain('fn decrypt_qmc_file_inplace');
  });
});
