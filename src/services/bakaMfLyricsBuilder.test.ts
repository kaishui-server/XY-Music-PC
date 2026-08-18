import { describe, expect, it } from 'vitest';

import { buildBakaMfLyricsRaw } from './bakaMfLyricsBuilder';

describe('buildBakaMfLyricsRaw', () => {
  it('将酷狗 KRC 逐字歌词转换为 Enhanced LRC', () => {
    const result = buildBakaMfLyricsRaw({
      lyric: '[0,420]周(0,52)杰(52,52)伦 (104,52)- (156,52)红(208,52)尘(260,52)客(312,52)栈(364,52)',
    });

    expect(result).toBe('[00:00.000]<00:00.000>周<00:00.052>杰<00:00.104>伦 <00:00.156>- <00:00.208>红<00:00.260>尘<00:00.312>客<00:00.364>栈<00:00.416>');
  });

  it('保留普通 LRC 歌词', () => {
    const result = buildBakaMfLyricsRaw({
      lyric: '[00:20.80]琴键上透着光',
    });

    expect(result).toBe('[00:20.80]琴键上透着光');
  });

  it('按文件级酷我规则保留所有逐字行，并支持缺少行时间戳', () => {
    const result = buildBakaMfLyricsRaw({
      lxlyric: [
        '[kuwo:0]',
        '[00:01.000]天<1000,1200>外<1600,1800>',
        '来<-2000,6000>物<-1200,6800>',
      ].join('\n'),
    });

    expect(result).toBe([
      '[00:01.000]<00:01.100>天<00:01.700>外<00:01.800>',
      '[00:02.000]<00:02.000>来<00:02.800>物<00:06.800>',
    ].join('\n'));
  });
});
