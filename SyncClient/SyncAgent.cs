using PlexClient.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TraktNet;
using TraktNet.Objects.Get.Collections;
using TraktNet.Objects.Get.Movies;
using TraktNet.Objects.Get.Watched;
using TraktNet.Objects.Post.Syncs.Collection;
using TraktNet.Objects.Post.Syncs.History;
using TraktNet.Requests.Parameters;
using TraktNet.Responses;
using Plex = PlexClient;

namespace SyncClient
{
    public class SyncAgent
    {
        /// <summary>
        /// List of process movies so far.
        /// </summary>
        private readonly List<ITraktMovie> processedMovies = new List<ITraktMovie>(200);

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
        public SyncAgent(Plex.Client plexClient, TraktClient traktClient, int batchLimit = 2)
        {
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

            if (DeleteFromCollection && traktMovies.Length > 0 && processedMovies.Count > 0)
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

                if (deleteQueueMovies.Count > 0)
                {
                    TraktSyncCollectionPostBuilder tsp = new TraktSyncCollectionPostBuilder();
                    tsp.AddMovies(deleteQueueMovies);
                    await TraktClient.Sync.RemoveCollectionItemsAsync(tsp.Build());
                }
            }
        }

        /// <summary>
        /// Flag to check if the extra item should be removed from collection.
        /// </summary>
        protected bool DeleteFromCollection
        {
            get
            {
                return false;
            }
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
        /// <param name="traktIds1">Trakt Ids.</param>
        /// <param name="traktIds2">Trakt Ids.</param>
        /// <returns>True if match.</returns>
        private bool HasMatchingId(ITraktMovieIds traktIds1, ITraktMovieIds traktIds2)
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
                        await ReportProgressAsync($"The movie \"{plexMovie.Title}\" was not found on Trakt. Skipping");
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
                    await ReportProgressAsync($"The movie \"{plexMovie.Title}\" was watched on Trakt or plex.");
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