using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using System;
using System.Linq;

namespace MatchZy
{
    public partial class MatchZy
    {
        public bool isPreVeto = false;
        public bool isVeto = false;
        public int warningsPrinted = 0;
        public int vetoCountdownTime = 5; // In Seconds

        public bool mapChangePending = false;
        public bool isVetoFirstChoicePending = false;
        public CounterStrikeSharp.API.Modules.Timers.Timer? vetoStateTimer = null;
        public Dictionary<string, int> vetoCaptains = new(){
            {"team1", -1},
            {"team2", -1}
        };

        // Rock-Paper-Scissors, played before the veto starts - the winner is the one who
        // gets to decide (via .vetostart/.vetoswap) which team acts first in the veto.
        public bool isRpsPending = false;
        public Dictionary<string, string?> rpsChoices = new(){
            {"team1", null},
            {"team2", null}
        };
        // Which team currently holds the .vetostart/.vetoswap choice - set to the RPS
        // winner once RPS resolves.
        public string vetoFirstChoiceTeam = "team1";

        public CsTeam lastVetoTeam = CsTeam.None;

        public void CreateVeto()
        {
            SwapPlayersToTeams();
            vetoCaptains["team1"] = GetTeamCaptain("team1");
            vetoCaptains["team2"] = GetTeamCaptain("team2");
            // Todo: Implement pauseOnVeto CVAR
            // if (pauseOnVeto) {
            //     Server.ExecuteCommand("mp_pause_match");
            //     isPaused = true;
            //     unpauseData["pauseTeam"] = "Admin";
            // }
            Server.ExecuteCommand("mp_warmup_end");
            Server.ExecuteCommand("mp_pause_match");
            isPaused = true;
            unpauseData["pauseTeam"] = "Admin";
            vetoStateTimer = AddTimer(1, VetoCountdown, TimerFlags.REPEAT);
            isVeto = true;
            readyAvailable = false;
            isWarmup = false;
            KillPhaseTimers();
        }

        public void VetoCountdown()
        {
            if (!isVeto)
            {
                warningsPrinted = 0;
                vetoStateTimer?.Kill();
                vetoStateTimer = null;
                return;
            }
            if (warningsPrinted >= vetoCountdownTime)
            {
                int team1Captain = vetoCaptains["team1"];
                int team2Captain = vetoCaptains["team2"];
                warningsPrinted = 0;
                bool team1CaptainValid = playerData.ContainsKey(team1Captain) && playerData[team1Captain].IsValid;
                bool team2CaptainValid = playerData.ContainsKey(team2Captain) && playerData[team2Captain].IsValid;
                if ((!team1CaptainValid && !IsSimulatingTeam1) || (!team2CaptainValid && !IsSimulatingTeam2))
                {
                    AbortVeto();
                    vetoStateTimer?.Kill();
                    vetoStateTimer = null;
                    return;
                }
                if (team1CaptainValid)
                    Server.PrintToChatAll($"{chatPrefix} Captain for {ChatColors.Green}{matchzyTeam1.teamName}{ChatColors.Default}: {ChatColors.Green}{playerData[team1Captain].PlayerName}{ChatColors.Default}");
                else if (IsSimulatingTeam1)
                    Server.PrintToChatAll($"{chatPrefix} Captain for {ChatColors.Green}{matchzyTeam1.teamName}{ChatColors.Default}: {ChatColors.Yellow}[Simulated]{ChatColors.Default}");
                if (team2CaptainValid)
                    Server.PrintToChatAll($"{chatPrefix} Captain for {ChatColors.Green}{matchzyTeam2.teamName}{ChatColors.Default}: {ChatColors.Green}{playerData[team2Captain].PlayerName}{ChatColors.Default}");
                else if (IsSimulatingTeam2)
                    Server.PrintToChatAll($"{chatPrefix} Captain for {ChatColors.Green}{matchzyTeam2.teamName}{ChatColors.Default}: {ChatColors.Yellow}[Simulated]{ChatColors.Default}");

                StartRps();

                vetoStateTimer?.Kill();
                vetoStateTimer = null;
                return;
            }
            warningsPrinted++;
            int secondsRemaining = vetoCountdownTime - warningsPrinted + 1;
            Server.PrintToChatAll($"{chatPrefix} Map selection commencing in {secondsRemaining}");
        }

        public void StartRps()
        {
            isRpsPending = true;
            rpsChoices["team1"] = null;
            rpsChoices["team2"] = null;

            Server.PrintToChatAll($"{chatPrefix} {Localizer["matchzy.veto.rpsstart"]}");

            if (IsSimulatingTeam1) AutoPlayRps("team1");
            if (IsSimulatingTeam2) AutoPlayRps("team2");
        }

        public void AutoPlayRps(string team)
        {
            AddTimer(2.0f, () => {
                if (!isVeto || !isRpsPending || rpsChoices[team] != null) return;
                string[] moves = { "rock", "paper", "scissors" };
                string move = moves[new Random().Next(moves.Length)];
                Server.PrintToChatAll($"{chatPrefix} [Simulate] {ChatColors.Green}{(team == "team1" ? matchzyTeam1.teamName : matchzyTeam2.teamName)}{ChatColors.Default} has made their choice.");
                SubmitRpsChoice(team, move);
            });
        }

        public void HandleRpsChoice(CCSPlayerController? player, string move)
        {
            if (player == null || !isVeto || !isRpsPending) return;
            string? team = null;
            if (player.UserId == vetoCaptains["team1"]) team = "team1";
            else if (player.UserId == vetoCaptains["team2"]) team = "team2";
            if (team == null || rpsChoices[team] != null) return;
            SubmitRpsChoice(team, move);
        }

        public void SubmitRpsChoice(string team, string move)
        {
            rpsChoices[team] = move;
            Team matchzyTeam = team == "team1" ? matchzyTeam1 : matchzyTeam2;
            // Don't reveal the move itself yet - only confirm it was locked in, so the
            // other captain can't just counter-pick after seeing it in chat.
            Server.PrintToChatAll($"{chatPrefix} {Localizer["matchzy.veto.rpschoicemade", matchzyTeam.teamName]}");

            if (rpsChoices["team1"] != null && rpsChoices["team2"] != null)
            {
                ResolveRps();
            }
        }

        public void ResolveRps()
        {
            string move1 = rpsChoices["team1"]!;
            string move2 = rpsChoices["team2"]!;

            Server.PrintToChatAll($"{chatPrefix} {Localizer["matchzy.veto.rpsreveal", matchzyTeam1.teamName, CapitalizeFirst(move1), matchzyTeam2.teamName, CapitalizeFirst(move2)]}");

            string? winnerTeam = RpsWinner(move1, move2);
            if (winnerTeam == null)
            {
                Server.PrintToChatAll($"{chatPrefix} {Localizer["matchzy.veto.rpstie"]}");
                rpsChoices["team1"] = null;
                rpsChoices["team2"] = null;
                if (IsSimulatingTeam1) AutoPlayRps("team1");
                if (IsSimulatingTeam2) AutoPlayRps("team2");
                return;
            }

            isRpsPending = false;
            Team winningMatchzyTeam = winnerTeam == "team1" ? matchzyTeam1 : matchzyTeam2;
            Server.PrintToChatAll($"{chatPrefix} {Localizer["matchzy.veto.rpswinner", winningMatchzyTeam.teamName]}");

            StartVetoFirstChoice(winnerTeam);
        }

        private static string? RpsWinner(string move1, string move2)
        {
            if (move1 == move2) return null;
            bool team1Wins = (move1 == "rock" && move2 == "scissors") ||
                              (move1 == "paper" && move2 == "rock") ||
                              (move1 == "scissors" && move2 == "paper");
            return team1Wins ? "team1" : "team2";
        }

        private static string CapitalizeFirst(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        // Grants the given team (the RPS winner) the .vetostart/.vetoswap choice - same
        // coin-flip-style prompt as before, just directed at whichever team actually won
        // instead of always team1.
        public void StartVetoFirstChoice(string chooserTeam)
        {
            isVetoFirstChoicePending = true;
            vetoFirstChoiceTeam = chooserTeam;

            Team chooserMatchzyTeam = chooserTeam == "team1" ? matchzyTeam1 : matchzyTeam2;
            Team otherMatchzyTeam = chooserTeam == "team1" ? matchzyTeam2 : matchzyTeam1;
            int chooserCaptain = vetoCaptains[chooserTeam];
            bool chooserCaptainValid = playerData.ContainsKey(chooserCaptain) && playerData[chooserCaptain].IsValid;
            bool chooserIsSimulating = (chooserTeam == "team1" && IsSimulatingTeam1) || (chooserTeam == "team2" && IsSimulatingTeam2);

            Server.PrintToChatAll($"{chatPrefix} {Localizer["matchzy.veto.coinchoicepending", chooserMatchzyTeam.teamName]}");
            if (chooserCaptainValid)
                playerData[chooserCaptain].PrintToChat($"{chatPrefix} {Localizer["matchzy.veto.coinchoiceprompt", otherMatchzyTeam.teamName]}");

            if (chooserIsSimulating)
            {
                AddTimer(2.0f, () => {
                    if (!isVeto || !isVetoFirstChoicePending) return;
                    isVetoFirstChoicePending = false;
                    bool doStart = new Random().Next(2) == 0;
                    // The ban order defaults to team1 acting first - only swap it when
                    // the outcome actually needs team2 to go first instead.
                    bool needsSwap = (chooserTeam == "team1") ? !doStart : doStart;
                    if (needsSwap) SwapMapBanOrderTeams();
                    if (doStart)
                        Server.PrintToChatAll($"{chatPrefix} [Simulate] {chooserMatchzyTeam.teamName} chose to start veto first.");
                    else
                        Server.PrintToChatAll($"{chatPrefix} [Simulate] {chooserMatchzyTeam.teamName} gave first veto to {otherMatchzyTeam.teamName}.");
                    HandleVetoStep();
                });
            }
            else
            {
                // Only the choosing team needs to act - if the other team happens to be
                // simulated, note that any real captain can still drive this manually.
                bool otherIsSimulating = chooserTeam == "team1" ? IsSimulatingTeam2 : IsSimulatingTeam1;
                if (otherIsSimulating)
                    Server.PrintToChatAll($"{chatPrefix} [Simulate] Any captain can use {ChatColors.Green}.vetostart{ChatColors.Default} or {ChatColors.Yellow}.vetoswap{ChatColors.Default}");
            }
        }

        public void SwapMapBanOrderTeams()
        {
            // Swap all team1_* <-> team2_* entries in the ban order so the other team
            // goes first.
            for (int i = 0; i < matchConfig.MapBanOrder.Count; i++)
            {
                if (matchConfig.MapBanOrder[i].StartsWith("team1_"))
                    matchConfig.MapBanOrder[i] = "team2_" + matchConfig.MapBanOrder[i].Substring(6);
                else if (matchConfig.MapBanOrder[i].StartsWith("team2_"))
                    matchConfig.MapBanOrder[i] = "team1_" + matchConfig.MapBanOrder[i].Substring(6);
            }
        }

        [ConsoleCommand("css_rock", "Rock-Paper-Scissors: pick rock")]
        public void OnRpsRockCommand(CCSPlayerController? player, CommandInfo? command) => HandleRpsChoice(player, "rock");

        [ConsoleCommand("css_paper", "Rock-Paper-Scissors: pick paper")]
        public void OnRpsPaperCommand(CCSPlayerController? player, CommandInfo? command) => HandleRpsChoice(player, "paper");

        [ConsoleCommand("css_scissors", "Rock-Paper-Scissors: pick scissors")]
        public void OnRpsScissorsCommand(CCSPlayerController? player, CommandInfo? command) => HandleRpsChoice(player, "scissors");

        public void HandleVetoStep()
        {
            // As long as sides are not set for a map, either give side pick or auto-decide sides and recursively call this.
            if (matchConfig.MapSides.Count < matchConfig.Maplist.Count)
            {
                if (matchConfig.MatchSideType == "standard")
                {
                    CsTeam otherMatchTeam = lastVetoTeam;
                    if (lastVetoTeam == CsTeam.Terrorist) otherMatchTeam = CsTeam.CounterTerrorist;
                    else if (lastVetoTeam == CsTeam.CounterTerrorist) otherMatchTeam = CsTeam.Terrorist;
                    PromptForSideSelectionInChat(otherMatchTeam);
                }
                else
                {
                    HandleAutomaticSideSelection();
                    HandleVetoStep();
                }
            }
            else if (matchConfig.NumMaps > matchConfig.Maplist.Count)
            {
                if (matchConfig.MapsLeftInVetoPool.Count == 1)
                {
                    // Only 1 map left in the pool, add it by deduction and determine knife logic.
                    string mapName = matchConfig.MapsLeftInVetoPool[0];
                    PickMap(mapName, 0);
                    HandleAutomaticSideSelection();
                    FinishVeto();
                }
                else
                {
                    // More than 1 map in the pool and not all maps are picked; present choices as determine by config.
                    PromptForMapSelectionInChat(GetCurrentMapSelectionOption());
                }
            }
            else
            {
                FinishVeto();
            }
        }

        public void PromptForMapSelectionInChat(string option) {
            string action = "";
            int client = -1;
            string stepMessage = "";
            switch (option) 
            {
                case "team1_ban":
                    action = $"{ChatColors.Green}{matchzyTeam1.teamName}{ChatColors.Default} must now {ChatColors.Red}BAN{ChatColors.Default} a map.";
                    client = vetoCaptains["team1"];
                    stepMessage = $"Use .ban <map> to ban a map";
                    break;
                case "team2_ban":
                    action = $"{ChatColors.Green}{matchzyTeam2.teamName}{ChatColors.Default} must now {ChatColors.Red}BAN{ChatColors.Default} a map.";
                    client = vetoCaptains["team2"];
                    stepMessage = $"Use .ban <map> to ban a map";
                    break;                                                       
                case "team1_pick":
                    action = $"{ChatColors.Green}{matchzyTeam1.teamName}{ChatColors.Default} must now {ChatColors.Green}PICK{ChatColors.Default} a map to play as map {matchConfig.Maplist.Count + 1}.";
                    client = vetoCaptains["team1"];
                    stepMessage = $"Use .pick <map> to pick a map.";
                    break;
                case "team2_pick":
                    action = $"{ChatColors.Green}{matchzyTeam2.teamName}{ChatColors.Default} must now {ChatColors.Green}PICK{ChatColors.Default} a map to play as map {matchConfig.Maplist.Count + 1}.";
                    client = vetoCaptains["team2"];
                    stepMessage = $"Use .pick <map> to pick a map.";
                    break;
            }
            bool captainValid = playerData.ContainsKey(client) && playerData[client].IsValid;
            string actionTeam = option.StartsWith("team1") ? "team1" : "team2";
            bool isSimulatingThisTeam = (actionTeam == "team1" && IsSimulatingTeam1) || (actionTeam == "team2" && IsSimulatingTeam2);
            if (!captainValid && !isSimulatingThisTeam)
            {
                Log($"[PromptForMapSelectionInChat] Invalid captain found with ID: {client}");
                return;
            }
            Server.PrintToChatAll($"{chatPrefix} {action}");

            string mapListAsString = string.Join(", ", matchConfig.MapsLeftInVetoPool.Select(matchConfig.GetMapDisplayName));
            Server.PrintToChatAll($"{chatPrefix} Remaining Maps: {mapListAsString}");

            if (captainValid)
                playerData[client].PrintToChat($"{chatPrefix} {stepMessage}");

            if (isSimulatingThisTeam)
            {
                string capturedOption = option;
                Team capturedTeam = actionTeam == "team1" ? matchzyTeam1 : matchzyTeam2;
                int capturedSide = actionTeam == "team1" ? (teamSides[matchzyTeam1] == "CT" ? 3 : 2) : (teamSides[matchzyTeam2] == "CT" ? 3 : 2);
                AddTimer(2.0f, () => {
                    if (!isVeto || GetCurrentMapSelectionOption() != capturedOption || matchConfig.MapsLeftInVetoPool.Count == 0) return;
                    string randomMap = matchConfig.MapsLeftInVetoPool[new Random().Next(matchConfig.MapsLeftInVetoPool.Count)];
                    bool success;
                    if (capturedOption.EndsWith("_ban"))
                    {
                        Server.PrintToChatAll($"{chatPrefix} [Simulate] Simulating ban for {ChatColors.Green}{capturedTeam.teamName}{ChatColors.Default}: {ChatColors.LightRed}{matchConfig.GetMapDisplayName(randomMap)}{ChatColors.Default}");
                        success = BanMap(randomMap, capturedSide);
                    }
                    else
                    {
                        Server.PrintToChatAll($"{chatPrefix} [Simulate] Simulating pick for {ChatColors.Green}{capturedTeam.teamName}{ChatColors.Default}: {ChatColors.Green}{matchConfig.GetMapDisplayName(randomMap)}{ChatColors.Default}");
                        success = PickMap(randomMap, capturedSide);
                    }
                    if (success) HandleVetoStep();
                });
            }
        }

        [ConsoleCommand("css_pick", "Picks map")]
        public void OnPickMapCommand(CCSPlayerController? player, CommandInfo? command) {
            if (player == null || command == null) return;
            if (command.ArgCount < 1) return;
            string mapArg = command.ArgByIndex(1);
            HandeMapPickCommand(player, mapArg);
        }

        [ConsoleCommand("css_ban", "Bans map")]
        public void OnBanMapCommand(CCSPlayerController? player, CommandInfo? command) { 
            if (player == null || command == null) return;
            if (command.ArgCount < 1) return;
            string mapArg = command.ArgByIndex(1);
            HandeMapBanCommand(player, mapArg);
        }

        [ConsoleCommand("css_vetostart", "Start the veto (the RPS-winning captain only)")]
        public void OnVetoStartCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null || !isVeto || !isVetoFirstChoicePending) return;
            if (player.UserId != vetoCaptains[vetoFirstChoiceTeam]) return;

            isVetoFirstChoicePending = false;
            // The ban order defaults to team1 acting first - only swap it when team2 won
            // RPS and chose to start themselves.
            if (vetoFirstChoiceTeam == "team2") SwapMapBanOrderTeams();
            Team chooserMatchzyTeam = vetoFirstChoiceTeam == "team1" ? matchzyTeam1 : matchzyTeam2;
            Server.PrintToChatAll($"{chatPrefix} {Localizer["matchzy.veto.chosetostart", chooserMatchzyTeam.teamName]}");
            HandleVetoStep();
        }

        [ConsoleCommand("css_vetoswap", "Give the first veto action to the opposing team (the RPS-winning captain only)")]
        public void OnVetoSwapCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null || !isVeto || !isVetoFirstChoicePending) return;
            if (player.UserId != vetoCaptains[vetoFirstChoiceTeam]) return;

            isVetoFirstChoicePending = false;
            // The ban order defaults to team1 acting first - only swap it when team1 won
            // RPS and gave the first action away.
            if (vetoFirstChoiceTeam == "team1") SwapMapBanOrderTeams();
            Team chooserMatchzyTeam = vetoFirstChoiceTeam == "team1" ? matchzyTeam1 : matchzyTeam2;
            Team otherMatchzyTeam = vetoFirstChoiceTeam == "team1" ? matchzyTeam2 : matchzyTeam1;
            Server.PrintToChatAll($"{chatPrefix} {Localizer["matchzy.veto.chosetoswap", chooserMatchzyTeam.teamName, otherMatchzyTeam.teamName]}");
            HandleVetoStep();
        }

        public void HandeMapBanCommand(CCSPlayerController player, string map)
        {
            if (!isVeto || isRpsPending || isVetoFirstChoicePending || SidePickPending() || player == null || map == null) return;

            int playerTeam = player.TeamNum;
            string currentTeamToBan;
            switch (GetCurrentMapSelectionOption()) {
                case "team1_ban":
                    currentTeamToBan = "team1";
                    break;
                case "team2_ban":
                    currentTeamToBan = "team2";
                    break;
                case "invalid":
                case "team1_pick":
                case "team2_pick":
                default:
                    return;
            }

            if (player.UserId != vetoCaptains[currentTeamToBan] && !IsSimulatingTeam1 && !IsSimulatingTeam2) return;

            if (!BanMap(map, playerTeam)) {
                PrintToPlayerChat(player, $"{map} is not a valid map.");
            } else {
                HandleVetoStep();
            }
        }

        public void HandeMapPickCommand(CCSPlayerController player, string map)
        {
            if (!isVeto || isRpsPending || isVetoFirstChoicePending || SidePickPending() || player == null || map == null) return;

            int playerTeam = player.TeamNum;
            string currentTeamToPick;
            switch (GetCurrentMapSelectionOption()) 
            {
                case "team1_pick":
                    currentTeamToPick = "team1";
                    break;
                case "team2_pick":
                    currentTeamToPick = "team2";
                    break;
                case "invalid":
                case "team1_ban":
                case "team2_ban":
                default:
                    return;
            }

            if (player.UserId != vetoCaptains[currentTeamToPick] && !IsSimulatingTeam1 && !IsSimulatingTeam2) return;

            if (!PickMap(map, playerTeam)) {
                PrintToPlayerChat(player, $"{map} is not a valid map.");
            } else {
                HandleVetoStep();
            }
        }


        public bool PickMap(string mapName, int team) {
            (bool mapRemoved, string mapRemovedName) = RemoveMapFromMapPool(mapName);

            if (!mapRemoved) return false;

            Team matchzyTeam = matchzyTeam1;

            if (team != 0) {
                matchzyTeam = (team == 2) ? reverseTeamSides["TERRORIST"] : reverseTeamSides["CT"];
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{matchzyTeam.teamName}{ChatColors.Default} picked {ChatColors.Green}{matchConfig.GetMapDisplayName(mapRemovedName)}{ChatColors.Default} as map {matchConfig.Maplist.Count + 1}");
            }

            matchConfig.Maplist.Add(mapRemovedName);

            var mapPickedEvent = new MatchZyMapPickedEvent
            {
                MatchId = liveMatchId,
                MapName = mapRemovedName,
                MapNumber = matchConfig.Maplist.Count,
                Team = (matchzyTeam == matchzyTeam1) ? "team1" : "team2",
            };

            Task.Run(async () => {
                await SendEventAsync(mapPickedEvent);
            });

            lastVetoTeam = (CsTeam)team;

            return true;
        }

        public bool BanMap(string mapName, int team) 
        {
            (bool mapRemoved, string mapRemovedName) = RemoveMapFromMapPool(mapName);

            if (!mapRemoved) return false;

            Team matchzyTeam = matchzyTeam1;

            if (team != 0) {
                matchzyTeam = (team == 2) ? reverseTeamSides["TERRORIST"] : reverseTeamSides["CT"];
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{matchzyTeam.teamName}{ChatColors.Default} banned {ChatColors.LightRed}{matchConfig.GetMapDisplayName(mapRemovedName)}{ChatColors.Default}");
            }

            var mapMapVetoedEvent = new MatchZyMapVetoedEvent
            {
                MatchId = liveMatchId,
                MapName = mapRemovedName,
                Team = (matchzyTeam == matchzyTeam1) ? "team1" : "team2",
            };

            Task.Run(async () => {
                await SendEventAsync(mapMapVetoedEvent);
            });

            lastVetoTeam = (CsTeam)team;

            return true;
        }

        public void AbortVeto()
        {
            // Todo: Add AbortVeto() when captain is disconnecting in-between veto
            Server.PrintToChatAll($"{chatPrefix} A team captain left during map selection. Map selection is paused.");
            Server.PrintToChatAll($"{chatPrefix} Type .ready when you are ready to resume map selection.");
            isPreVeto = true;
            isVeto = false;
            isVetoFirstChoicePending = false;
            isRpsPending = false;
            rpsChoices["team1"] = null;
            rpsChoices["team2"] = null;
            if (isPaused)
            {
                UnpauseMatch();
            }
            vetoCaptains = new(){
                {"team1", -1},
                {"team2", -1}
            };
            foreach (var key in playerReadyStatus.Keys) {
                playerReadyStatus[key] = false;
            }
            readyAvailable = true;
            isWarmup = true;
            StartWarmup();
        }

        public void FinishVeto() 
        {
            Server.PrintToChatAll($"{chatPrefix} The maps have been decided:");
            matchConfig.MapsLeftInVetoPool.Clear();

            if (isPaused) {
                UnpauseMatch();
            }

            // If a team has a map advantage, don't print that map.
            int mapNumber = matchConfig.CurrentMapNumber;

            for (int i = mapNumber; i < matchConfig.Maplist.Count; i++) {
                Server.PrintToChatAll($"{chatPrefix} Map {i + 1 - mapNumber}: {matchConfig.GetMapDisplayName(matchConfig.Maplist[i])}.");
            }

            string currentMapName = Server.MapName;
            string mapToPlay = matchConfig.Maplist[0];

            // In case the sides don't match after selection, we check it here before writing the backup.
            // Also required if the map doesn't need to change.
            SetMapSides();
            ExecuteChangedConvars();
            foreach (var key in playerReadyStatus.Keys) {
                playerReadyStatus[key] = false;
            }

            if (IsMapReloadRequiredForGameMode(matchConfig.Wingman) || mapReloadRequired || currentMapName != mapToPlay) {

                SetCorrectGameMode();
                float delay = 7.0f;
                mapChangePending = true;
                // Todo: Implement displayGotvVeto cvar
                // if (displayGotvVeto) {
                //     delay += GetTvDelay();
                // }
                AddTimer(delay, () => {
                    string nextMap = matchConfig.Maplist[matchConfig.CurrentMapNumber];
                    ChangeMap(nextMap, 3);
                });
            }
            isWarmup = true;
            readyAvailable = true;
            isPreVeto = false;
            isVeto = false;
            isVetoFirstChoicePending = false;
            StartWarmup();
        }


        public int GetTeamCaptain(string team)
        {
            Team matchzyTeam = team == "team1" ? matchzyTeam1 : matchzyTeam2;
            int teamSide = teamSides[matchzyTeam] == "CT" ? 3 : 2;
            foreach (var key in playerData.Keys)
            {
                if (!playerData[key].IsValid || playerData[key].IsBot) continue;
                if (playerData[key].TeamNum == teamSide) return key;
            }

            return -1;
        }

        public void SwapPlayersToTeams()
        {
            foreach (var key in playerData.Keys)
            {
                if (!playerData[key].IsValid || playerData[key].IsBot) continue;
                playerData[key].SwitchTeam(GetPlayerTeam(playerData[key]));
            }
        }

        public string GetCurrentMapSelectionOption()
        {
            // Number of banned maps must be: original pool - (current pool + picked);
            // 7 - (4 + 2) = 1; if 4 are left and 2 were picked, 1 must have been banned.
            int mapsBanned = matchConfig.MapsPool.Count - (matchConfig.MapsLeftInVetoPool.Count + matchConfig.Maplist.Count);
            int index = matchConfig.Maplist.Count + mapsBanned;
            if (index > matchConfig.MapBanOrder.Count - 1)
            {
                return "invalid";
            }
            return matchConfig.MapBanOrder[index];
        }
        public bool SidePickPending() {
            return matchConfig.MapSides.Count < matchConfig.Maplist.Count && matchConfig.MatchSideType == "standard";
        }

        public void HandleAutomaticSideSelection() {
            if (matchConfig.MatchSideType == "random") {
                matchConfig.MapSides.Add(new Random().Next(0, 2) == 0 ? "team1_ct" : "team1_t");
            } else {
                matchConfig.MapSides.Add(matchConfig.MatchSideType == "never_knife" ? "team1_ct" : "knife");
            }
        }

        public (bool, string) RemoveMapFromMapPool(string mapName) {
            string mapRemoved = "";
            int eraseIndex = -1;
            // First check if we have a single match with a substring.
            if (mapName.Length >= 4) {
                for (int i = 0; i < matchConfig.MapsLeftInVetoPool.Count; i++) {
                    mapRemoved = matchConfig.MapsLeftInVetoPool[i];
                    if (mapRemoved.IndexOf(mapName, StringComparison.OrdinalIgnoreCase) > -1) {
                        if (eraseIndex >= 0) {
                            eraseIndex = -1;  // If more than one match, reset and break.
                            break;
                        }
                        eraseIndex = i;
                    }
                }
            }
            // If no match or more than one match on substring, restart, this time only matching the full string.
            if (eraseIndex == -1) {
                for (int i = 0; i < matchConfig.MapsLeftInVetoPool.Count; i++) {
                    mapRemoved = matchConfig.MapsLeftInVetoPool[i];
                    if (mapRemoved == mapName) {
                        eraseIndex = i;
                        break;
                    }
                }
            }
            if (eraseIndex >= 0) {
                mapRemoved = matchConfig.MapsLeftInVetoPool[eraseIndex];
                matchConfig.MapsLeftInVetoPool.RemoveAt(eraseIndex);
                return (true, mapRemoved);
            }
            if (mapName.IndexOf("cobble", StringComparison.OrdinalIgnoreCase) > -1) {
                // Because Cobblestone is the only map that's actually misspelled, we re-run the code if the input contained
                // "cobble" but there was no match, this time using "cbble" instead, which will match de_cbble.
                return RemoveMapFromMapPool("cbble");
            }
            return (false, mapRemoved);
        }
        public void PromptForSideSelectionInChat(CsTeam team) {
            string mapName = matchConfig.Maplist[^1];
            Team matchzyTeam = (team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
            string teamString = (matchzyTeam == matchzyTeam1) ? "team1" : "team2";

            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{matchzyTeam.teamName}{ChatColors.Default} must now pick a side to play on {ChatColors.Green}{matchConfig.GetMapDisplayName(mapName)}{ChatColors.Default}");

            int client = vetoCaptains[teamString];
            if (playerData.ContainsKey(client) && playerData[client].IsValid)
            {
                playerData[client].PrintToChat($"{chatPrefix} Use .ct or .t to pick a side");
            }

            bool shouldSimulateSide = (teamString == "team2" && IsSimulatingTeam2) || (teamString == "team1" && IsSimulatingTeam1);
            if (shouldSimulateSide)
            {
                string capturedTeamString = teamString;
                Team capturedMatchzyTeam = matchzyTeam;
                AddTimer(2.0f, () => {
                    if (!SidePickPending()) return;
                    CsTeam randomSide = new Random().Next(2) == 0 ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
                    string sideLabel = randomSide == CsTeam.CounterTerrorist ? "CT" : "T";
                    Server.PrintToChatAll($"{chatPrefix} [Simulate] {ChatColors.Green}{capturedMatchzyTeam.teamName}{ChatColors.Default} picks {ChatColors.Green}{sideLabel}{ChatColors.Default}");
                    PickSide(randomSide, capturedTeamString);
                    HandleVetoStep();
                });
            }
        }

        public bool ValidateMapBanLogic() 
        {
            int numberOfPicks = 0;
            string option;
            for (int i = 0; i < matchConfig.MapBanOrder.Count; i++) {
                option = matchConfig.MapBanOrder[i];
                if (option == "team1_pick" || option == "team2_pick") {
                    numberOfPicks++;
                }
                if (numberOfPicks == matchConfig.NumMaps || i == matchConfig.MapsPool.Count - 2) {
                    break;
                }
            }

            // Example: In a Bo3, at least 2 of the options must be picks to avoid randomly selecting map order of remaining maps.
            if (matchConfig.NumMaps > 1 && numberOfPicks < matchConfig.NumMaps - 1) {
                Log($"[ValidateMapBanLogic] In a series of {matchConfig.NumMaps} maps, at least {matchConfig.NumMaps - 1} veto options must be picks. Found {numberOfPicks} pick(s).");
                return false;
            }

            if (matchConfig.MapsPool.Count - 1 != matchConfig.MapBanOrder.Count && numberOfPicks != matchConfig.NumMaps) {
                // Example: Map pool of 7 requires 6 picks/bans *unless* we have picks for all maps.
                Log($"[ValidateMapBanLogic] The number of maps in the pool {matchConfig.MapsPool.Count} must be one larger than the number of map picks/bans {matchConfig.MapBanOrder.Count}, unless the number of picks {numberOfPicks} matches the series length {matchConfig.NumMaps}.");
                return false;
            }

            return true;
        }

        public void HandleSideChoice(CsTeam side, int client) {
            if (!SidePickPending()) {
                // No side selection is done by players in this case.
                return;
            }
            Team team = matchzyTeam1;

            if (lastVetoTeam == CsTeam.Terrorist) team = reverseTeamSides["CT"];
            else if (lastVetoTeam == CsTeam.CounterTerrorist) team = reverseTeamSides["TERRORIST"];

            string pickingTeam = (team == matchzyTeam1) ? "team1" : "team2";

            if (client != vetoCaptains[pickingTeam]) {
                // Only captain can select a side.
                return;
            }
            PickSide(side, pickingTeam);
            HandleVetoStep();
        }

        public void PickSide(CsTeam side, string team) {
            if (side == CsTeam.CounterTerrorist) {
                matchConfig.MapSides.Add(team == "team1" ? "team1_ct" : "team1_t");
            } else {
                matchConfig.MapSides.Add(team == "team1" ? "team1_t" : "team1_ct");
            }

            int mapNumber = matchConfig.Maplist.Count - 1;

            string mapName = matchConfig.Maplist[mapNumber];

            string sideFormatted = (side == CsTeam.CounterTerrorist) ? "CT" : "T";

            Team matchzyTeam = (team == "team1") ? matchzyTeam1 : matchzyTeam2;

            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{matchzyTeam.teamName}{ChatColors.Default} elected to start as {ChatColors.Green}{sideFormatted}{ChatColors.Default} on {ChatColors.Green}{matchConfig.GetMapDisplayName(mapName)}{ChatColors.Default}.");

            var sidePickedEvent = new MatchZySidePickedEvent
            {
                MatchId = liveMatchId,
                MapName = mapName,
                MapNumber = matchConfig.Maplist.Count,
                Team = (matchzyTeam == matchzyTeam1) ? "team1" : "team2",
                Side = sideFormatted.ToLower()
            };
            Task.Run(async () => {
                await SendEventAsync(sidePickedEvent);
            });
        }

        public void GenerateDefaultVetoSetup()
        {
            Team startingVetoTeam = matchzyTeam1;
            if (lastVetoTeam == CsTeam.CounterTerrorist)
            {
                if (reverseTeamSides["CT"] == matchzyTeam1) startingVetoTeam = matchzyTeam2;
                if (reverseTeamSides["CT"] == matchzyTeam2) startingVetoTeam = matchzyTeam1;
            }
            else if (lastVetoTeam == CsTeam.Terrorist)
            {
                if (reverseTeamSides["TERRORIST"] == matchzyTeam1) startingVetoTeam = matchzyTeam2;
                if (reverseTeamSides["TERRORIST"] == matchzyTeam2) startingVetoTeam = matchzyTeam1;
            }
            switch (matchConfig.NumMaps)
            {
                case 1:
                    int numberOfBans = matchConfig.MapsPool.Count - 1;  // Last map either played by default or ignored.
                    for (int i = 0; i < numberOfBans; i++)
                    {
                        matchConfig.MapBanOrder.Add(
                        i % 2 == 0
                            ? (startingVetoTeam == matchzyTeam1 ? "team1_ban" : "team2_ban")
                            : (startingVetoTeam == matchzyTeam1 ? "team2_ban" : "team1_ban"));
                    }
                    break;

                case 2:
                    if (matchConfig.MapsPool.Count < 5)
                    {
                        matchConfig.MapBanOrder.Add(startingVetoTeam == matchzyTeam1 ? "team1_pick"
                                                                        : "team2_pick");
                        matchConfig.MapBanOrder.Add(startingVetoTeam == matchzyTeam1 ? "team2_pick"
                                                                        : "team1_pick");
                    }
                    else
                    {
                        matchConfig.MapBanOrder.Add(startingVetoTeam == matchzyTeam1 ? "team1_ban"
                                                                        : "team2_ban");
                        matchConfig.MapBanOrder.Add(startingVetoTeam == matchzyTeam1 ? "team2_ban"
                                                                        : "team1_ban");
                        matchConfig.MapBanOrder.Add(startingVetoTeam == matchzyTeam1 ? "team1_pick"
                                                                        : "team2_pick");
                        matchConfig.MapBanOrder.Add(startingVetoTeam == matchzyTeam1 ? "team2_pick"
                                                                        : "team1_pick");
                    }
                    break;

                default:
                    // Bo3 with 7 maps as an example.
                    // For this to work, a Bo3 requires a map pool of at least 5.
                    if (matchConfig.MapsPool.Count >= matchConfig.NumMaps + 2)
                    {  // 7 >= 3 + 2
                        int numberOfPicks = matchConfig.NumMaps - 1;    // 2 picks in a Bo3
                        // Determine how many bans before we start picking (may be 0):
                        int numberOfStartBans = matchConfig.MapsPool.Count - (matchConfig.NumMaps + 2);  // 7 - (3 + 2) = 2
                        if (numberOfStartBans > 0)
                        {                                          // == 2
                            for (int i = 0; i < numberOfStartBans; i++)
                            {
                                matchConfig.MapBanOrder.Add(
                                matchConfig.MapBanOrder.Count % 2 == 0
                                    ? (startingVetoTeam == matchzyTeam1 ? "team1_ban" : "team2_ban")
                                    : (startingVetoTeam == matchzyTeam1 ? "team2_ban" : "team1_ban"));
                            }
                        }

                        // After the initial bans, add the picks:
                        for (int i = 0; i < numberOfPicks; i++)
                        {
                            matchConfig.MapBanOrder.Add(
                                matchConfig.MapBanOrder.Count % 2 == 0
                                ? (startingVetoTeam == matchzyTeam1 ? "team1_pick" : "team2_pick")
                                : (startingVetoTeam == matchzyTeam1 ? "team2_pick" : "team1_pick"));
                        }

                        // Determine how many bans to append to the end (may be 0):
                        int numberOfEndBans = matchConfig.MapsPool.Count - 1 - numberOfPicks - numberOfStartBans;  // 7 - 2 - 2 - 1 = 2
                        if (numberOfEndBans > 0)
                        {                                                     // == 2
                            for (int i = 0; i < numberOfEndBans; i++)
                            {
                                matchConfig.MapBanOrder.Add(
                                matchConfig.MapBanOrder.Count % 2 == 0
                                    ? (startingVetoTeam == matchzyTeam1 ? "team1_ban" : "team2_ban")
                                    : (startingVetoTeam == matchzyTeam1 ? "team2_ban" : "team1_ban"));
                            }
                        }
                    }
                    else
                    {
                        // else we just alternate picks and ignore the last map.
                        for (int i = 0; i < matchConfig.NumMaps; i++)
                        {
                            matchConfig.MapBanOrder.Add(
                                i % 2 == 0
                                ? (startingVetoTeam == matchzyTeam1 ? "team1_pick" : "team2_pick")
                                : (startingVetoTeam == matchzyTeam1 ? "team2_pick" : "team1_pick"));
                        }
                    }
                    break;
            }
        }

        public void SkipVeto()
        {
            isWarmup = true;
            readyAvailable = true;
            isPreVeto = false;
            isVeto = false;
            isVetoFirstChoicePending = false;
            StartWarmup();
        }
    }
}

