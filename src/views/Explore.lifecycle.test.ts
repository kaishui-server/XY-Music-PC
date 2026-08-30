import { describe, expect, it } from 'vitest';

import mainShellSource from '../components/layout/MainShell.vue?raw';
import routerSource from '../router/index.ts?raw';
import exploreSource from './Explore.vue?raw';
import exploreRecommendationsSource from './ExploreRecommendations.vue?raw';

describe('Explore page lifecycle', () => {
  it('keeps a single element root and uses a non-blocking route transition', () => {
    expect(mainShellSource).not.toContain('mode="out-in"');
    expect(mainShellSource).toContain('<transition :name="skipNextPageTransition ? \'\' : \'page-fade\'">');
    expect(exploreSource).toContain('<div class="explore-route-root h-full min-h-0">');
    expect(exploreSource).toContain('<div class="explore-page h-full overflow-y-auto custom-scrollbar px-6 pb-12 pt-5">');
    expect(exploreSource).not.toContain('<Teleport to="body">');
  });

  it('invalidates pending work when the explore page unmounts', () => {
    expect(exploreSource).toContain('onBeforeUnmount(() => {');
    expect(exploreSource).toContain('loadSequence += 1;');
    expect(exploreSource).not.toContain('Teleport to="body"');
  });

  it('keeps recommendations compact and opens the full list on a separate route', () => {
    expect(exploreSource).toContain('class="explore-playlist-list"');
    expect(exploreSource).toContain('class="explore-more-footer"');
    expect(exploreSource).toContain("path: '/explore/recommendations'");
    expect(routerSource).toContain("path: '/explore/recommendations'");
  });

  it('opens recommended playlists in the shared online detail page', () => {
    for (const source of [exploreSource, exploreRecommendationsSource]) {
      expect(source).toContain('onlineDetailStore.setContext({');
      expect(source).toContain("path: '/online-detail'");
      expect(source).toContain("type: 'playlist'");
    }
  });

  it('waits for search submission before opening the search page', () => {
    expect(exploreSource).toContain('v-model="exploreSearchQuery"');
    expect(exploreSource).toContain('@keydown.enter.prevent="submitSearch"');
    expect(exploreSource).toContain('@blur="submitSearch"');
    expect(exploreSource).toContain('navigationStore.setSearch(query);');
    expect(exploreSource).not.toContain('@click="openSearch"');
  });
});
