using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;

namespace MatchZy
{
    public partial class MatchZy
    {
        public string demoPath = "MatchZy/";
        public string demoNameFormat = "{TIME}_{MATCH_ID}_{MAP}_{TEAM1}_vs_{TEAM2}";
        public string demoUploadURL = "";
        public string demoUploadHeaderKey = "";
        public string demoUploadHeaderValue = "";

        public string activeDemoFile = "";

        public bool isDemoRecording = false;
        public bool isDemoRecordingEnabled = true;
        public int demoUploadDelay = 10;

        public void StartDemoRecording()
        {
            if (!isDemoRecordingEnabled)
            {
                Log("[StartDemoRecording] Demo recording is disabled.");
                return;
            }
            if (isDemoRecording)
            {
                Log("[StartDemoRecording] Demo recording is already in progress.");
                return;
            }
            // Only letters, digits, '-', '_' and '.': see DemoFileName.
            (int team1Score, int team2Score) = GetTeamsScore();
            string demoFileName = DemoFileName.Build(
                demoNameFormat,
                DateTime.Now,
                liveMatchId,
                Server.MapName,
                matchConfig.CurrentMapNumber,
                matchzyTeam1.teamName,
                matchzyTeam2.teamName,
                team1Score,
                team2Score) + ".dem";
            try
            {
                string demoDirectory = Path.Join(Server.GameDirectory + "/csgo/", DemoFileLocator.NormalizeDemoPath(demoPath));
                if (!Directory.Exists(demoDirectory))
                {
                    Directory.CreateDirectory(demoDirectory);
                }
                // Relative to csgo/: StopDemoRecording resolves it again.
                string tempDemoPath = DemoFileLocator.NormalizeDemoPath(demoPath) + demoFileName;
                activeDemoFile = tempDemoPath;
                // tv_record gets the absolute path. A relative one lands in the first Game search
                // path, which is csgo/addons/metamod/ on Metamod servers.
                string fullPath = DemoFileLocator.TvRecordPath(Server.GameDirectory, demoPath, demoFileName);
                Log($"[StartDemoRecoding] Starting demo recording, path: {fullPath}");
                Server.ExecuteCommand($"tv_record {DemoFileLocator.TvRecordArgument(fullPath)}");
                isDemoRecording = true;
            }
            catch (Exception ex)
            {
                Log($"[StartDemoRecording - FATAL] Error: {ex.Message}. Starting demo recording with path. Name: {demoFileName}");
                // This is to avoid demo loss in any case of exception
                Server.ExecuteCommand($"tv_record {demoFileName}");
                isDemoRecording = true;
            }

        }

        public void StopDemoRecording(float delay, string activeDemoFile, long liveMatchId, int currentMapNumber)
        {
            Log($"[StopDemoRecording] Going to stop demorecording in {delay}s");
            IReadOnlyList<string> demoPaths = DemoFileLocator.CandidatePaths(Server.GameDirectory, activeDemoFile);
            (int t1score, int t2score) = GetTeamsScore();
            int roundNumber = t1score + t2score;
            AddTimer(delay, () =>
            {
                if (isDemoRecording)
                {
                    Server.ExecuteCommand($"tv_stoprecord");
                }
                isDemoRecording = false;
                // Use Task.Delay instead of AddTimer so the upload survives a map change
                string capturedUrl = demoUploadURL;
                string capturedHeaderKey = demoUploadHeaderKey;
                string capturedHeaderValue = demoUploadHeaderValue;
                Log($"[StopDemoRecording] Demo upload config — URL: \"{capturedUrl}\" HeaderKey: \"{capturedHeaderKey}\" HeaderValue: \"{(string.IsNullOrEmpty(capturedHeaderValue) ? "" : "***")}\"");
                Task.Run(async () =>
                {
                    await Task.Delay(demoUploadDelay * 1000);
                    // Demos recorded by older builds (relative tv_record path) are in csgo/addons/metamod/.
                    string demoPath = demoPaths.FirstOrDefault(File.Exists) ?? demoPaths[0];
                    await UploadFileAsync(demoPath, capturedUrl, capturedHeaderKey, capturedHeaderValue, liveMatchId, currentMapNumber, roundNumber);
                });
            });
        }

        public void StopTvForMapChange()
        {
            bool tvBroadcast = ConVar.Find("tv_broadcast")!.GetPrimitiveValue<bool>();
            if (tvBroadcast) Server.ExecuteCommand("tv_broadcast 0");
            if (isDemoRecording)
            {
                Server.ExecuteCommand("tv_stoprecord");
                isDemoRecording = false;
            }
        }

        public int GetTvDelay()
        {
            bool tvEnable = ConVar.Find("tv_enable")!.GetPrimitiveValue<bool>();
            if (!tvEnable) return 0;

            bool tvEnable1 = ConVar.Find("tv_enable1")!.GetPrimitiveValue<bool>();
            int tvDelay = ConVar.Find("tv_delay")!.GetPrimitiveValue<int>();

            if (!tvEnable1) return tvDelay;
            int tvDelay1 = ConVar.Find("tv_delay1")!.GetPrimitiveValue<int>();

            if (tvDelay < tvDelay1) return tvDelay1;
            return tvDelay;
        }

        [ConsoleCommand("get5_demo_upload_header_key", "If defined, a custom HTTP header with this name is added to the HTTP requests for demos")]
        [ConsoleCommand("matchzy_demo_upload_header_key", "If defined, a custom HTTP header with this name is added to the HTTP requests for demos")]
        public void DemoUploadHeaderKeyCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;
            string header = command.ArgByIndex(1).Trim();

            if (header != "") demoUploadHeaderKey = header;
        }

        [ConsoleCommand("get5_demo_upload_header_value", "If defined, the value of the custom header added to the demos sent over HTTP")]
        [ConsoleCommand("matchzy_demo_upload_header_value", "If defined, the value of the custom header added to the demos sent over HTTP")]
        public void DemoUploadHeaderValueCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;
            string headerValue = command.ArgByIndex(1).Trim();

            if (headerValue != "") demoUploadHeaderValue = headerValue;
        }
    }
}
