using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SyncClient;
using SyncClient.Model;
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
                string filePath = null;
                if (args != null && args.Length > 0)
                {
                    filePath = args[0];

                    if (!string.IsNullOrWhiteSpace(filePath))
                    {
                        Console.WriteLine($"Using path:{filePath} ");
                    }
                }

                var t = SyncData(filePath);
                t.Wait();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private static Task ReportProgress(ProgressReport progressReport)
        {
            Console.WriteLine(progressReport);

            return Task.FromResult(0);
        }

        /// <summary>
        /// Sync data.
        /// </summary>
        /// <returns>Task to await.</returns>
        private static async Task SyncData(string filePath = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                filePath = "config.json";
            }

            var configText = await File.ReadAllTextAsync(filePath);

            if (!string.IsNullOrWhiteSpace(configText))
            {
                dynamic configJson = JsonConvert.DeserializeObject(configText);

                string tclientId = configJson.traktConfig.clientId;                
                string tclientKey = configJson.traktConfig.key;
                
                TraktClient traktClient = new TraktClient(clientId: tclientId, clientSecret: string.Empty);
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

                agent.ReportProgressDelegate = ReportProgress;

                await agent.SyncMoviesAsync();
                await agent.SyncTVShowsAsync();
            }
        }
    }
}