using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Verse;

namespace RimTaiName.Spike
{
    /// <summary>
    /// Step 0: proves that HttpClient and TLS work inside RimWorld's Mono runtime.
    /// Sends no API key; any HTTP status (403 is expected) means the HTTPS handshake succeeded.
    /// </summary>
    internal static class ConnectivityProbe
    {
        private const string Url = "https://generativelanguage.googleapis.com/v1beta/models";

        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        private static volatile string status = "-";

        public static string Status => status;

        public static void Start()
        {
            status = "...";
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Task.Run(async () =>
            {
                var watch = Stopwatch.StartNew();
                try
                {
                    using (var response = await Client.GetAsync(Url).ConfigureAwait(false))
                        status = $"HTTP {(int)response.StatusCode} {response.StatusCode} ({watch.ElapsedMilliseconds} ms)";
                }
                catch (Exception e)
                {
                    status = $"FAILED {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}";
                }
                Log.Message("[RimTaiName] HTTPS probe: " + status);
            });
        }
    }
}
