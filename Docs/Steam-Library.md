# Steam library access and ownership evidence

This guide records the [PRO-17 access and privacy decisions](https://linear.app/botlog/issue/PRO-17/define-read-only-steam-access-privacy-and-ownership-semantics) approved on 9 November 2026 and the Steam library behavior implemented under [PRO-49](https://linear.app/botlog/issue/PRO-49/integrate-opt-in-read-only-steam-library-and-ownership-evidence). The reader lives in `Utilities/SteamLibrary.cs`.

## Access and consent

Keep the opt-in Settings flow and the direct, read-only Steam Web API. The user supplies their SteamID64 or HTTPS Steam Community profile URL and a personal Steam Web API key. Numeric profile URLs supply the ID directly. Vanity URLs require one `ResolveVanityURL` request. The shared `HttpClient` calls `IPlayerService/GetOwnedGames/v1` on `api.steampowered.com` with `input_json`, without requesting names or icons. Valve documents the user key and SteamID requirements and conditions the result on game details visibility. Direct public-host access is the selected approach. [Player service](https://partner.steamgames.com/doc/webapi/IPlayerService), [Web API overview](https://partner.steamgames.com/doc/webapi_overview)

Open **Settings → Steam account**, enter your SteamID64 or profile URL and personal Web API key, then select **Link or change account**. The app sends the ID and key to Steam and saves the key in Windows Credential Manager after a successful link. SQLite and database backups contain the resolved account ID, AppIDs, and sync timestamp. Setting Game details to Public changes visibility outside the app. If you keep the library private or Steam returns no visible library, you can still use manual ownership. A previous successful snapshot remains visible with a warning. [Key registration and authentication](https://partner.steamgames.com/doc/webapi_overview/auth), [Steam Profile Privacy](https://help.steampowered.com/en/faqs/view/588C-C67D-0251-C276), [Backup behavior](Database.md)

OpenID verifies the SteamID. Library access still requires the Web API's own authentication and visibility checks. Publisher ownership APIs are scoped to the publisher's titles. The library reader uses the desktop app and Valve's API. [Valve authentication and ownership](https://partner.steamgames.com/doc/features/auth)

## Snapshot and refresh behavior

The app keeps one active snapshot with its SteamID, unique AppIDs and last successful sync in UTC. Games can refresh automatically at most once per day when enabled, with manual refresh available. A sync failure shows saved evidence with a warning, and a snapshot older than a day is marked potentially out of date. Missing credentials, hidden or malformed responses, duplicate AppIDs, and count mismatches leave the last successful snapshot intact. An explicit valid zero count can replace it. A successful account switch replaces the previous snapshot and key. A failed switch preserves the old account, and unlink removes the active snapshot and key. Keep the last-success time separate from attempt and error status, and keep request URLs and keys out of diagnostics.

Use **Sync now** in Settings or **Refresh catalog** in Games to retry. If the key or saved snapshot is unavailable, relink the account in Settings. **Unlink** removes the saved library and key. Each request has a 25-second deadline, an 8 MiB response limit, and redirects disabled. On HTTP 429, the app stores the retry time from a valid `Retry-After` header or waits one minute if no usable time is present. Manual sync, automatic sync, and linking honor that deadline, including after restart. The last successful snapshot and timestamp stay available during the cooldown.

## Ownership and filtering

With schema 2 active, the app intersects reviewed AppIDs for each stable catalog identity with the linked account's saved library. Catalog corrections, withdrawals and omissions take effect when the catalog reloads, without a Steam request. With schema 1, the compatibility path uses a single distinct AppID from the bundled map or reviewed Steam base-game offers. Conflicting IDs are ambiguous and non-base offers do not qualify for that legacy match. Games reports counts across local game rows. Unmatched rows either lack a qualifying mapping or have a reviewed AppID that Steam did not report. Game details gives the reason. [Shared catalog identity contract](../Tools/CATALOG.md#proposed-shared-identity-contract-pro-44), [PRO-45](https://linear.app/botlog/issue/PRO-45/persist-and-validate-reviewed-steam-app-id-catalog-mappings)

Manual ownership is the user's assertion. A matching Steam row is evidence that Valve returned the AppID for the linked account at the last sync. The reader currently requests played free games as well, and its AppID-only cache cannot classify purchases, free licences, shared access or temporary access. Absence is inconclusive. Keep purchase costs, DLC and edition entitlement separate.

**Owned** uses manual ownership. **Steam library** shows exact matches from the last successful sync, including saved matches with a warning after a failed sync or after one day. The row badge and Game Details show Steam evidence separately from the manual ownership control and purchase cost. **Playable now** includes manual ownership, current free offers, and current subscription inclusion with a selected membership. Sync, account changes, and unlinking preserve manual ownership, costs, bookmarks, and subscription settings.

## Local installation indicators

In **Settings → Steam account → Local Steam installation indicators**, choose
up to eight local `steamapps` folders. The app saves the selected paths in the
profile, including in database backups. Selecting **Disable install indicators**
clears them. The app reads only the selected folders and needs no Steam account
link. For a second or custom library, select its own `steamapps` folder.

Game Details shows a local installation when a reviewed AppID matches an
app manifest with a fully installed state and an existing install directory.
Missing, inaccessible, incomplete, and unfamiliar manifests yield an unknown
state. Steam's local manifest format can change. Installation is device
evidence only. The Owned and Playable now filters continue to use their
existing evidence. [PRO-50](https://linear.app/botlog/issue/PRO-50/optionally-show-read-only-local-steam-installed-game-indicators)

## Fixtures and implementation gate

| Synthetic input | Expected result |
| --- | --- |
| Visible `game_count: 1` with AppID `101` and one reviewed base-game match | Save Steam evidence for that match. Manual ownership and purchase cost retain their values. |
| Empty `response`, HTTP 429, or count `2` with only one AppID | Preserve the prior snapshot and last-success time. The 429 cooldown delays the next request. |
| Explicit `game_count: 0` with no entries | Replace only Steam evidence with an empty snapshot for that account. |
| Saved AppID `101` with competing catalog identity evidence, or only a matching name or install manifest | Report ambiguous or unmatched evidence as applicable. Do not assign Steam evidence or create a game. |
| Account A is linked and linking B fails, then succeeds | Keep A on failure. On success, use only B's library, even if A had matches B lacks. |

The Steam tests use synthetic HTTP responses, fake credentials and isolated storage. Live behavior still needs a separately authorized disposable-account check covering key setup, public and hidden Game details, Friends Only, individually private games, and visibility of free and shared titles.
