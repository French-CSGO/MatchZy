# MatchZy — Claude Instructions

## Vue d'ensemble

Plugin CS2 (Counter-Strike 2) pour la gestion de matchs compétitifs.
Écrit en C# sur CounterStrikeSharp API (.NET 8.0).
Fork French-CSGO — version 0.8.18+.

## Stack technique

- **Langage** : C# (.NET 8.0, nullable enabled, implicit usings)
- **Framework plugin** : CounterStrikeSharp.API v1.0.342
- **ORM** : Dapper v2.1.15
- **BDD** : SQLite (défaut) via Microsoft.Data.Sqlite + MySqlConnector (optionnel)
- **Sérialisation** : Newtonsoft.Json v13
- **CSV** : CsvHelper v30

## Build

```bash
dotnet publish -o package/addons/counterstrikesharp/plugins/MatchZy
```

Le CI/CD GitHub Actions produit 3 archives :
- `MatchZy-VERSION.zip` — plugin seul (CounterStrikeSharp requis séparément)
- `MatchZy-VERSION-with-cssharp-linux.zip`
- `MatchZy-VERSION-with-cssharp-windows.zip`

## Structure du projet

```
matchzy/
  MatchZy.cs                   # Classe principale du plugin, routing des commandes, état joueurs
  MatchManagement.cs           # Chargement config match, équipes, transitions d'état
  ConsoleCommands.cs           # Commandes console admin et joueur
  EventHandlers.cs             # Handlers des événements CS2 (round, bombe, joueurs)
  Utility.cs                   # Fonctions utilitaires, formatage (~95 KB)
  PracticeMode.cs              # Mode pratique : spawns, bots, grenades (~93 KB)
  DatabaseStats.cs             # Stats SQLite/MySQL (matchs, maps, joueurs)
  MapVeto.cs                   # Système de veto/ban de maps (BO1/BO3/BO5)
  BackupManagement.cs          # Backup/restauration de rounds (système Valve)
  Pausing.cs                   # Pause/unpause, pauses tactiques, vote d'équipe
  ReadySystem.cs               # Système de ready-up des joueurs
  DemoManagement.cs            # Enregistrement et upload des démos
  G5API.cs                     # Intégration Get5 Panel API (publication d'événements)
  Teams.cs                     # Gestion des équipes, swaps, verrouillage
  Coach.cs                     # Système de coaching, spawns fixes par map
  ConfigConvars.cs             # Variables de configuration (cvars)
  Events.cs                    # Classes d'événements sérialisables (webhooks/API)
  PlayerStatsTracking.cs       # Stats joueurs par round (K/D, damage, KAST, bombe)
  DamageInfo.cs                # Suivi des dégâts pour les rapports de round
  Constants.cs                 # Mappings types de grenades
  MatchZy.csproj               # Fichier projet .NET
  cfg/MatchZy/
    config.cfg                  # Configuration principale du plugin
    database.json               # Connexion BDD (SQLite par défaut, MySQL optionnel)
    admins.json                 # Définition des admins et leurs permissions
    knife.cfg                   # Config phase couteau
    warmup.cfg                  # Config warmup
    live.cfg                    # Config match live
    prac.cfg                    # Config mode pratique
    whitelist.cfg               # Whitelist joueurs
  lang/
    en.json, fr.json, de.json   # Localisation (12 langues)
    es-ES.json, pt-BR.json, ru.json, ja.json, hu.json, uz.json
    zh-Hans.json, zh-Hant.json
  spawns/coach/                 # Positions spawn coach par map (JSON)
    de_ancient.json, de_anubis.json, de_dust2.json, de_inferno.json, de_mirage.json
```

## Cycle de vie d'un match

1. **Warmup** — joueurs rejoignent, se configurent, se ready
2. **Round couteau** (optionnel) — détermine l'assignation des côtés
3. **Sélection des côtés** — si configuré
4. **Série de maps** — veto + jeu (BO1/BO3/BO5)
5. **Upload démo** — automatique après fin de map
6. **Stats sauvegardées** — BDD + export CSV

## Schéma de base de données

| Table | Contenu |
|-------|---------|
| `matchzy_stats_matches` | Métadonnées match (noms équipes, scores, type série) |
| `matchzy_stats_maps` | Résultats par map |
| `matchzy_stats_players` | Stats par joueur et par round (K/D, damage, utility damage…) |

## Événements publiés (Get5 API / Webhooks)

Définis dans `Events.cs` — sérialisés en JSON et envoyés aux webhooks :
- `series_start`, `series_end`
- `map_picked`, `map_vetoed`, `map_result`
- `round_end`, `game_paused`, `game_unpaused`
- `game_round_live`
- `demo_upload_ended`
- `player_death`, `bomb_planted`, `bomb_defused`

## Variables de config importantes (config.cfg)

| Variable | Description |
|----------|-------------|
| `matchzy_knife_enabled_default` | Activer le round couteau |
| `matchzy_minimum_ready_required` | Minimum de joueurs prêts |
| `matchzy_demo_recording_enabled` | Enregistrement automatique des démos |
| `matchzy_demo_path` | Dossier de stockage des démos |
| `matchzy_demo_name_format` | Template du nom de fichier démo |
| `matchzy_stop_command_available` | Autoriser la commande stop/restore |
| `matchzy_use_pause_command_for_tactical_pause` | Comportement de la pause |
| `matchzy_whitelist_enabled_default` | Activer la whitelist joueurs |

## Règles de développement

- Les handlers d'événements CS2 qui capturent des valeurs natives doivent le faire sur le thread principal **avant** tout `Task.Run` (voir commit fefbec6 — bug threading dans `EventRoundFreezeEnd`)
- Toute nouvelle commande joueur/admin s'enregistre dans `ConsoleCommands.cs`
- Tout nouvel événement publié vers G5API/webhooks doit avoir sa classe dans `Events.cs`
- Les messages affichés aux joueurs passent par le système de localisation (`lang/`)
- Ne jamais hardcoder de chaînes visibles par les joueurs — toujours utiliser les clés i18n
- Les nouvelles positions spawn coach (par map) vont dans `spawns/coach/`
