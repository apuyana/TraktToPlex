using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SyncClient;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TraktNet;
using TraktNet.Objects.Authentication;
using Plex = PlexClient;

namespace SyncConsoleClient
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("Loading configuration");

            try
            {
                var t = SyncData();
                t.Wait();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        /// <summary>
        /// Sync data.
        /// </summary>
        /// <returns>Task to await.</returns>
        private static async Task SyncData()
        {
            var configText = await File.ReadAllTextAsync("config.json");

            if (!string.IsNullOrWhiteSpace(configText))
            {
                dynamic configJson = JsonConvert.DeserializeObject(configText);

                string tclientId = configJson.traktConfig.clientId;
                string tclientSecret = configJson.traktConfig.clientSecret;
                string tclientKey = configJson.traktConfig.key;

                TraktClient traktClient = new TraktClient(clientId: tclientId, clientSecret: tclientSecret);
                traktClient.Authorization = TraktAuthorization.CreateWith(accessToken: tclientKey);

                string pclientSecret = configJson.plexConfig.clientSecret;
                string pclientServerKey = configJson.plexConfig.plexServerKey;
                string pclientServer = configJson.plexConfig.server;

                Plex.Client plexClient = new Plex.Client(clientId: pclientSecret);
                plexClient.SetAuthToken(pclientServerKey);

                JArray servers = configJson.plexConfig.servers;

                JToken selectedServer = null;

                if (string.IsNullOrWhiteSpace("pclientServer"))
                {
                    selectedServer = servers.FirstOrDefault();
                }
                else
                {
                    for (int i = 0; (i < servers.Count) && (selectedServer == null); i++)
                    {
                        JToken element = servers[i];

                        if (element != null && string.Compare(element.Value<string>("name"), pclientServer, true) == 0)
                        {
                            selectedServer = element;
                        }
                    }
                }

                if (selectedServer != null)
                {
                    plexClient.SetPlexServerUrl(selectedServer.Value<string>("url"));
                }

                bool removeFromCollection = false;

                try
                {
                    if (configJson.removeFromCollection != null)
                    {
                        removeFromCollection = configJson.removeFromCollection;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(new ArgumentOutOfRangeException("removeFromCollection", ex));
                }

                SyncAgent agent = new SyncAgent(plexClient: plexClient, traktClient: traktClient, removeFromCollection: removeFromCollection);

                await agent.SyncMoviesAsync();
                await agent.SyncTVShowsAsync();
            }
        }
    }
}