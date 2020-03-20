using PlexClient.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TraktNet;
using TraktNet.Objects.Get.Movies;
using TraktNet.Objects.Get.Watched;
using TraktNet.Requests.Parameters;
using TraktNet.Responses;
using Plex = PlexClient;

namespace SyncClient
{
    public class SyncAgent
    {
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
            Task<Movie[]> plexMoviesTask = PlexClient.GetMovies();

            Task<TraktListResponse<ITraktWatchedMovie>> traktMoviesTask = TraktClient.Sync.GetWatchedMoviesAsync(new TraktExtendedInfo().SetFull());

            await Task.WhenAll(plexMoviesTask, traktMoviesTask);

            ITraktWatchedMovie[] traktMovies = traktMoviesTask.Result.ToArray();
            Movie[] plexMovies = plexMoviesTask.Result;

            if ((traktMovies != null) && (traktMovies.Length > 0) &&
                (plexMovies != null) && (plexMovies.Length > 0))
            {
                await ReportProgressAsync($"Total watched plex movies: {plexMovies.Length}; Total watched Trakt Movies {traktMovies.Length}");

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
                            batchTasks.Add(ProcessPlexMovieAsync(plexMovie: plexMovies[i + j], traktMovies: traktMovies));
                        }
                    }

                    await Task.WhenAll(batchTasks.ToArray());

                    await ReportProgressAsync(string.Empty);
                    await ReportProgressAsync($"------- Processed from: {i} to {i + batchLimit}. Total to process: {calculatedLimit} ----------");
                }
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
        /// Process single moview.
        /// </summary>
        /// <param name="plexMovie">Plex movie to sync.</param>
        /// <param name="traktMovies">All Trakt movies to search.</param>
        /// <returns>Task to await.</returns>
        private async Task ProcessPlexMovieAsync(Movie plexMovie, ITraktWatchedMovie[] traktMovies)
        {
            var traktMovie = traktMovies.FirstOrDefault(x => HasMatchingId(plexMovie, x.Ids));
            if (traktMovie == null)
            {
                await ReportProgressAsync($"The movie \"{plexMovie.Title}\" was not found as watched on Trakt. Skipping!");
            }
            else
            {
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