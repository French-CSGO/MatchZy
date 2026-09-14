using System.Text.Json.Serialization;
using Newtonsoft.Json.Linq;


namespace MatchZy
{

    public class MatchConfig
    {
        [JsonPropertyName("maplist")]
        public List<string> Maplist { get; set; } = new List<string>();

        [JsonPropertyName("maps_pool")]
        public List<string> MapsPool { get; set; } = new List<string>();

        // Optional technical-id -> display-name overrides (e.g. a Steam Workshop file id
        // like "3081538" -> "My Custom Map"). Everything that loads or reports a map
        // (host_workshop_map, stats, webhook events) must keep using the raw id from
        // Maplist/MapsPool - this is only ever read through GetMapDisplayName() for text
        // shown to players in chat.
        [JsonPropertyName("maps_display_names")]
        public Dictionary<string, string> MapsDisplayNames { get; set; } = new();

        [JsonPropertyName("maps_left_in_veto_pool")]
        public List<string> MapsLeftInVetoPool { get; set; } = new List<string>();

        [JsonPropertyName("map_ban_order")]
        public List<string> MapBanOrder { get; set; } = new List<string>();

        [JsonPropertyName("skip_veto")]
        public bool SkipVeto { get; set; } = true;

        [JsonPropertyName("match_id")]
        public long MatchId { get; set; }

        [JsonPropertyName("num_maps")]
        public int NumMaps { get; set; } = 1;

        [JsonPropertyName("players_per_team")]
        public int PlayersPerTeam { get; set; } = 5;

        [JsonPropertyName("min_players_to_ready")]
        public int MinPlayersToReady { get; set; } = 12;

        [JsonPropertyName("min_spectators_to_ready")]
        public int MinSpectatorsToReady { get; set; } = 0;

        [JsonPropertyName("current_map_number")]
        public int CurrentMapNumber { get; set; } = 0;

        [JsonPropertyName("map_sides")]
        public List<string> MapSides { get; set; } = new List<string>();

        [JsonPropertyName("series_can_clinch")]
        public bool SeriesCanClinch { get; set; } = true;

        [JsonPropertyName("scrim")]
        public bool Scrim { get; set; } = false;

        [JsonPropertyName("wingman")]
        public bool Wingman { get; set; } = false;

        [JsonPropertyName("match_side_type")]
        public string MatchSideType { get; set; } = "standard";

        [JsonPropertyName("changed_cvars")]
        public Dictionary<string, string> ChangedCvars { get; set; } = new();

        [JsonPropertyName("original_cvars")]
        public Dictionary<string, string> OriginalCvars { get; set; } = new();

        [JsonPropertyName("spectators")]
        public JToken Spectators { get; set; } = new JObject();

        [JsonPropertyName("remote_log_url")]
        public string RemoteLogURL { get; set; } = "";

        [JsonPropertyName("remote_log_header_key")]
        public string RemoteLogHeaderKey { get; set; } = "";

        [JsonPropertyName("remote_log_header_value")]
        public string RemoteLogHeaderValue { get; set; } = "";

        // Resolves a map's technical id to a human-readable name for chat/UI text, using
        // the API-provided override when there is one and falling back to the id itself
        // otherwise (a classic map id like "de_dust2" is already readable; a bare
        // Workshop file id like "3081538" has no other way to get a name).
        public string GetMapDisplayName(string mapId)
        {
            if (MapsDisplayNames != null && MapsDisplayNames.TryGetValue(mapId, out string? displayName) && !string.IsNullOrWhiteSpace(displayName))
            {
                return displayName;
            }
            return mapId;
        }
    }
}
