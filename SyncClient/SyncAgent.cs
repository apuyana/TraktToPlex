using PlexClient.Models;
using PlexClient.Models.Shows;
using SyncClient.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TraktNet;
using TraktNet.Enums;
using TraktNet.Objects.Basic;
using TraktNet.Objects.Get.Collections;
using TraktNet.Objects.Get.Episodes;
using TraktNet.Objects.Get.Movies;
using TraktNet.Objects.Get.Shows;
using TraktNet.Objects.Get.Watched;
using TraktNet.Objects.Post.Syncs.Collection;
using TraktNet.Objects.Post.Syncs.History;
using TraktNet.Requests.Parameters;
using TraktNet.Responses;
using Plex = PlexClient;

namespace SyncClient
{
    /// <summary>
    /// Agent for sync media elements.
    /// </summary>
    public class SyncAgent
    {
        /// <summary>
        /// List of process episodes so far.
        /// </summary>
        private readonly List<EpisodeProcessed> processedEpisodesSync = new List<EpisodeProcessed>(1500);

        /// <summary>
        /// List of process movies so far.
        /// </summary>
        private readonly List<ITraktMovie> processedMovies = new List<ITraktMovie>(200);

        /// <summary>
        /// Cache for the trakt shows.
        /// </summary>
        private readonly Dictionary<uint, ITraktShow> traktShowCache = new Dictionary<uint, ITraktShow>();

        /// <summary>
        /// Batch limit.
        /// </summary>
        private int batchLimit;

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="plexClient">Client to use with all the relevant fields completed.</param>
        /// <param name="traktClient">Client to use with all the relevant fields completed.</param>
        /// <param name="batchLimit">Number of elements to process at the same time.</param>
        public SyncAgent(Plex.Client plexClient, TraktClient traktClient, bool removeFromCollection = false, int batchLimit = 2)
        {
            RemoveFromCollection = removeFromCollection;
            PlexClient = plexClient;
            TraktClient = traktClient;
            this.batchLimit = batchLimit;
        }

        /// <summary>
        /// Plex client to use.
        /// </summary>
        public Plex.Client PlexClient { get; private set; }

        /// <summary>
        /// Trakt client to use.
        /// </summary>
        public TraktClient TraktClient { get; private set; }

        /// <summary>
        /// Flag to check if the extra item should be removed from collection.
        /// </summary>
        protected bool RemoveFromCollection
        {
            get;
            private set;
        }

        /// <summary>
        /// Sync all movies.
        /// </summary>
        /// <returns>Task to await.</returns>
        public async Task SyncMoviesAsync()
        {
            if (processedMovies.Count > 0)
            {
                processedMovies.Clear();
            }

            Task<Movie[]> plexMoviesTask = PlexClient.GetMovies();

            // Get all trakt colleciton.
            Task<TraktListResponse<ITraktCollectionMovie>> traktMoviesTask = TraktClient.Sync.GetCollectionMoviesAsync(new TraktExtendedInfo().SetMetadata());

            Task<TraktListResponse<ITraktWatchedMovie>> traktMoviesWatchedTask = TraktClient.Sync.GetWatchedMoviesAsync(new TraktExtendedInfo().SetMetadata());

            await Task.WhenAll(plexMoviesTask, traktMoviesWatchedTask, traktMoviesTask);

            ITraktCollectionMovie[] traktMovies = traktMoviesTask.Result.ToArray();
            ITraktWatchedMovie[] traktMoviesWatched = traktMoviesWatchedTask.Result.ToArray();
            Movie[] plexMovies = plexMoviesTask.Result;

            // Sync plex movies to trakt.
            if ((traktMoviesWatched != null) && (traktMoviesWatched.Length > 0) &&
                (traktMovies != null) && (traktMovies.Length > 0) &&
                (plexMovies != null) && (plexMovies.Length > 0))
            {
                await ReportProgressAsync($"Total plex movies: {plexMovies.Length}; Total watched Trakt Movies {traktMoviesWatched.Length}");

                int calculatedLimit = plexMovies.Length;
                int i;
                List<Task> batchTasks = new List<Task>(batchLimit);

                // Process plex movies first.
                for (i = 0; i < calculatedLimit; i += batchLimit)
                {
                    if (batchTasks.Count > 0)
                    {
                        batchTasks.Clear();
                    }

                    for (int j = 0; j < batchLimit; j++)
                    {
                        if ((i + j) < calculatedLimit)
                        {
                            batchTasks.Add(ProcessPlexMovieAsync(plexMovie: plexMovies[i + j], traktMoviesWatched: traktMoviesWatched, traktMovies: traktMovies));
                        }
                    }

                    await Task.WhenAll(batchTasks.ToArray());

                    await ReportProgressAsync(string.Empty);
                    await ReportProgressAsync($"------- Processed from: {i} to {i + batchLimit}. Total to process: {calculatedLimit} ----------");
                }
            }

            await ReportProgressAsync($"------- All processed, Plex movies: {plexMovies.Length}; Trakt movies: {traktMovies.Length}; Processed trakt movies found: {processedMovies.Count}");

            if (traktMovies.Length > 0 && processedMovies.Count > 0)
            {
                List<ITraktMovie> deleteQueueMovies = new List<ITraktMovie>(traktMovies.Length);

                for (int i = 0; i < traktMovies.Length; i++)
                {
                    var traktMovieProcessFound = processedMovies.FirstOrDefault(x => HasMatchingId(traktMovies[i].Ids, x.Ids));

                    if (traktMovieProcessFound == null)
                    {
                        // Add to delete list.

                        deleteQueueMovies.Add(traktMovies[i]);
                        await ReportProgressAsync($"------- Movie {traktMovies[i].Title} should be removed from collection");
                    }
                }

                await ReportProgressAsync($"------- Trakt movies to remove from collection: {deleteQueueMovies.Count}");

                if (RemoveFromCollection && deleteQueueMovies.Count > 0)
                {
                    TraktSyncCollectionPostBuilder tsp = new TraktSyncCollectionPostBuilder();
                    tsp.AddMovies(deleteQueueMovies);
                    await TraktClient.Sync.RemoveCollectionItemsAsync(tsp.Build());
                }
            }
        }

        /// <summary>
        /// Sync all tv shows.
        /// </summary>
        /// <returns>Task to await.</returns>
        public async Task SyncTVShowsAsync()
        {
            if (processedEpisodesSync.Count > 0)
            {
                processedEpisodesSync.Clear();
            }

            Task<Show[]> plexShowsTask = PlexClient.GetShows();

            // Get all trakt colleciton.
            Task<TraktListResponse<ITraktCollectionShow>> traktShowsTask = TraktClient.Sync.GetCollectionShowsAsync(new TraktExtendedInfo().SetMetadata());

            Task<TraktListResponse<ITraktWatchedShow>> traktShowsWatchedTask = TraktClient.Sync.GetWatchedShowsAsync(new TraktExtendedInfo().SetMetadata());

            await Task.WhenAll(plexShowsTask, traktShowsWatchedTask, traktShowsTask);

            ITraktCollectionShow[] traktShows = traktShowsTask.Result.ToArray();
            ITraktWatchedShow[] traktShowsWatched = traktShowsWatchedTask.Result.ToArray();
            Show[] plexShows = plexShowsTask.Result;

            // Sync plex movies to trakt.
            if ((traktShowsWatched != null) && (traktShowsWatched.Length > 0) &&
                (traktShows != null) && (traktShows.Length > 0) &&
                (plexShows != null) && (plexShows.Length > 0))
            {
                await ReportProgressAsync($"Total plex shows: {plexShows.Length}; Total watched Trakt Shows {traktShowsWatched.Length}");

                int calculatedLimit = plexShows.Length;
                int i;
                List<Task> batchTasks = new List<Task>(batchLimit);

                // Process plex movies first.
                for (i = 0; i < calculatedLimit; i += batchLimit)
                {
                    if (batchTasks.Count > 0)
                    {
                        batchTasks.Clear();
                    }

                    for (int j = 0; j < batchLimit; j++)
                    {
                        if ((i + j) < calculatedLimit)
                        {
                            batchTasks.Add(ProcessPlexShowAsync(plexShow: plexShows[i + j], traktShowsWatched: traktShowsWatched, traktShows: traktShows));
                        }
                    }

                    await Task.WhenAll(batchTasks.ToArray());

                    await ReportProgressAsync(string.Empty);
                    await ReportProgressAsync($"------- Processed from: {i} to {i + batchLimit}. Total to process: {calculatedLimit} ----------");
                }
            }

            await ReportProgressAsync($"------- All processed, Plex shows: {plexShows.Length}; Trakt shows: {traktShows.Length}; Processed trakt shows found: ");

            if (traktShows.Length > 0 && processedEpisodesSync.Count > 0)
            {
                List<ITraktEpisode> deleteQueueEpisodes = new List<ITraktEpisode>(traktShows.Length);

                for (int i = 0; i < traktShows.Length; i++)
                {
                    foreach (var traktSeason in traktShows[i].CollectionSeasons)
                    {
                        foreach (var traktEpisode in traktSeason.Episodes)
                        {
                            var traktEpisodeProcessFound = processedEpisodesSync.FirstOrDefault(x => (x.ShowTraktId == traktShows[i].Ids.Trakt) && (x.SeasonNumber == traktSeason.Number) && (x.Number == traktEpisode.Number));

                            if (traktEpisodeProcessFound == null)
                            {
                                ITraktEpisode remoteEpisode = await GetTraktEpisodeAsync(showTraktId: traktShows[i].Ids.Trakt, seasonNumber: traktSeason.Number, episodeNumber: traktEpisode.Number);

                                if (remoteEpisode == null)
                                {
                                    try
                                    {
                                        await ReportProgressAsync($"------- Episode \"{traktShows[i].Title}\" - S{traktSeason.Number.Value.ToString("00")}E{traktEpisode.Number.Value.ToString("00")} could not be found in trakt");
                                    }
                                    catch
                                    {
                                        await ReportProgressAsync($"------- Episode from  \"{traktShows[i].Title}\" could not be found in trakt");
                                    }
                                }
                                else
                                {
                                    // Add to delete list.
                                    deleteQueueEpisodes.Add(remoteEpisode);
                                }

                                try
                                {
                                    await ReportProgressAsync($"------- Episode \"{traktShows[i].Title}\" - S{traktSeason.Number.Value.ToString("00")}E{traktEpisode.Number.Value.ToString("00")} should be removed from collection");
                                }
                                catch
                                {
                                    await ReportProgressAsync($"------- Episode from  \"{traktShows[i].Title}\" should be removed from collection");
                                }
                            }
                        }
                    }
                }

                await ReportProgressAsync($"------- Trakt movies to remove from collection: {deleteQueueEpisodes.Count}");

                if (RemoveFromCollection && deleteQueueEpisodes.Count > 0)
                {
                    TraktSyncCollectionPostBuilder tsp = new TraktSyncCollectionPostBuilder();
                    tsp.AddEpisodes(deleteQueueEpisodes);
                    await TraktClient.Sync.RemoveCollectionItemsAsync(tsp.Build());
                }
            }
        }



        /// <summary>
        /// Get Trakt episode from id.
        /// </summary>
        /// <param name="traktEpisodeProcessFound">Processed episode.</param>
        /// <returns>Task to await.</returns>
        private async Task<ITraktEpisode> GetTraktEpisodeAsync(uint showTraktId, int? seasonNumber, int? episodeNumber)
        {
            ITraktEpisode foundEpisode = null;
            ITraktShow traktShow = await GetTraktShowByIdAsync(showTraktId);

            if (traktShow != null && traktShow.Seasons != null)
            {
                var traktSeason = traktShow.Seasons.Where(x => x.Number == seasonNumber).FirstOrDefault();

                if (traktSeason != null && traktSeason.Episodes != null)
                {
                    foundEpisode = traktSeason.Episodes.Where(x => x.Number == episodeNumber).FirstOrDefault();
                }
            }

            return foundEpisode;
        }

        /// <summary>
        /// Get trakt show by id.
        /// </summary>
        /// <param name="showTraktId">Id to use.</param>
        /// <returns>Task to await.</returns>
        private async Task<ITraktShow> GetTraktShowByIdAsync(uint showTraktId)
        {
            ITraktShow foundShow = null;

            try
            {
                if (!traktShowCache.TryGetValue(key: showTraktId, value: out foundShow))
                {
                    var traktRemoteShowResponse = await TraktClient.Shows.GetShowAsync(showIdOrSlug: showTraktId.ToString(), extendedInfo: new TraktExtendedInfo().SetMetadata());

                    if (traktRemoteShowResponse != null && traktRemoteShowResponse.IsSuccess)
                    {
                        foundShow = traktRemoteShowResponse.Value;

                        if (foundShow != null)
                        {
                            var traktRemoteSeasonResponse = await TraktClient.Seasons.GetAllSeasonsAsync(showIdOrSlug: showTraktId.ToString(), extendedInfo: new TraktExtendedInfo().SetEpisodes().SetMetadata());
                            if (traktRemoteSeasonResponse != null && traktRemoteSeasonResponse.IsSuccess)
                            {
                                foundShow.Seasons = traktRemoteSeasonResponse.Value;
                            }
                        }
                    }

                    if (foundShow != null)
                    {
                        traktShowCache.Add(key: showTraktId, value: foundShow);
                    }
                }
            }
            catch (Exception ex)
            {
                await ReportProgressAsync($"Problem query showId {showTraktId}. Error: {ex.Message}");
            }

            return foundShow;
        }

        /// <summary>
        /// Predicate to match a movie.
        /// </summary>
        /// <param name="plexItem">Plex item.</param>
        /// <param name="traktIds">Trakt Ids.</param>
        /// <returns>True if match.</returns>
        private bool HasMatchingId(IMediaItem plexItem, ITraktMovieIds traktIds)
        {
            switch (plexItem.ExternalProvider)
            {
                case "imdb":
                    return plexItem.ExternalProviderId.Equals(traktIds.Imdb);

                case "tmdb":
                    return uint.TryParse(plexItem.ExternalProviderId, out var tmdbId) && tmdbId.Equals(traktIds.Tmdb);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Predicate to match a movie.
        /// </summary>
        /// <param name="plexItem">Plex item.</param>
        /// <param name="traktIds">Trakt Ids.</param>
        /// <returns>True if match.</returns>
        private bool HasMatchingId(IMediaItem plexItem, ITraktIds traktIds)
        {
            switch (plexItem.ExternalProvider)
            {
                case "imdb":
                    return plexItem.ExternalProviderId.Equals(traktIds.Imdb);

                case "tmdb":
                    return uint.TryParse(plexItem.ExternalProviderId, out var tmdbId) && tmdbId.Equals(traktIds.Tmdb);

                case "thetvdb":
                    return uint.TryParse(plexItem.ExternalProviderId, out var tvdbId) && tvdbId.Equals(traktIds.Tvdb);

                case "tvrage":
                    return uint.TryParse(plexItem.ExternalProviderId, out var tvrageId) && tvrageId.Equals(traktIds.TvRage);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Predicate to match a movie.
        /// </summary>
        /// <param name="traktIds1">Trakt Ids.</param>
        /// <param name="traktIds2">Trakt Ids.</param>
        /// <returns>True if match.</returns>
        private bool HasMatchingId(ITraktMovieIds traktIds1, ITraktMovieIds traktIds2)
        {
            return traktIds1.HasAnyId && traktIds2.HasAnyId && traktIds1.Trakt == traktIds2.Trakt;
        }

        /// <summary>
        /// Predicate to match a movie.
        /// </summary>
        /// <param name="traktIds1">Trakt Ids.</param>
        /// <param name="traktIds2">Trakt Ids.</param>
        /// <returns>True if match.</returns>
        private bool HasMatchingId(ITraktEpisodeIds traktIds1, ITraktEpisodeIds traktIds2)
        {
            return traktIds1.HasAnyId && traktIds2.HasAnyId && traktIds1.Trakt == traktIds2.Trakt;
        }

        /// <summary>
        /// Predicate to match a movie.
        /// </summary>
        /// <param name="traktIds1">Trakt Ids.</param>
        /// <param name="traktIds2">Trakt Ids.</param>
        /// <returns>True if match.</returns>
        private bool HasMatchingId(ITraktIds traktIds1, ITraktIds traktIds2)
        {
            return traktIds1.HasAnyId && traktIds2.HasAnyId && traktIds1.Trakt == traktIds2.Trakt;
        }

        /// <summary>
        /// Process single moview.
        /// </summary>
        /// <param name="plexMovie">Plex movie to sync.</param>
        /// <param name="traktMovies">All movie collection.</param>
        /// <param name="traktMoviesWatched">All Trakt movies to search.</param>
        /// <returns>Task to await.</returns>
        private async Task ProcessPlexMovieAsync(Movie plexMovie, ITraktCollectionMovie[] traktMovies, ITraktWatchedMovie[] traktMoviesWatched)
        {
            var traktMovieWatched = traktMoviesWatched.FirstOrDefault(x => HasMatchingId(plexMovie, x.Ids));
            if (traktMovieWatched == null)
            {
                await ReportProgressAsync($"The movie \"{plexMovie.Title}\" was not found as watched on Trakt.");

                var traktMovie = traktMovies.FirstOrDefault(x => HasMatchingId(plexMovie, x.Ids));

                if (traktMovie != null)
                {
                    // Add to the processed list to find extra items.
                    processedMovies.Add(traktMovie);
                }

                if (plexMovie.ViewCount > 0)
                {
                    if (traktMovie == null)
                    {
                        await ReportProgressAsync($"The movie \"{plexMovie.Title}\" was not found on Trakt.");

                        try
                        {
                            if (plexMovie.ExternalProvider == "imdb")
                            {
                                var traktRemoteSearchResponse = await TraktClient.Search.GetIdLookupResultsAsync(searchIdType: TraktSearchIdType.ImDB, lookupId: plexMovie.ExternalProviderId, extendedInfo: new TraktExtendedInfo().SetMetadata());

                                if (traktRemoteSearchResponse != null && traktRemoteSearchResponse.IsSuccess)
                                {
                                    // Found it and is not watched, lets update this.
                                    TraktSyncHistoryPostBuilder syncHistory = new TraktSyncHistoryPostBuilder();
                                    TraktSyncCollectionPostBuilder syncCollection = new TraktSyncCollectionPostBuilder();

                                    var traktMovieRemote = traktRemoteSearchResponse.Value.FirstOrDefault();

                                    if (traktMovieRemote != null && traktMovieRemote.Movie != null)
                                    {
                                        syncCollection.AddMovie(traktMovieRemote.Movie);
                                        await TraktClient.Sync.AddCollectionItemsAsync(syncCollection.Build());

                                        syncHistory.AddMovie(traktMovieRemote.Movie);
                                        await TraktClient.Sync.AddWatchedHistoryItemsAsync(syncHistory.Build());
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {
                            await ReportProgressAsync($"Moview \"{plexMovie.Title}\" Could not be added to trakt");
                        }
                    }
                    else
                    {
                        // Found it and is not watched, lets update this.
                        TraktSyncHistoryPostBuilder spb = new TraktSyncHistoryPostBuilder();

                        spb.AddMovie(traktMovie);

                        await TraktClient.Sync.AddWatchedHistoryItemsAsync(spb.Build());

                        await ReportProgressAsync($"The movie \"{plexMovie.Title}\" was set as watched on Trakt");
                    }
                }
                else
                {
                    await ReportProgressAsync($"The movie \"{plexMovie.Title}\" was not watched on Trakt or plex.");

                    if (traktMovie == null)
                    {
                        await ReportProgressAsync($"The movie \"{plexMovie.Title}\" was not found on Trakt.");

                        try
                        {
                            if (plexMovie.ExternalProvider == "imdb")
                            {
                                var traktRemoteSearchResponse = await TraktClient.Search.GetIdLookupResultsAsync(searchIdType: TraktSearchIdType.ImDB, lookupId: plexMovie.ExternalProviderId, extendedInfo: new TraktExtendedInfo().SetMetadata());

                                if (traktRemoteSearchResponse != null && traktRemoteSearchResponse.IsSuccess)
                                {
                                    // Found it and is not watched, lets update this.
                                    TraktSyncHistoryPostBuilder syncHistory = new TraktSyncHistoryPostBuilder();
                                    TraktSyncCollectionPostBuilder syncCollection = new TraktSyncCollectionPostBuilder();

                                    var traktMovieRemote = traktRemoteSearchResponse.Value.FirstOrDefault();

                                    if (traktMovieRemote != null && traktMovieRemote.Movie != null)
                                    {
                                        syncCollection.AddMovie(traktMovieRemote.Movie);
                                        await TraktClient.Sync.AddCollectionItemsAsync(syncCollection.Build());
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {
                            await ReportProgressAsync($"Moview \"{plexMovie.Title}\" Could not be added to trakt");
                        }
                    }
                }
            }
            else
            {
                // Add to the processed list to find extra items.
                processedMovies.Add(traktMovieWatched);

                await ReportProgressAsync($"Found the movie \"{plexMovie.Title}\" as watched on Trakt. Processing!");

                if (plexMovie.ViewCount > 0)
                {
                    await ReportProgressAsync($"Movie \"{plexMovie.Title}\" has {plexMovie.ViewCount} views on Plex. Nothing more to do");
                }
                else
                {
                    await PlexClient.Scrobble(plexMovie);
                    await ReportProgressAsync($"Marking {plexMovie.Title} as watched.");
                }
            }
        }

        /// <summary>
        /// Process plex show.
        /// </summary>
        /// <param name="plexShow">Show to use.</param>
        /// <param name="traktShowsWatched">List of trakt watched shows.</param>
        /// <param name="traktShows">List of all trakt shows.</param>
        /// <returns>Task to await.</returns>
        private async Task ProcessPlexShowAsync(Show plexShow, ITraktWatchedShow[] traktShowsWatched, ITraktCollectionShow[] traktShows)
        {
            if (plexShow.ExternalProvider.Equals("themoviedb"))
            {
                await ReportProgressAsync($"Skipping {plexShow.Title} since it's configured to use TheMovieDb agent for metadata. This agent isn't supported, as Trakt doesn't have TheMovieDb ID's.");
            }
            else
            {
                var traktShow = traktShows.FirstOrDefault(x => HasMatchingId(plexShow, x.Ids));
                if (traktShow == null)
                {
                    await ReportProgressAsync($"The show \"{plexShow.Title}\" was not found on Trakt. Adding");

                    try
                    {
                        if (plexShow.ExternalProvider == "thetvdb")
                        {
                            var traktRemoteSearchResponse = await TraktClient.Search.GetIdLookupResultsAsync(searchIdType: TraktSearchIdType.TvDB, lookupId: plexShow.ExternalProviderId, extendedInfo: new TraktExtendedInfo().SetMetadata());

                            if (traktRemoteSearchResponse != null && traktRemoteSearchResponse.IsSuccess)
                            {
                                // Found it and is not watched, lets update this.
                                TraktSyncHistoryPostBuilder syncHistory = new TraktSyncHistoryPostBuilder();
                                TraktSyncCollectionPostBuilder syncCollection = new TraktSyncCollectionPostBuilder();

                                var traktShowRemote = traktRemoteSearchResponse.Value.FirstOrDefault();

                                if (traktShowRemote != null && traktShowRemote.Show != null)
                                {
                                    syncCollection.AddShow(traktShowRemote.Show);
                                    await TraktClient.Sync.AddCollectionItemsAsync(syncCollection.Build());
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        await ReportProgressAsync($"Moview \"{plexShow.Title}\" Could not be added to trakt");
                    }
                }

                if (traktShow != null)
                {
                    var traktShowWatched = traktShowsWatched.FirstOrDefault(x => HasMatchingId(plexShow, x.Ids));
                    await PlexClient.PopulateSeasons(plexShow);

                    foreach (var plexSeason in plexShow.Seasons)
                    {
                        ITraktWatchedShowSeason traktSeasonWatched = null;

                        if (traktShowWatched != null)
                        {
                            traktSeasonWatched = traktShowWatched.WatchedSeasons.Where(x => x.Number == plexSeason.No).FirstOrDefault();
                        }

                        ITraktCollectionShowSeason traktSeasonCollected = traktShow.CollectionSeasons.Where(x => x.Number == plexSeason.No).FirstOrDefault();

                        await PlexClient.PopulateEpisodes(plexSeason);

                        if (traktSeasonCollected != null)
                        {
                            foreach (var plexEpisode in plexSeason.Episodes)
                            {
                                ITraktWatchedShowEpisode traktEpisodeWatched = null;
                                ITraktCollectionShowEpisode traktEpisodeCollected = null;

                                if (traktSeasonWatched != null)
                                {
                                    traktEpisodeWatched = traktSeasonWatched.Episodes.Where(x => x.Number == plexEpisode.No).FirstOrDefault();
                                }

                                traktEpisodeCollected = traktSeasonCollected.Episodes.Where(x => x.Number == plexEpisode.No).FirstOrDefault();

                                if (traktEpisodeCollected != null)
                                {
                                    processedEpisodesSync.Add(
                                        new EpisodeProcessed()
                                        {
                                            ShowTraktId = traktShow.Ids.Trakt,
                                            SeasonNumber = traktSeasonCollected.Number,
                                            Number = traktEpisodeCollected.Number
                                        });
                                }

                                if (plexEpisode.ViewCount > 0 && traktEpisodeWatched == null)
                                {
                                    // Scrobble to trakt
                                    await ReportProgressAsync($"Show \"{plexShow.Title}\" - S{plexSeason.No.ToString("00")}E{plexEpisode.No.ToString("00")} has {plexEpisode.ViewCount } views on Plex. Mark as watched on trakt");

                                    try
                                    {
                                        var traktRemoteEpisodeResponse = await TraktClient.Episodes.GetEpisodeAsync(showIdOrSlug: traktShow.Ids.Trakt.ToString(), seasonNumber: Convert.ToUInt32(plexSeason.No), episodeNumber: Convert.ToUInt32(plexEpisode.No), extendedInfo: new TraktExtendedInfo().SetMetadata());

                                        if (traktRemoteEpisodeResponse != null && traktRemoteEpisodeResponse.IsSuccess)
                                        {
                                            // Found it and is not watched, lets update this.
                                            TraktSyncHistoryPostBuilder spb = new TraktSyncHistoryPostBuilder();
                                            spb.AddEpisode(traktRemoteEpisodeResponse.Value);
                                            await TraktClient.Sync.AddWatchedHistoryItemsAsync(spb.Build());

                                            processedEpisodesSync.Add(
                                                new EpisodeProcessed()
                                                {
                                                    ShowTraktId = traktShow.Ids.Trakt,
                                                    SeasonNumber = traktSeasonCollected.Number,
                                                    Number = traktRemoteEpisodeResponse.Value.Number
                                                });
                                        }
                                    }
                                    catch (Exception)
                                    {
                                        await ReportProgressAsync($"Show \"{plexShow.Title}\" - S{plexSeason.No.ToString("00")}E{plexEpisode.No.ToString("00")} Could not be added to trakt");
                                    }
                                }
                                else if ((traktEpisodeWatched != null) && (traktEpisodeWatched.Plays > 0) && (plexEpisode.ViewCount == 0))
                                {
                                    // Scroble to plex.
                                    await ReportProgressAsync($"Show \"{plexShow.Title}\" - S{plexSeason.No.ToString("00")}E{plexEpisode.No.ToString("00")} has {traktEpisodeWatched.Plays } views on Trakt. Mark as watched on plex");

                                    await PlexClient.Scrobble(plexEpisode);
                                }
                                else if ((traktEpisodeWatched != null) && (traktEpisodeWatched.Plays > 0) && (plexEpisode.ViewCount > 0))
                                {
                                    await ReportProgressAsync($"Show \"{plexShow.Title}\" - S{plexSeason.No.ToString("00")}E{plexEpisode.No.ToString("00")} has {plexEpisode.ViewCount } views on Plex. Nothing more to do");
                                }
                            }
                        }
                        else
                        {
                            await ReportProgressAsync($"Show \"{plexShow.Title}\" - S{plexSeason.No.ToString("00")} Not found in trakt. Add to collection");

                            try
                            {
                                var traktRemoteSeasonResponse = await TraktClient.Seasons.GetSeasonAsync(showIdOrSlug: traktShow.Ids.Trakt.ToString(), seasonNumber: Convert.ToUInt32(plexSeason.No), extendedInfo: new TraktExtendedInfo().SetMetadata());

                                if (traktRemoteSeasonResponse != null && traktRemoteSeasonResponse.IsSuccess)
                                {
                                    // Found it and is not watched, lets update this.
                                    TraktSyncHistoryPostBuilder syncHistory = new TraktSyncHistoryPostBuilder();
                                    TraktSyncCollectionPostBuilder syncCollection = new TraktSyncCollectionPostBuilder();

                                    var traktEpisodes = traktRemoteSeasonResponse.Value;

                                    foreach (var plexEpisode in plexSeason.Episodes)
                                    {
                                        var traktEpisode = traktEpisodes.Where(x => x.Number == plexEpisode.No).FirstOrDefault();

                                        if (traktEpisode != null)
                                        {
                                            syncCollection.AddEpisode(traktEpisode);

                                            processedEpisodesSync.Add(
                                                new EpisodeProcessed()
                                                {
                                                    ShowTraktId = traktShow.Ids.Trakt,
                                                    SeasonNumber = plexSeason.No,
                                                    Number = traktEpisode.Number
                                                });

                                            if (plexEpisode.ViewCount > 0)
                                            {
                                                syncHistory.AddEpisode(traktEpisode);
                                            }
                                        }
                                    }

                                    await TraktClient.Sync.AddCollectionItemsAsync(syncCollection.Build());
                                    await TraktClient.Sync.AddWatchedHistoryItemsAsync(syncHistory.Build());
                                }
                            }
                            catch (Exception)
                            {
                                await ReportProgressAsync($"Show \"{plexShow.Title}\" - S{plexSeason.No.ToString("00")} Could not be added to trakt");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Wrapper for reporting progress.
        /// </summary>
        /// <param name="progress">Progress to report.</param>
        /// <returns>Task to await.</returns>
        private Task ReportProgressAsync(string progress)
        {
            return Task.Run(
                () =>
                {
                    Console.WriteLine(progress);
                });
        }
    }
}