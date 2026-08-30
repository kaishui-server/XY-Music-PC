import type { PluginPlaylistSearchResult, PluginSearchResult, PluginSource, Song } from '../types';
import {
  getStoredPlugins,
  pluginGetPlaylistDetail,
  pluginPlaylistSearch,
  pluginSearch,
  pluginTopListSearch,
} from './pluginEngine';

export interface ExplorePlaylist {
  plugin: PluginSource;
  result: PluginPlaylistSearchResult;
}

export interface ExploreData {
  songs: PluginSearchResult[];
  playlists: ExplorePlaylist[];
  charts: ExplorePlaylist[];
}

const MAX_RECOMMENDATIONS = 150;
const MAX_QUERIES = 6;
const MAX_PLUGINS = 3;
const CACHE_TTL_MS = 30 * 60 * 1000;
const EXPLORE_CACHE_KEY = 'xy_explore_recommendations_v3';

interface ExploreLoadOptions {
  forceRefresh?: boolean;
  playlistNames?: string[];
}

interface ExploreCacheEntry {
  pluginSignature: string;
  tasteSignature: string;
  generatedAt: number;
  data: ExploreData;
}

interface TasteTerm {
  text: string;
  weight: number;
}

interface TasteProfile {
  seeds: Song[];
  artistWeights: Map<string, TasteTerm>;
  albumWeights: Map<string, TasteTerm>;
  genreWeights: Map<string, TasteTerm>;
}

interface RankedSong {
  song: PluginSearchResult;
  score: number;
}

interface RankedPlaylist {
  plugin: PluginSource;
  result: PluginPlaylistSearchResult;
  score: number;
  tasteKey: string;
}

let memoryCache: ExploreCacheEntry | null = null;
let inFlightRequest: { signature: string; promise: Promise<ExploreData> } | null = null;

const tasteText = (value: unknown) => String(value ?? '')
  .trim()
  .toLocaleLowerCase()
  .replace(/[^a-z0-9\u3400-\u9fff]+/g, '');

const enabledMusicFreePlugins = () => getStoredPlugins()
  .filter(plugin => plugin.enabled && plugin.format === 'musicfree');

const pluginSignature = (plugins: PluginSource[]) => plugins
  .map(plugin => `${plugin.id}|${plugin.name}|${plugin.version}|${plugin.enabled}`)
  .sort()
  .join('::');

const tasteSignature = (seeds: Song[], playlistNames: string[]) => [
  ...seeds.slice(0, 80).map(song => `${tasteText(song.title)}|${tasteText(song.artist)}|${tasteText(song.album)}`),
  '--playlists--',
  ...playlistNames.map(name => tasteText(name)),
].join('::');

const isExploreData = (value: unknown): value is ExploreData => {
  if (!value || typeof value !== 'object') return false;
  const candidate = value as Partial<ExploreData>;
  return Array.isArray(candidate.songs)
    && Array.isArray(candidate.playlists)
    && Array.isArray(candidate.charts);
};

const readPersistedCache = (): ExploreCacheEntry | null => {
  if (typeof localStorage === 'undefined') return null;
  try {
    const raw = localStorage.getItem(EXPLORE_CACHE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<ExploreCacheEntry>;
    if (!parsed.pluginSignature || !parsed.tasteSignature || !isExploreData(parsed.data)) return null;
    return {
      pluginSignature: parsed.pluginSignature,
      tasteSignature: parsed.tasteSignature,
      generatedAt: Number(parsed.generatedAt) || 0,
      data: parsed.data,
    };
  } catch {
    return null;
  }
};

const isFreshCache = (entry: ExploreCacheEntry | null, pluginKey: string, tasteKey: string) => (
  !!entry
  && entry.pluginSignature === pluginKey
  && entry.tasteSignature === tasteKey
  && entry.generatedAt > 0
  && Date.now() - entry.generatedAt < CACHE_TTL_MS
);

const saveCache = (entry: ExploreCacheEntry) => {
  memoryCache = entry;
  if (typeof localStorage === 'undefined') return;
  try {
    localStorage.setItem(EXPLORE_CACHE_KEY, JSON.stringify(entry));
  } catch {
    // 插件原始数据偶尔包含不可序列化字段时，保留内存缓存即可。
  }
};

const runSafely = async <T>(task: Promise<T>, timeoutMs = 9000): Promise<T | null> => {
  let timeoutId: ReturnType<typeof setTimeout> | null = null;
  try {
    return await Promise.race([
      task,
      new Promise<null>(resolve => {
        timeoutId = setTimeout(() => resolve(null), timeoutMs);
      }),
    ]);
  } catch {
    return null;
  } finally {
    if (timeoutId) clearTimeout(timeoutId);
  }
};

const addTasteTerm = (target: Map<string, TasteTerm>, value: unknown, weight: number) => {
  const text = String(value ?? '').trim();
  const key = tasteText(text);
  if (text.length < 2 || !key) return;
  const previous = target.get(key);
  target.set(key, { text, weight: Math.min(10, (previous?.weight ?? 0) + weight) });
};

const rankedTerms = (terms: Map<string, TasteTerm>) => [...terms.values()]
  .sort((a, b) => b.weight - a.weight);

const isUnknownArtist = (value: unknown) => {
  const normalized = tasteText(value);
  return !normalized || new Set([
    '未知', '未知歌手', '未知艺术家', '歌手未知', 'unknown', 'unknownartist',
    'unknownsinger', 'anonymous', 'variousartists', 'various', '佚名', '不详',
    '无名', 'null', 'none', 'na', 'n/a',
  ].map(tasteText)).has(normalized);
};

const extractGenres = (song: Song) => {
  const raw = song.rawData && typeof song.rawData === 'object' ? song.rawData : {};
  const values: string[] = [];
  for (const key of ['genre', 'genres', 'style', 'styles', 'musicStyle', 'music_style', 'category', 'tags']) {
    const value = (raw as Record<string, unknown>)[key];
    if (typeof value === 'string') values.push(...value.split(/[,，、/|]/));
    else if (Array.isArray(value)) values.push(...value.map(item => String(item)));
  }
  if (song.genre) values.push(song.genre);
  return [...new Set(values.map(value => value.trim()).filter(value => value.length >= 2 && value.length <= 24))];
};

const isGenericPlaylistName = (value: string) => new Set([
  '我喜欢', '我的收藏', '收藏', '喜欢', '我喜欢的音乐',
].map(tasteText)).has(tasteText(value));

const buildTasteProfile = (seeds: Song[], playlistNames: string[]): TasteProfile => {
  const artistWeights = new Map<string, TasteTerm>();
  const albumWeights = new Map<string, TasteTerm>();
  const genreWeights = new Map<string, TasteTerm>();

  seeds.forEach((song, index) => {
    const weight = index < 10 ? 4 : index < 30 ? 2 : 1;
    if (isUnknownArtist(song.artist)) return;
    addTasteTerm(artistWeights, song.artist, weight);
    addTasteTerm(albumWeights, song.album, weight);
    extractGenres(song).forEach(genre => addTasteTerm(genreWeights, genre, weight));
  });
  playlistNames.forEach(name => {
    if (!isGenericPlaylistName(name)) addTasteTerm(genreWeights, name, 2);
  });

  return { seeds, artistWeights, albumWeights, genreWeights };
};

const buildQueryList = (profile: TasteProfile) => {
  const queries: string[] = [];
  const seen = new Set<string>();
  const add = (value: unknown) => {
    const text = String(value ?? '').trim();
    const key = tasteText(text);
    if (text.length < 2 || !key || seen.has(key)) return;
    seen.add(key);
    queries.push(text);
  };

  rankedTerms(profile.artistWeights).slice(0, 3).forEach(term => add(term.text));
  rankedTerms(profile.genreWeights).slice(0, 3).forEach(term => add(term.text));
  profile.seeds.filter(song => isUnknownArtist(song.artist)).slice(0, 2).forEach(song => add(song.title));
  rankedTerms(profile.albumWeights).slice(0, 2).forEach(term => {
    if (queries.length < MAX_QUERIES) add(term.text);
  });
  return queries.slice(0, MAX_QUERIES);
};

const isLikelyNonMusicTitle = (value: string) => /放屁|屁声|fart|burp|呕吐|呕吐声|咳嗽|咳嗽声|喷嚏|打鼾|打呼噜|snore/i.test(value);

const isLikelyIrrelevantContent = (title: string, artist: string, album: string) => /儿歌|童谣|童歌|幼儿|婴幼|早教|胎教|摇篮曲|亲宝|贝瓦|咕力|佛经|佛曲|佛乐|佛音|佛号|诵经|念经|读经|经文|大悲咒|往生咒|楞严咒|金刚经|地藏经|阿弥陀|观音|禅修|禅乐|禅音|冥想|asmr|白噪音|哄睡|睡前故事|童话故事|国学|三字经|弟子规|百家姓|千字文|评书|相声|小品|广播剧|有声书|nursery\s*rhymes|kids\s*songs|buddhist\s*chant|sutra|mantra/i.test(`${title} ${artist} ${album}`);

const shouldFilterMissingCovers = (covers: string[]) => {
  const total = covers.length;
  const missing = covers.filter(cover => !cover.trim()).length;
  return total > 0 && missing * 2 <= total;
};

const songTasteKey = (song: Pick<PluginSearchResult, 'title' | 'artist'>) => (
  `${tasteText(song.title)}|${tasteText(song.artist)}`
);

const recommendationScore = (song: PluginSearchResult, profile: TasteProfile, query: string) => {
  const unknownArtist = isUnknownArtist(song.artist);
  const artist = profile.artistWeights.get(tasteText(song.artist));
  const album = profile.albumWeights.get(tasteText(song.album));
  const genreScore = unknownArtist ? 0 : extractGenres({
    ...song,
    name: song.title,
    artist_names: [],
    effective_artist_names: [],
    album_artist: song.artist,
    album_key: song.album,
    is_various_artists_album: false,
    collapse_artist_credits: false,
    duration: Math.floor(song.duration / 1000),
    path: `plugin://${song.pluginId}/${song.id}`,
  } as unknown as Song).reduce((sum, genre) => sum + (profile.genreWeights.get(tasteText(genre))?.weight ?? 0), 0);
  let score = (artist?.weight ?? 0) * 42 + genreScore * 35 + (album?.weight ?? 0) * 14 + 1;
  if (tasteText(song.title).includes(tasteText(query))) score += 45;
  if (unknownArtist) score -= 18;
  return score;
};

const isBilibiliPlugin = (plugin: PluginSource) => /bilibili|哔哩|b站/i.test(`${plugin.id} ${plugin.name}`);

const videoTitlePenalty = (title: string) => {
  let penalty = 0;
  if (/[\[\]【】|｜]/.test(title)) penalty += 55;
  if (/up主|官方|现场|翻唱|纯音乐|高音质|动态歌词|完整版|mv|music\s*video|live|cover/i.test(title)) penalty += 35;
  return penalty;
};

const selectRecommendedSongs = (ranked: Map<string, RankedSong>, plugins: PluginSource[]) => {
  const groups = new Map<string, RankedSong[]>();
  for (const item of ranked.values()) {
    const pluginId = item.song.pluginId.trim();
    if (!pluginId) continue;
    groups.set(pluginId, [...(groups.get(pluginId) ?? []), item]);
  }
  groups.forEach(group => group.sort((a, b) => b.score - a.score));
  const groupList = [...groups.entries()].sort((a, b) => (b[1][0]?.score ?? 0) - (a[1][0]?.score ?? 0));
  if (groupList.length === 0) return [];
  const pluginNames = new Map(plugins.map(plugin => [plugin.id, plugin.name]));
  const pluginTarget = Math.ceil(MAX_RECOMMENDATIONS / groupList.length);
  const hasAlternativePlugin = groupList.some(([id]) => !/bilibili|哔哩|b站/i.test(`${id} ${pluginNames.get(id) ?? ''}`));
  const pluginCounts = new Map<string, number>();
  const artistCounts = new Map<string, number>();
  const titleCounts = new Map<string, number>();
  const selectedKeys = new Set<string>();
  const result: PluginSearchResult[] = [];

  for (let round = 0; result.length < MAX_RECOMMENDATIONS && round < MAX_RECOMMENDATIONS; round++) {
    const roundArtists = new Set<string>();
    for (let offset = 0; offset < groupList.length; offset++) {
      const [pluginId, items] = groupList[(offset + round) % groupList.length];
      const isBilibili = /bilibili|哔哩|b站/i.test(`${pluginId} ${pluginNames.get(pluginId) ?? ''}`);
      const pluginLimit = isBilibili && hasAlternativePlugin ? Math.min(pluginTarget, 2) : pluginTarget;
      if ((pluginCounts.get(pluginId) ?? 0) >= pluginLimit) continue;
      let candidate = items.find(item => {
        const key = songTasteKey(item.song);
        const titleKey = tasteText(item.song.title);
        const artistKey = isUnknownArtist(item.song.artist) ? '__unknown_artist__' : tasteText(item.song.artist);
        const artistLimit = artistKey === '__unknown_artist__' ? 2 : 3;
        return !selectedKeys.has(key)
          && (titleCounts.get(titleKey) ?? 0) < 2
          && (artistCounts.get(artistKey) ?? 0) < artistLimit
          && !roundArtists.has(artistKey);
      });
      candidate ??= items.find(item => {
        const key = songTasteKey(item.song);
        const titleKey = tasteText(item.song.title);
        const artistKey = isUnknownArtist(item.song.artist) ? '__unknown_artist__' : tasteText(item.song.artist);
        const artistLimit = artistKey === '__unknown_artist__' ? 2 : 3;
        return !selectedKeys.has(key)
          && (titleCounts.get(titleKey) ?? 0) < 2
          && (artistCounts.get(artistKey) ?? 0) < artistLimit;
      });
      if (!candidate) continue;
      const key = songTasteKey(candidate.song);
      const titleKey = tasteText(candidate.song.title);
      const artistKey = isUnknownArtist(candidate.song.artist) ? '__unknown_artist__' : tasteText(candidate.song.artist);
      selectedKeys.add(key);
      roundArtists.add(artistKey);
      pluginCounts.set(pluginId, (pluginCounts.get(pluginId) ?? 0) + 1);
      artistCounts.set(artistKey, (artistCounts.get(artistKey) ?? 0) + 1);
      titleCounts.set(titleKey, (titleCounts.get(titleKey) ?? 0) + 1);
      result.push(candidate.song);
      if (result.length >= MAX_RECOMMENDATIONS) break;
    }
  }

  if (result.length < MAX_RECOMMENDATIONS) {
    for (const item of [...ranked.values()].sort((a, b) => b.score - a.score)) {
      if (result.length >= MAX_RECOMMENDATIONS) break;
      const key = songTasteKey(item.song);
      const titleKey = tasteText(item.song.title);
      const artistKey = isUnknownArtist(item.song.artist) ? '__unknown_artist__' : tasteText(item.song.artist);
      const artistLimit = artistKey === '__unknown_artist__' ? 2 : 3;
      if (selectedKeys.has(key) || (titleCounts.get(titleKey) ?? 0) >= 2 || (artistCounts.get(artistKey) ?? 0) >= artistLimit) continue;
      selectedKeys.add(key);
      titleCounts.set(titleKey, (titleCounts.get(titleKey) ?? 0) + 1);
      artistCounts.set(artistKey, (artistCounts.get(artistKey) ?? 0) + 1);
      result.push(item.song);
    }
  }
  return result;
};

const playlistTrackCount = (item: PluginPlaylistSearchResult): number | null => {
  const raw = item.rawData && typeof item.rawData === 'object' ? item.rawData as Record<string, unknown> : {};
  const parse = (value: unknown): number | null => {
    if (typeof value === 'number' && Number.isFinite(value)) return value;
    if (typeof value === 'string') {
      const match = value.match(/\d+/);
      return match ? Number(match[0]) : null;
    }
    if (value && typeof value === 'object') {
      for (const key of ['value', 'count', 'total', 'num']) {
        const parsed = parse((value as Record<string, unknown>)[key]);
        if (parsed !== null) return parsed;
      }
    }
    return null;
  };
  for (const value of [item.trackCount, raw.trackCount, raw.trackcount, raw.track_count, raw.songCount, raw.songcount, raw.song_count, raw.trackNum, raw.tracknum, raw.videoCount, raw.video_count]) {
    const count = parse(value);
    if (count !== null) return count;
  }
  for (const key of ['tracks', 'songs', 'songList', 'songlist', 'musicList', 'musiclist', 'videos', 'archives']) {
    if (Array.isArray(raw[key])) return raw[key].length;
  }
  return null;
};

const playlistRecommendationScore = (result: PluginPlaylistSearchResult, query: string, profile: TasteProfile) => {
  const text = tasteText(`${result.title} ${result.artist ?? ''}`);
  let score = text.includes(tasteText(query)) ? 40 : 5;
  rankedTerms(profile.artistWeights).slice(0, 6).forEach(term => {
    if (text.includes(tasteText(term.text))) score += term.weight * 18;
  });
  rankedTerms(profile.genreWeights).slice(0, 6).forEach(term => {
    if (text.includes(tasteText(term.text))) score += term.weight * 12;
  });
  return score;
};

const selectRecommendedPlaylists = (ranked: Map<string, RankedPlaylist>) => {
  const groups = new Map<string, RankedPlaylist[]>();
  for (const item of ranked.values()) groups.set(item.plugin.id, [...(groups.get(item.plugin.id) ?? []), item]);
  groups.forEach(group => group.sort((a, b) => b.score - a.score));
  const groupList = [...groups.entries()].sort((a, b) => (b[1][0]?.score ?? 0) - (a[1][0]?.score ?? 0));
  const target = Math.ceil(MAX_RECOMMENDATIONS / Math.max(1, groupList.length));
  const counts = new Map<string, number>();
  const tasteCounts = new Map<string, number>();
  const selected = new Set<string>();
  const result: ExplorePlaylist[] = [];
  for (let round = 0; result.length < MAX_RECOMMENDATIONS && round < MAX_RECOMMENDATIONS; round++) {
    const roundTasteKeys = new Set<string>();
    for (let offset = 0; offset < groupList.length; offset++) {
      const entry = groupList[(offset + round) % groupList.length];
      if ((counts.get(entry[0]) ?? 0) >= target) continue;
      const candidate = entry[1].find(item => {
        const id = `${item.plugin.id}|${item.result.id}`;
        return !selected.has(id) && !roundTasteKeys.has(item.tasteKey) && (tasteCounts.get(item.tasteKey) ?? 0) < 5;
      }) ?? entry[1].find(item => !selected.has(`${item.plugin.id}|${item.result.id}`));
      if (!candidate) continue;
      const id = `${candidate.plugin.id}|${candidate.result.id}`;
      selected.add(id);
      counts.set(entry[0], (counts.get(entry[0]) ?? 0) + 1);
      roundTasteKeys.add(candidate.tasteKey);
      tasteCounts.set(candidate.tasteKey, (tasteCounts.get(candidate.tasteKey) ?? 0) + 1);
      result.push({ plugin: candidate.plugin, result: candidate.result });
      if (result.length >= MAX_RECOMMENDATIONS) break;
    }
  }
  return result;
};

const loadHotChartSongs = async (plugins: PluginSource[], seedKeys: Set<string>) => {
  const result: PluginSearchResult[] = [];
  const seen = new Set<string>();
  const tasks = plugins.slice(0, MAX_PLUGINS).filter(plugin => !isBilibiliPlugin(plugin)).map(async plugin => {
    const charts = await runSafely(pluginTopListSearch(plugin), 9000);
    if (!charts?.length) return;
    const preferred = charts.filter(chart => /热歌|热门|流行|飙升|新歌|top|hit|hot/i.test(chart.title));
    const pool = preferred.length > 0 ? preferred : charts;
    const chart = pool[Math.floor(Math.random() * pool.length)];
    const songs = await runSafely(pluginGetPlaylistDetail(plugin, chart.rawData, 1), 12000);
    if (!songs?.length) return;
    const filterMissingCovers = shouldFilterMissingCovers(songs.map(song => song.coverUrl));
    for (const song of songs.slice(0, 30)) {
      if (!song.title.trim() || (filterMissingCovers && !song.coverUrl.trim())) continue;
      if (isLikelyNonMusicTitle(song.title) || isLikelyIrrelevantContent(song.title, song.artist, song.album)) continue;
      const key = songTasteKey(song);
      if (!key || seedKeys.has(key) || seen.has(key)) continue;
      seen.add(key);
      result.push({ ...song, pluginId: song.pluginId || plugin.id });
    }
  });
  await Promise.all(tasks);
  return result.sort(() => Math.random() - 0.5);
};

const loadChartRecommendations = async (plugins: PluginSource[]) => {
  const charts: ExplorePlaylist[] = [];
  const responses = await Promise.all(plugins.map(async plugin => ({ plugin, results: await runSafely(pluginTopListSearch(plugin), 9000) })));
  for (const { plugin, results } of responses) {
    if (!results) continue;
    const filterMissingCovers = shouldFilterMissingCovers(results.map(item => item.coverUrl));
    const seen = new Set<string>();
    for (const result of results) {
      if (!result.title.trim() || (filterMissingCovers && !result.coverUrl.trim())) continue;
      if (playlistTrackCount(result) === 1) continue;
      const key = `${tasteText(result.id)}|${tasteText(result.title)}`;
      if (!seen.add(key)) continue;
      charts.push({ plugin, result });
      if (seen.size >= 8) break;
    }
  }
  await Promise.all(charts.map(async (entry, index) => {
    if (entry.result.coverUrl.trim()) return;
    const songs = await runSafely(pluginGetPlaylistDetail(entry.plugin, entry.result.rawData, 1), 9000);
    const cover = songs?.find(song => song.coverUrl.trim())?.coverUrl;
    if (cover) charts[index] = { ...entry, result: { ...entry.result, coverUrl: cover } };
  }));
  return charts;
};

const generateExploreData = async (
  seeds: Song[],
  playlistNames: string[],
  plugins: PluginSource[],
  pluginKey: string,
  tasteKey: string,
): Promise<ExploreData> => {
  if (plugins.length === 0) return { songs: [], playlists: [], charts: [] };
  const profile = buildTasteProfile(seeds, playlistNames);
  const queries = buildQueryList(profile);
  const seedKeys = new Set(seeds.map(song => `${tasteText(song.title)}|${tasteText(song.artist)}`));
  const pluginNames = new Map(plugins.map(plugin => [plugin.id, plugin.name]));
  const rankedSongs = new Map<string, RankedSong>();
  const rankedPlaylists = new Map<string, RankedPlaylist>();
  const titleQueryKeys = new Set(profile.seeds.filter(song => isUnknownArtist(song.artist)).slice(0, 2).map(song => tasteText(song.title)));

  if (queries.length > 0) {
    const songResponses = await Promise.all(plugins.slice(0, MAX_PLUGINS).flatMap(plugin => queries.map(async query => ({
      plugin,
      query,
      results: await runSafely(pluginSearch(plugin, query, 1, 30), 9000),
    }))));
    for (const { plugin, query, results } of songResponses) {
      if (!results) continue;
      const filterMissingCovers = shouldFilterMissingCovers(results.map(item => item.coverUrl));
      for (const item of results.slice(0, 30)) {
        if (!item.title.trim() || (filterMissingCovers && !item.coverUrl.trim())) continue;
        if (isLikelyNonMusicTitle(item.title) || isLikelyIrrelevantContent(item.title, item.artist, item.album)) continue;
        if (isUnknownArtist(item.artist) && (!titleQueryKeys.has(tasteText(query)) || !(tasteText(item.title).includes(tasteText(query)) || tasteText(query).includes(tasteText(item.title))))) continue;
        const normalized = { ...item, pluginId: item.pluginId || plugin.id };
        const key = songTasteKey(normalized);
        if (!key || seedKeys.has(key)) continue;
        let score = recommendationScore(normalized, profile, query) + Math.floor(Math.random() * 4);
        if (isBilibiliPlugin(plugin)) score -= videoTitlePenalty(item.title);
        const previous = rankedSongs.get(key);
        const previousIsBilibili = previous ? /bilibili|哔哩|b站/i.test(`${previous.song.pluginId} ${pluginNames.get(previous.song.pluginId) ?? ''}`) : false;
        if (!previous || score > previous.score || (score === previous.score && previousIsBilibili && !isBilibiliPlugin(plugin))) rankedSongs.set(key, { song: normalized, score });
      }
    }

    const playlistResponses = await Promise.all(plugins.slice(0, MAX_PLUGINS).flatMap(plugin => queries.map(async query => ({
      plugin,
      query,
      results: await runSafely(pluginPlaylistSearch(plugin, query, 1, { allowFallback: false }), 9000),
    }))));
    for (const { plugin, query, results } of playlistResponses) {
      if (!results) continue;
      const filterMissingCovers = shouldFilterMissingCovers(results.map(item => item.coverUrl));
      for (const item of results.slice(0, 20)) {
        if (!item.title.trim() || (filterMissingCovers && !item.coverUrl.trim())) continue;
        if (playlistTrackCount(item) === 1) continue;
        const key = `${tasteText(item.title)}|${tasteText(item.artist ?? '')}`;
        if (!key || rankedPlaylists.has(key)) continue;
        rankedPlaylists.set(key, {
          plugin,
          result: item,
          score: playlistRecommendationScore(item, query, profile) + Math.floor(Math.random() * 4),
          tasteKey: tasteText(query),
        });
      }
    }
  }

  let songs = selectRecommendedSongs(rankedSongs, plugins);
  if (songs.length > 0) {
    const hotSongs = await loadHotChartSongs(plugins, seedKeys);
    const resultKeys = new Set(songs.map(songTasteKey));
    const hotQueue = hotSongs.filter(song => {
      const key = songTasteKey(song);
      if (!key || resultKeys.has(key)) return false;
      resultKeys.add(key);
      return true;
    });
    if (hotQueue.length > 0) {
      const mixed: PluginSearchResult[] = [];
      let hotIndex = 0;
      for (const song of songs) {
        mixed.push(song);
        if (mixed.length % 4 === 0 && hotIndex < hotQueue.length) mixed.push(hotQueue[hotIndex++]);
        if (mixed.length >= MAX_RECOMMENDATIONS) break;
      }
      while (hotIndex < hotQueue.length && mixed.length < MAX_RECOMMENDATIONS) mixed.push(hotQueue[hotIndex++]);
      songs = mixed;
    }
  } else if (seeds.length === 0) {
    songs = (await loadHotChartSongs(plugins, new Set())).slice(0, MAX_RECOMMENDATIONS);
  }

  const data: ExploreData = {
    songs,
    playlists: selectRecommendedPlaylists(rankedPlaylists),
    charts: await loadChartRecommendations(plugins),
  };
  if (data.songs.length > 0 || data.playlists.length > 0) saveCache({ pluginSignature: pluginKey, tasteSignature: tasteKey, generatedAt: Date.now(), data });
  return data;
};

export async function loadExploreData(seeds: Song[], options: ExploreLoadOptions = {}): Promise<ExploreData> {
  const plugins = enabledMusicFreePlugins();
  const currentPluginKey = pluginSignature(plugins);
  const currentTasteKey = tasteSignature(seeds, options.playlistNames ?? []);
  if (!options.forceRefresh) {
    if (isFreshCache(memoryCache, currentPluginKey, currentTasteKey)) return memoryCache!.data;
    const persisted = readPersistedCache();
    if (isFreshCache(persisted, currentPluginKey, currentTasteKey)) {
      memoryCache = persisted;
      return persisted!.data;
    }
    if (inFlightRequest?.signature === `${currentPluginKey}|${currentTasteKey}`) return inFlightRequest.promise;
  }
  const signature = `${currentPluginKey}|${currentTasteKey}`;
  const request = generateExploreData(seeds, options.playlistNames ?? [], plugins, currentPluginKey, currentTasteKey);
  inFlightRequest = { signature, promise: request };
  void request.then(
    () => { if (inFlightRequest?.promise === request) inFlightRequest = null; },
    () => { if (inFlightRequest?.promise === request) inFlightRequest = null; },
  );
  return request;
}

export async function loadExplorePlaylistSongs(playlist: ExplorePlaylist): Promise<PluginSearchResult[]> {
  const songs: PluginSearchResult[] = [];
  for (let page = 1; page <= 20; page += 1) {
    const next = await runSafely(pluginGetPlaylistDetail(playlist.plugin, playlist.result.rawData, page), 12000);
    if (!next || next.length === 0) break;
    songs.push(...next);
    if (next.length < 30) break;
  }
  return songs;
}

export function pluginResultToSong(item: PluginSearchResult): Song {
  const artists = item.artist
    ? item.artist.split(/[、,/&]/).map(value => value.trim()).filter(Boolean)
    : ['未知歌手'];
  return {
    name: item.title,
    title: item.title,
    path: `plugin://${item.platform}/${item.id}`,
    artist: item.artist || '未知歌手',
    artist_names: artists,
    effective_artist_names: artists,
    album: item.album || '未知专辑',
    album_artist: item.artist || '未知歌手',
    album_key: `${item.album || '未知专辑'}-${item.artist || '未知歌手'}`,
    is_various_artists_album: false,
    collapse_artist_credits: false,
    duration: Math.floor((item.duration || 0) / 1000),
    cover_thumb_path: item.coverUrl || '',
    source_type: 'plugin',
    plugin_id: item.pluginId,
    remote_source_id: `plugin://${item.platform}/${item.id}`,
    rawData: item,
  } as Song;
}
