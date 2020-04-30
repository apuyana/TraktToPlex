using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using SyncClient;
using SyncClient.Model;
using System;
using System.Threading;
using System.Threading.Tasks;
using TraktNet;
using TraktNet.Objects.Authentication;
using Plex = PlexClient;

namespace TraktToPlex.Hubs
{
    public class MigrationHub : Hub
    {
        private readonly IConfiguration _config;
        private readonly Plex.Client _plexClient;
        private readonly TraktClient _traktClient;

        public MigrationHub(IConfiguration config, Plex.Client plexClient)
        {
            _config = config;
            _plexClient = plexClient;
            if (!_plexClient.HasClientId)
            {
                _plexClient.SetClientId(_config["PlexConfig:ClientSecret"]);
            }

            _traktClient = new TraktClient(_config["TraktConfig:ClientId"], _config["TraktConfig:ClientSecret"]);
        }

        public async Task StartMigration(string traktKey, string plexKey, string plexUrl)
        {
            try
            {
                _plexClient.SetAuthToken(plexKey);
                _plexClient.SetPlexServerUrl(plexUrl);

                _traktClient.Authorization = TraktAuthorization.CreateWith(traktKey);

                SyncAgent agent = new SyncAgent(plexClient: _plexClient, traktClient: _traktClient, removeFromCollection: false);

                agent.ReportProgressDelegate = ReportProgress;

                await agent.SyncMoviesAsync(token: CancellationToken.None);
                await agent.SyncTVShowsAsync(token: CancellationToken.None);
            }
            catch (Exception e)
            {
                throw new HubException(e.Message);
            }
        }

        private async Task ReportProgress(IProgressReport progressReport)
        {
            await Clients.Caller.SendAsync("UpdateProgress", $"[{DateTime.Now}]: {progressReport}");
        }
    }
}