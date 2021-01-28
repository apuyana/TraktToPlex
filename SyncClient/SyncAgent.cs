using PlexClient.Models;
using PlexClient.Models.Shows;
using SyncClient.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TraktNet;
using TraktNet.Enums;
using TraktNet.Objects.Basic;
using TraktNet.Objects.Get.Collections;
using TraktNet.Objects.Get.Episodes;
using TraktNet.Objects.Get.Movies;
using TraktNet.Objects.Get.Shows;
using TraktNet.Objects.Get.Watched;
using TraktNet.Objects.Post;
using TraktNet.Objects.Post.Builder;
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
        /// Process id constant.
        /// </summary>
        public const int PROCESS_ID_MOVIES = 1;

        /// <summary>
        /// Process id constant.
        /// </summary>
        public const int PROCESS_ID_TVSHOWS = 2;

        /// <summary>
        /// Process name constant.
        /// </summary>
        public const string PROCESS_MOVIES = "Movies";

        /// <summary>
        /// Process name constant.
        /// </summary>
        public const string PROCESS_TVSHOWS = "TV Shows";

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
        /// Delegate to report progress.
        /// </summary>
        public Func<IProgressReport, Task> ReportProgressDelegate { get; set; }

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
        public async Task SyncMoviesAsync(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (processedMovies.Count > 0)
            {
                processedMovies.Clear();
            }

            Task<Movie[]> plexMoviesTask = PlexClient.GetMovies();

            // Get all trakt collection.
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
                await ReportProgressAsync(new ProgressReportMovie()
                {
                    Id = PROCESS_ID_MOVIES,
                    Name = PROCESS_MOVIES,
                    ItemName = "Summary",
                    Message = $"Total plex movies: {plexMovies.Length}; Total watched Trakt Movies {traktMoviesWatched.Length}",
                    Status = ProgressStatus.Message
                });

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
                            try
                            {
                                batchTasks.Add(ProcessPlexMovieAsync(plexMovie: plexMovies[i + j], traktMoviesWatched: traktMoviesWatched, traktMovies: traktMovies, itemCount: i + j, totalCount: calculatedLimit, token: token));
                            }
                            catch (Exception)
                            {
                                await ReportProgressAsync(new ProgressReportMovie()
                                {
                                    Id = PROCESS_ID_MOVIES,
                                    Name = PROCESS_MOVIES,
                                    ItemName = plexMovies[i + j].Title,
                                    Year = plexMovies[i + j].Year,
                                    CurrentItemCount = i + j,
                                    TotalItemsCount = calculatedLimit,
                                    Status = ProgressStatus.ErrorAddRemote
                                });
                            }
                        }
                    }

                    await Task.WhenAll(batchTasks.ToArray());

                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }

            await ReportProgressAsync(new ProgressReportMovie()
            {
                Id = PROCESS_ID_MOVIES,
                Name = PROCESS_MOVIES,
                ItemName = "Summary",
                Message = $"All processed, Plex movies: {plexMovies.Length}; Trakt movies: {traktMovies.Length}; Processed trakt movies found: {processedMovies.Count}",
                Status = ProgressStatus.Message
            });

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
                        await ReportProgressAsync(new ProgressReportMovie()
                        {
                            Id = PROCESS_ID_MOVIES,
                            Name = PROCESS_MOVIES,
                            ItemName = traktMovies[i].Title,
                            CurrentItemCount = i,
                            TotalItemsCount = traktMovies.Length,
                            Year = traktMovies[i].Year,
                            Status = ProgressStatus.Remove,
                        });

                        if (token.IsCancellationRequested)
                        {
                            return;
                        }
                    }
                }

                await ReportProgressAsync(new ProgressReportMovie()
                {
                    Id = PROCESS_ID_MOVIES,
                    Name = PROCESS_MOVIES,
                    Status = ProgressStatus.Message,
                    Message = $"------- Trakt movies to remove from collection: {deleteQueueMovies.Count}",
                });

                if (RemoveFromCollection && deleteQueueMovies.Count > 0)
                {
                    ITraktSyncCollectionPostBuilder tsp = TraktPost.NewSyncCollectionPost();

                    for (int i = 0; i < deleteQueueMovies.Count; i++)
                    {
                        tsp.WithMovie(deleteQueueMovies[i]);
                        tsp.AddMovieAndMetadata(deleteQueueMovies[i]);
                    }

                    await TraktClient.Sync.RemoveCollectionItemsAsync(tsp.Build());
                }
            }
        }

        /// <summary>
        /// Sync all TV shows.
        /// </summary>
        /// <returns>Task to await.</returns>
        public async Task SyncTVShowsAsync(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (processedEpisodesSync.Count > 0)
            {
                processedEpisodesSync.Clear();
            }

            Task<Show[]> plexShowsTask = PlexClient.GetShows();

            // Get all trakt collection.
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
                            batchTasks.Add(ProcessPlexShowAsync(plexShow: plexShows[i + j], traktShowsWatched: traktShowsWatched, traktShows: traktShows, itemCount: i + j, totalCount: calculatedLimit, token: token));
                        }
                    }

                    await Task.WhenAll(batchTasks.ToArray());

                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }

            await ReportProgressAsync(new ProgressReportTVShow()
            {
                Id = PROCESS_ID_TVSHOWS,
                Name = PROCESS_TVSHOWS,
                ItemName = "Summary",
                Message = $"------- All processed, Plex shows: {plexShows.Length}; Trakt shows: {traktShows.Length}; Processed trakt shows found: ",
                Status = ProgressStatus.Message
            });

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

                                if (remoteEpisode != null)
                                {
                                    // Add to delete list.
                                    deleteQueueEpisodes.Add(remoteEpisode);
                                }

                                try
                                {
                                    await ReportProgressAsync(new ProgressReportTVShow()
                                    {
                                        Id = PROCESS_ID_TVSHOWS,
                                        Name = PROCESS_TVSHOWS,
                                        ItemName = traktShows[i].Title,
                                        Season = traktSeason.Number.Value,
                                        Episode = traktEpisode.Number.Value,
                                        CurrentItemCount = i,
                                        TotalItemsCount = traktShows.Length,
                                        Status = ProgressStatus.ShouldRemove
                                    });
                                }
                                catch
                                {
                                    await ReportProgressAsync(new ProgressReportTVShow()
                                    {
                                        Id = PROCESS_ID_TVSHOWS,
                                        Name = PROCESS_TVSHOWS,
                                        ItemName = traktShows[i].Title,
                                        CurrentItemCount = i,
                                        TotalItemsCount = traktShows.Length,
                                        Status = ProgressStatus.ShouldRemove
                                    });
                                }
                            }

                            if (token.IsCancellationRequested)
                            {
                                return;
                            }
                        }
                    }
                }

                await ReportProgressAsync(new ProgressReportTVShow()
                {
                    Id = PROCESS_ID_TVSHOWS,
                    Name = PROCESS_TVSHOWS,
                    ItemName = "Summary",
                    Message = $"------- Trakt episodes to remove from collection: {deleteQueueEpisodes.Count}",
                    Status = ProgressStatus.Message
                });

                if (RemoveFromCollection && deleteQueueEpisodes.Count > 0)
                {
                    ITraktSyncCollectionPostBuilder tsp = TraktPost.NewSyncCollectionPost();

                    for (int i = 0; i < deleteQueueEpisodes.Count; i++)
                    {
                        tsp.WithEpisode(deleteQueueEpisodes[i]);
                        tsp.AddEpisodeAndMetadata(deleteQueueEpisodes[i]);
                    }

                    await TraktClient.Sync.RemoveCollectionItemsAsync(tsp.Build());

                    for (int i = 0; i < deleteQueueEpisodes.Count; i++)
                    {
                        await ReportProgressAsync(new ProgressReportTVShow()
                        {
                            Id = PROCESS_ID_TVSHOWS,
                            Name = PROCESS_TVSHOWS,
                            ItemName = deleteQueueEpisodes[i].Title,
                            Season = deleteQueueEpisodes[i].SeasonNumber,
                            Episode = deleteQueueEpisodes[i].Number,
                            CurrentItemCount = i,
                            TotalItemsCount = traktShows.Length,
                            Status = ProgressStatus.Remove
                        });
                    }
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
                await ReportProgressAsync(new ProgressReportTVShow()
                {
                    Id = PROCESS_ID_TVSHOWS,
                    Name = PROCESS_TVSHOWS,
                    Message = $"Problem query showId {showTraktId}. Error: {ex.Message}",
                    Status = ProgressStatus.Message
                });
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
        /// Process single movie.
        /// </summary>
        /// <param name="plexMovie">Plex movie to sync.</param>
        /// <param name="traktMovies">All movie collection.</param>
        /// <param name="traktMoviesWatched">All Trakt movies to search.</param>
        /// <returns>Task to await.</returns>
        private async Task ProcessPlexMovieAsync(Movie plexMovie, ITraktCollectionMovie[] traktMovies, ITraktWatchedMovie[] traktMoviesWatched, int itemCount, int totalCount, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            var traktMovieWatched = traktMoviesWatched.FirstOrDefault(x => HasMatchingId(plexMovie, x.Ids));
            if (traktMovieWatched == null)
            {
                await ReportProgressAsync(new ProgressReportMovie()
                {
                    Id = PROCESS_ID_MOVIES,
                    Name = PROCESS_MOVIES,
                    ItemName = plexMovie.Title,
                    Year = plexMovie.Year,
                    CurrentItemCount = itemCount,
                    TotalItemsCount = totalCount,
                    Status = ProgressStatus.NotWatchedRemote
                });

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
                        await ReportProgressAsync(new ProgressReportMovie()
                        {
                            Id = PROCESS_ID_MOVIES,
                            Name = PROCESS_MOVIES,
                            ItemName = plexMovie.Title,
                            Year = plexMovie.Year,
                            CurrentItemCount = itemCount,
                            TotalItemsCount = totalCount,
                            Status = ProgressStatus.NotFoundRemote
                        });

                        try
                        {
                            TraktSearchIdType searchType = SupportedSearchIdMovie(plexMovie.ExternalProvider);

                            if (searchType != null)
                            {
                                var traktRemoteSearchResponse = await TraktClient.Search.GetIdLookupResultsAsync(searchIdType: searchType, lookupId: plexMovie.ExternalProviderId, extendedInfo: new TraktExtendedInfo().SetMetadata());
                                await Task.Delay(TimeSpan.FromSeconds(.15));

                                if (traktRemoteSearchResponse != null && traktRemoteSearchResponse.IsSuccess)
                                {
                                    // Found it and is not watched, lets update this.

                                    var syncHistory = TraktPost.NewSyncHistoryPost();
                                    var syncCollection = TraktPost.NewSyncCollectionPost();

                                    var traktMovieRemote = traktRemoteSearchResponse.Value.FirstOrDefault();

                                    if (traktMovieRemote != null && traktMovieRemote.Movie != null)
                                    {
                                        syncCollection.WithMovie(traktMovieRemote.Movie);
                                        syncCollection.AddCollectedMovie(traktMovieRemote.Movie);
                                        await TraktClient.Sync.AddCollectionItemsAsync(syncCollection.Build());

                                        syncHistory.WithMovie(traktMovieRemote.Movie);
                                        syncHistory.AddWatchedMovie(traktMovieRemote.Movie);
                                        await TraktClient.Sync.AddWatchedHistoryItemsAsync(syncHistory.Build());
                                        await Task.Delay(TimeSpan.FromSeconds(.15));

                                        await ReportProgressAsync(new ProgressReportMovie()
                                        {
                                            Id = PROCESS_ID_MOVIES,
                                            Name = PROCESS_MOVIES,
                                            ItemName = plexMovie.Title,
                                            Year = plexMovie.Year,
                                            CurrentItemCount = itemCount,
                                            TotalItemsCount = totalCount,
                                            Status = ProgressStatus.AddRemote
                                        });
                                    }
                                }
                            }
                            else
                            {
                                await ReportProgressAsync(new ProgressReportMovie()
                                {
                                    Id = PROCESS_ID_MOVIES,
                                    Name = PROCESS_MOVIES,
                                    ItemName = plexMovie.Title,
                                    Year = plexMovie.Year,
                                    CurrentItemCount = itemCount,
                                    TotalItemsCount = totalCount,
                                    Status = ProgressStatus.NotFoundRemote
                                });
                            }
                        }
                        catch (Exception)
                        {
                            await ReportProgressAsync(new ProgressReportMovie()
                            {
                                Id = PROCESS_ID_MOVIES,
                                Name = PROCESS_MOVIES,
                                ItemName = plexMovie.Title,
                                Year = plexMovie.Year,
                                CurrentItemCount = itemCount,
                                TotalItemsCount = totalCount,
                                Status = ProgressStatus.ErrorAddRemote
                            });
                        }
                    }
                    else
                    {
                        try
                        {
                            // Found it and is not watched, lets update this.
                            var spb = TraktPost.NewSyncHistoryPost();

                            spb.WithMovie(traktMovie);
                            spb.AddWatchedMovie(traktMovie);

                            await TraktClient.Sync.AddWatchedHistoryItemsAsync(spb.Build());
                            await Task.Delay(TimeSpan.FromSeconds(.15));

                            await ReportProgressAsync(new ProgressReportMovie()
                            {
                                Id = PROCESS_ID_MOVIES,
                                Name = PROCESS_MOVIES,
                                ItemName = plexMovie.Title,
                                Year = plexMovie.Year,
                                CurrentItemCount = itemCount,
                                TotalItemsCount = totalCount,
                                Status = ProgressStatus.WatchedRemote
                            });
                        }
                        catch (Exception ex)
                        {
                            await ReportProgressAsync(new ProgressReportMovie()
                            {
                                Id = PROCESS_ID_MOVIES,
                                Name = PROCESS_MOVIES,
                                ItemName = plexMovie.Title,
                                Year = plexMovie.Year,
                                CurrentItemCount = itemCount,
                                TotalItemsCount = totalCount,
                                Status = ProgressStatus.ErrorAddRemote
                            });
                        }
                    }
                }
                else
                {
                    if (traktMovie == null)
                    {
                        await ReportProgressAsync(new ProgressReportMovie()
                        {
                            Id = PROCESS_ID_MOVIES,
                            Name = PROCESS_MOVIES,
                            ItemName = plexMovie.Title,
                            Year = plexMovie.Year,
                            CurrentItemCount = itemCount,
                            TotalItemsCount = totalCount,
                            Status = ProgressStatus.NotFoundRemote
                        });

                        try
                        {
                            TraktSearchIdType searchType = SupportedSearchIdMovie(plexMovie.ExternalProvider);

                            if (searchType != null)
                            {
                                var traktRemoteSearchResponse = await TraktClient.Search.GetIdLookupResultsAsync(searchIdType: searchType, lookupId: plexMovie.ExternalProviderId, extendedInfo: new TraktExtendedInfo().SetMetadata());
                                await Task.Delay(TimeSpan.FromSeconds(.15));

                                if (traktRemoteSearchResponse != null && traktRemoteSearchResponse.IsSuccess)
                                {
                                    var syncCollection = TraktPost.NewSyncCollectionPost();

                                    var traktMovieRemote = traktRemoteSearchResponse.Value.FirstOrDefault();

                                    if (traktMovieRemote != null && traktMovieRemote.Movie != null)
                                    {
                                        syncCollection.WithMovie(traktMovieRemote.Movie);
                                        syncCollection.AddCollectedMovie(traktMovieRemote.Movie);
                                        await TraktClient.Sync.AddCollectionItemsAsync(syncCollection.Build());
                                        await Task.Delay(TimeSpan.FromSeconds(.15));

                                        await ReportProgressAsync(new ProgressReportMovie()
                                        {
                                            Id = PROCESS_ID_MOVIES,
                                            Name = PROCESS_MOVIES,
                                            ItemName = plexMovie.Title,
                                            Year = plexMovie.Year,
                                            CurrentItemCount = itemCount,
                                            TotalItemsCount = totalCount,
                                            Status = ProgressStatus.AddRemote
                                        });
                                    }
                                }
                            }
                            else
                            {
                                await ReportProgressAsync(new ProgressReportMovie()
                                {
                                    Id = PROCESS_ID_MOVIES,
                                    Name = PROCESS_MOVIES,
                                    ItemName = plexMovie.Title,
                                    Year = plexMovie.Year,
                                    CurrentItemCount = itemCount,
                                    TotalItemsCount = totalCount,
                                    Status = ProgressStatus.NotSupported
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            string errorMessage = ex.Message;

                            await ReportProgressAsync(new ProgressReportMovie()
                            {
                                Id = PROCESS_ID_MOVIES,
                                Name = PROCESS_MOVIES,
                                ItemName = plexMovie.Title,
                                Year = plexMovie.Year,
                                CurrentItemCount = itemCount,
                                TotalItemsCount = totalCount,
                                Status = ProgressStatus.ErrorAddRemote
                            });

                            await ReportProgressAsync(new ProgressReportMovie()
                            {
                                Id = PROCESS_ID_MOVIES,
                                Name = PROCESS_MOVIES,
                                ItemName = plexMovie.Title,
                                Year = plexMovie.Year,
                                CurrentItemCount = itemCount,
                                TotalItemsCount = totalCount,
                                Status = ProgressStatus.Message,
                                Message = ex.Message
                            });
                        }
                    }
                }
            }
            else
            {
                // Add to the processed list to find extra items.
                processedMovies.Add(traktMovieWatched);

                await ReportProgressAsync(new ProgressReportMovie()
                {
                    Id = PROCESS_ID_MOVIES,
                    Name = PROCESS_MOVIES,
                    ItemName = plexMovie.Title,
                    Year = plexMovie.Year,
                    CurrentItemCount = itemCount,
                    TotalItemsCount = totalCount,
                    Status = ProgressStatus.Processing
                });

                if (plexMovie.ViewCount > 0)
                {
                    await ReportProgressAsync(new ProgressReportMovie()
                    {
                        Id = PROCESS_ID_MOVIES,
                        Name = PROCESS_MOVIES,
                        ItemName = plexMovie.Title,
                        Year = plexMovie.Year,
                        CurrentItemCount = itemCount,
                        TotalItemsCount = totalCount,
                        Status = ProgressStatus.Nothing
                    });
                }
                else
                {
                    await PlexClient.Scrobble(plexMovie);

                    await ReportProgressAsync(new ProgressReportMovie()
                    {
                        Id = PROCESS_ID_MOVIES,
                        Name = PROCESS_MOVIES,
                        ItemName = plexMovie.Title,
                        Year = plexMovie.Year,
                        CurrentItemCount = itemCount,
                        TotalItemsCount = totalCount,
                        Status = ProgressStatus.Sync
                    });
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
        private async Task ProcessPlexShowAsync(Show plexShow, ITraktWatchedShow[] traktShowsWatched, ITraktCollectionShow[] traktShows, int itemCount, int totalCount, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (plexShow.ExternalProvider.Equals("themoviedb"))
            {
                await ReportProgressAsync(new ProgressReportTVShow()
                {
                    Id = PROCESS_ID_TVSHOWS,
                    Name = PROCESS_TVSHOWS,
                    ItemName = plexShow.Title,
                    ExternalProviderId = plexShow.ExternalProviderId,
                    CurrentItemCount = itemCount,
                    TotalItemsCount = totalCount,
                    Status = ProgressStatus.NotSupported
                });
            }
            else
            {
                var traktShow = traktShows.FirstOrDefault(x => HasMatchingId(plexShow, x.Ids));
                if (traktShow == null)
                {
                    await ReportProgressAsync(new ProgressReportTVShow()
                    {
                        Id = PROCESS_ID_TVSHOWS,
                        Name = PROCESS_TVSHOWS,
                        ItemName = plexShow.Title,
                        ExternalProviderId = plexShow.ExternalProviderId,
                        CurrentItemCount = itemCount,
                        TotalItemsCount = totalCount,
                        Status = ProgressStatus.NotFoundRemote
                    });

                    try
                    {
                        if (plexShow.ExternalProvider == "thetvdb")
                        {
                            var traktRemoteSearchResponse = await TraktClient.Search.GetIdLookupResultsAsync(searchIdType: TraktSearchIdType.TvDB, lookupId: plexShow.ExternalProviderId, extendedInfo: new TraktExtendedInfo().SetMetadata());
                            await Task.Delay(TimeSpan.FromSeconds(.15));

                            if (traktRemoteSearchResponse != null && traktRemoteSearchResponse.IsSuccess)
                            {
                                var syncCollection = TraktPost.NewSyncCollectionPost();

                                var traktShowRemote = traktRemoteSearchResponse.Value.FirstOrDefault();

                                if (traktShowRemote != null && traktShowRemote.Show != null)
                                {
                                    syncCollection.WithShow(traktShowRemote.Show);
                                    syncCollection.AddCollectedShow(traktShowRemote.Show);
                                    await TraktClient.Sync.AddCollectionItemsAsync(syncCollection.Build());

                                    await ReportProgressAsync(new ProgressReportTVShow()
                                    {
                                        Id = PROCESS_ID_TVSHOWS,
                                        Name = PROCESS_TVSHOWS,
                                        ItemName = plexShow.Title,
                                        ExternalProviderId = plexShow.ExternalProviderId,
                                        CurrentItemCount = itemCount,
                                        TotalItemsCount = totalCount,
                                        Status = ProgressStatus.AddRemote
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        await ReportProgressAsync(new ProgressReportTVShow()
                        {
                            Id = PROCESS_ID_TVSHOWS,
                            Name = PROCESS_TVSHOWS,
                            ItemName = plexShow.Title,
                            ExternalProviderId = plexShow.ExternalProviderId,
                            CurrentItemCount = itemCount,
                            TotalItemsCount = totalCount,
                            Status = ProgressStatus.ErrorAddRemote
                        });
                    }
                }

                if (token.IsCancellationRequested)
                {
                    return;
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
                                    await ReportProgressAsync(new ProgressReportTVShow()
                                    {
                                        Id = PROCESS_ID_TVSHOWS,
                                        Name = PROCESS_TVSHOWS,
                                        ItemName = plexShow.Title,
                                        Season = plexSeason.No,
                                        Episode = plexEpisode.No,
                                        ExternalProviderId = plexShow.ExternalProviderId,
                                        CurrentItemCount = itemCount,
                                        TotalItemsCount = totalCount,
                                        Status = ProgressStatus.Processing
                                    });

                                    try
                                    {
                                        var traktRemoteEpisodeResponse = await TraktClient.Episodes.GetEpisodeAsync(showIdOrSlug: traktShow.Ids.Trakt.ToString(), seasonNumber: Convert.ToUInt32(plexSeason.No), episodeNumber: Convert.ToUInt32(plexEpisode.No), extendedInfo: new TraktExtendedInfo().SetMetadata());
                                        await Task.Delay(TimeSpan.FromSeconds(.15));

                                        // Add a delay to lower the rate of calls to trakt API.
                                        await Task.Delay(TimeSpan.FromSeconds(0.25));

                                        if (traktRemoteEpisodeResponse != null && traktRemoteEpisodeResponse.IsSuccess)
                                        {
                                            // Found it and is not watched, lets update this.
                                            var spb = TraktPost.NewSyncHistoryPost();

                                            spb.WithEpisode(traktRemoteEpisodeResponse.Value);
                                            spb.AddWatchedEpisode(traktRemoteEpisodeResponse.Value);
                                            await TraktClient.Sync.AddWatchedHistoryItemsAsync(spb.Build());
                                            await Task.Delay(TimeSpan.FromSeconds(.15));

                                            await ReportProgressAsync(new ProgressReportTVShow()
                                            {
                                                Id = PROCESS_ID_TVSHOWS,
                                                Name = PROCESS_TVSHOWS,
                                                ItemName = plexShow.Title,
                                                Season = plexSeason.No,
                                                Episode = plexEpisode.No,
                                                ExternalProviderId = plexShow.ExternalProviderId,
                                                CurrentItemCount = itemCount,
                                                TotalItemsCount = totalCount,
                                                Status = ProgressStatus.Sync
                                            });

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
                                        await ReportProgressAsync(new ProgressReportTVShow()
                                        {
                                            Id = PROCESS_ID_TVSHOWS,
                                            Name = PROCESS_TVSHOWS,
                                            ItemName = plexShow.Title,
                                            Season = plexSeason.No,
                                            Episode = plexEpisode.No,
                                            ExternalProviderId = plexShow.ExternalProviderId,
                                            CurrentItemCount = itemCount,
                                            TotalItemsCount = totalCount,
                                            Status = ProgressStatus.ErrorAddRemote
                                        });
                                    }
                                }
                                else if ((traktEpisodeWatched != null) && (traktEpisodeWatched.Plays > 0) && (plexEpisode.ViewCount == 0))
                                {
                                    await ReportProgressAsync(new ProgressReportTVShow()
                                    {
                                        Id = PROCESS_ID_TVSHOWS,
                                        Name = PROCESS_TVSHOWS,
                                        ItemName = plexShow.Title,
                                        Season = plexSeason.No,
                                        Episode = plexEpisode.No,
                                        ExternalProviderId = plexShow.ExternalProviderId,
                                        CurrentItemCount = itemCount,
                                        TotalItemsCount = totalCount,
                                        Status = ProgressStatus.Sync
                                    });

                                    await PlexClient.Scrobble(plexEpisode);
                                }
                                else if ((traktEpisodeWatched != null) && (traktEpisodeWatched.Plays > 0) && (plexEpisode.ViewCount > 0))
                                {
                                    await ReportProgressAsync(new ProgressReportTVShow()
                                    {
                                        Id = PROCESS_ID_TVSHOWS,
                                        Name = PROCESS_TVSHOWS,
                                        ItemName = plexShow.Title,
                                        Season = plexSeason.No,
                                        Episode = plexEpisode.No,
                                        ExternalProviderId = plexShow.ExternalProviderId,
                                        CurrentItemCount = itemCount,
                                        TotalItemsCount = totalCount,
                                        Status = ProgressStatus.Nothing
                                    });
                                }

                                if (token.IsCancellationRequested)
                                {
                                    return;
                                }
                            }
                        }
                        else
                        {
                            await ReportProgressAsync(new ProgressReportTVShow()
                            {
                                Id = PROCESS_ID_TVSHOWS,
                                Name = PROCESS_TVSHOWS,
                                ItemName = plexShow.Title,
                                Season = plexSeason.No,
                                ExternalProviderId = plexShow.ExternalProviderId,
                                CurrentItemCount = itemCount,
                                TotalItemsCount = totalCount,
                                Status = ProgressStatus.Processing
                            });

                            try
                            {
                                var traktRemoteSeasonResponse = await TraktClient.Seasons.GetSeasonAsync(showIdOrSlug: traktShow.Ids.Trakt.ToString(), seasonNumber: Convert.ToUInt32(plexSeason.No), extendedInfo: new TraktExtendedInfo().SetMetadata());
                                await Task.Delay(TimeSpan.FromSeconds(.15));

                                if (traktRemoteSeasonResponse != null && traktRemoteSeasonResponse.IsSuccess)
                                {
                                    // Found it and is not watched, lets update this.
                                    var syncHistory = TraktPost.NewSyncHistoryPost();
                                    var syncCollection = TraktPost.NewSyncCollectionPost();

                                    var traktEpisodes = traktRemoteSeasonResponse.Value;

                                    List<ITraktEpisode> collectedEpisodes = new List<ITraktEpisode>();
                                    List<ITraktEpisode> watcheddEpisodes = new List<ITraktEpisode>();


                                    foreach (var plexEpisode in plexSeason.Episodes)
                                    {
                                        var traktEpisode = traktEpisodes.Where(x => x.Number == plexEpisode.No).FirstOrDefault();

                                        if (traktEpisode != null)
                                        {
                                            syncCollection.WithEpisode(traktEpisode);
                                            syncCollection.AddCollectedEpisode(traktEpisode);
                                            collectedEpisodes.Add(traktEpisode);

                                            await ReportProgressAsync(new ProgressReportTVShow()
                                            {
                                                Id = PROCESS_ID_TVSHOWS,
                                                Name = PROCESS_TVSHOWS,
                                                ItemName = plexShow.Title,
                                                Season = plexSeason.No,
                                                Episode = plexEpisode.No,
                                                ExternalProviderId = plexShow.ExternalProviderId,
                                                CurrentItemCount = itemCount,
                                                TotalItemsCount = totalCount,
                                                Status = ProgressStatus.AddRemote
                                            });

                                            processedEpisodesSync.Add(
                                                new EpisodeProcessed()
                                                {
                                                    ShowTraktId = traktShow.Ids.Trakt,
                                                    SeasonNumber = plexSeason.No,
                                                    Number = traktEpisode.Number
                                                });

                                            if (plexEpisode.ViewCount > 0)
                                            {
                                                syncHistory.WithEpisode(traktEpisode);
                                                syncHistory.AddWatchedEpisode(traktEpisode);
                                                watcheddEpisodes.Add(traktEpisode);
                                            }
                                        }

                                        if (token.IsCancellationRequested)
                                        {
                                            return;
                                        }
                                    }

                                    if (collectedEpisodes.Count > 0)
                                    {
                                        await TraktClient.Sync.AddCollectionItemsAsync(syncCollection.Build());
                                        await Task.Delay(TimeSpan.FromSeconds(.15));
                                    }

                                    if (watcheddEpisodes.Count > 0)
                                    {
                                        await TraktClient.Sync.AddWatchedHistoryItemsAsync(syncHistory.Build());
                                        await Task.Delay(TimeSpan.FromSeconds(.15));
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                await ReportProgressAsync(new ProgressReportTVShow()
                                {
                                    Id = PROCESS_ID_TVSHOWS,
                                    Name = PROCESS_TVSHOWS,
                                    ItemName = plexShow.Title,
                                    Season = plexSeason.No,
                                    ExternalProviderId = plexShow.ExternalProviderId,
                                    CurrentItemCount = itemCount,
                                    TotalItemsCount = totalCount,
                                    Status = ProgressStatus.ErrorAddRemote
                                });
                            }
                        }
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(.25));
            }
        }

        /// <summary>
        /// Wrapper for reporting progress.
        /// </summary>
        /// <param name="progressReport">Progress to report.</param>
        /// <returns>Task to await.</returns>
        private async Task ReportProgressAsync(IProgressReport progressReport)
        {
            try
            {
                if (ReportProgressDelegate == null)
                {
                    Debug.WriteLine(progressReport);
                }
                else
                {
                    await ReportProgressDelegate(progressReport);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        /// <summary>
        /// Select the supported Search Id for movies.
        /// </summary>
        /// <param name="externalProvider">Provider to check.</param>
        /// <returns>Search that should be used.</returns>
        private TraktSearchIdType SupportedSearchIdMovie(string externalProvider)
        {
            TraktSearchIdType returnValue = null;
            switch (externalProvider.ToLowerInvariant())
            {
                case "imdb":
                    returnValue = TraktSearchIdType.ImDB;
                    break;

                case "themoviedb":
                    returnValue = TraktSearchIdType.TmDB;
                    break;
            }

            return returnValue;
        }
    }
}