import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { fetchHotCommentMock } = vi.hoisted(() => ({
  fetchHotCommentMock: vi.fn(),
}));

vi.mock('../services/hotCommentService', () => ({
  fetchHotComment: fetchHotCommentMock,
}));

describe('useHotCommentRecommendation', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.resetModules();
    fetchHotCommentMock.mockReset();
  });

  afterEach(() => {
    vi.clearAllTimers();
    vi.useRealTimers();
  });

  it('keeps the current comment across component remounts', async () => {
    const firstComment = { comment: '第一条', songTitle: '歌曲一' };
    fetchHotCommentMock.mockResolvedValue(firstComment);
    const module = await import('./useHotCommentRecommendation');

    const firstInstance = module.useHotCommentRecommendation();
    await firstInstance.ensureHotCommentRecommendation();

    const remountedInstance = module.useHotCommentRecommendation();
    await remountedInstance.ensureHotCommentRecommendation();

    expect(fetchHotCommentMock).toHaveBeenCalledTimes(1);
    expect(remountedInstance.hotComment.value).toEqual(firstComment);
  });

  it('automatically refreshes once after three minutes', async () => {
    fetchHotCommentMock
      .mockResolvedValueOnce({ comment: '第一条', songTitle: null })
      .mockResolvedValueOnce({ comment: '第二条', songTitle: null });
    const module = await import('./useHotCommentRecommendation');
    const recommendation = module.useHotCommentRecommendation();

    await recommendation.ensureHotCommentRecommendation();
    await vi.advanceTimersByTimeAsync(module.HOT_COMMENT_AUTO_REFRESH_MS - 1);
    expect(fetchHotCommentMock).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(1);
    expect(fetchHotCommentMock).toHaveBeenCalledTimes(2);
    expect(recommendation.hotComment.value?.comment).toBe('第二条');
  });

  it('restarts the three-minute countdown after a manual refresh', async () => {
    fetchHotCommentMock
      .mockResolvedValueOnce({ comment: '第一条', songTitle: null })
      .mockResolvedValueOnce({ comment: '手动更换', songTitle: null })
      .mockResolvedValueOnce({ comment: '自动更换', songTitle: null });
    const module = await import('./useHotCommentRecommendation');
    const recommendation = module.useHotCommentRecommendation();

    await recommendation.ensureHotCommentRecommendation();
    await vi.advanceTimersByTimeAsync(60_000);
    await recommendation.refreshHotComment();
    await vi.advanceTimersByTimeAsync(module.HOT_COMMENT_AUTO_REFRESH_MS - 1);
    expect(fetchHotCommentMock).toHaveBeenCalledTimes(2);

    await vi.advanceTimersByTimeAsync(1);
    expect(fetchHotCommentMock).toHaveBeenCalledTimes(3);
    expect(recommendation.hotComment.value?.comment).toBe('自动更换');
  });
});
