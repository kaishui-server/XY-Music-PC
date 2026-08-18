import { describe, expect, it } from 'vitest';

import { formatHotCommentForDisplay, parseHotComment } from './hotCommentService';

describe('hotCommentService', () => {
  it('extracts the song title and removes the trailing attribution', () => {
    expect(parseHotComment('念旧的人总是活的像个拾荒者——网易云热评《拾荒的人》')).toEqual({
      comment: '念旧的人总是活的像个拾荒者',
      songTitle: '拾荒的人',
    });
  });

  it('uses the last title when the comment contains multiple book-title marks', () => {
    expect(parseHotComment('读过《昨日》，才听懂这首歌。——《后来》')).toEqual({
      comment: '读过《昨日》，才听懂这首歌。',
      songTitle: '后来',
    });
  });

  it('keeps comments without a song title displayable', () => {
    expect(parseHotComment('愿你永远有重新开始的勇气')).toEqual({
      comment: '愿你永远有重新开始的勇气',
      songTitle: null,
    });
  });

  it('only adds quotation marks when the comment is not already wrapped', () => {
    expect(formatHotCommentForDisplay('愿你永远有重新开始的勇气')).toBe('“愿你永远有重新开始的勇气”');
    expect(formatHotCommentForDisplay('“愿你永远有重新开始的勇气”')).toBe('“愿你永远有重新开始的勇气”');
    expect(formatHotCommentForDisplay('"愿你永远有重新开始的勇气"')).toBe('"愿你永远有重新开始的勇气"');
    expect(formatHotCommentForDisplay('「愿你永远有重新开始的勇气」')).toBe('「愿你永远有重新开始的勇气」');
  });
});
