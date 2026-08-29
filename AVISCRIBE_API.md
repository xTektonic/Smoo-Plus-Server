# Aviscribe multiplayer API v1

Aviscribe online runs are a separate service on the SMOO+ game port. The first 20 bytes of a socket decide its route before a game `Client` is constructed. Aviscribe requests never register a player, consume a game slot, invoke packet handlers, or use game broadcasts.

## Framing

1. Send ASCII `AVISCRIBE_API_V1` followed by four null bytes.
2. Send a four-byte big-endian JSON byte length.
3. Send exactly that many UTF-8 JSON bytes.
4. Read the response using the same four-byte length plus JSON framing. The server then closes the connection.

Requests are limited to 64 KiB and responses to 4 MiB. There is one operation per connection; `waitForChanges` may remain open for the configured wait timeout.

The JSON envelope contains `version`, `requestId`, `operation`, optional `sessionId`, `participantId`, and `participantToken`, plus operation-specific `data`. Responses echo `requestId` and contain `ok` with either `data` or a structured `error`.

High-frequency moon events use compact integer fields:

- `id`: idempotency UUID
- `t`: event kind (`0` hint, `1` collection, `2` pending, `3` counted, `4` wrong, `5` remove)
- `k`: deterministic owning-kingdom catalog index
- `m`: kingdom-local moon ID

Manual classification is also numeric: `0` automatic, `1` counted, and `2` uncounted. Display names and localized moon data never travel to the server.

## Operations

The v1 operations are `capabilities`, `createRun`, `joinRun`, `resumeRun`, `publishEvents`, `waitForChanges`, `leaveRun`, `resetRun`, and `endRun`.

Run codes are Crockford Base32 in `XXXX-XXXX` form. Participant tokens contain 256 random bits. Only SHA-256 hashes of codes and tokens are persisted, and neither secret is logged.

## Operator controls

- `aviscribe list`
- `aviscribe inspect`
- `aviscribe state`
- `aviscribe end`
- `aviscribe purge`

Each server port permits one active Aviscribe multiplayer room because it represents the one SMOO+ game on that port. A room is removed immediately when its final player leaves or its run is ended. Rooms with no new room/run events are removed after `IdleExpirationMinutes`, even if a client continues sending wait heartbeats. The player limit always follows `Server.MaxPlayers`; changing the normal server limit also changes future Aviscribe joins.

Because each server port permits only one active room, operator commands automatically target that room and report when none exists. `aviscribe list` includes its room code, internal session ID, current run generation and configuration, player counts, moon-fact count, last event time, and remaining idle lifetime. Room codes persist across server restarts. Participant tokens remain hash-only. `aviscribe state` prints the shared moon facts for the current run.

The `Aviscribe` object in `settings.json` controls enablement, idle and optional maximum lifetime, presence and wait timeouts, persistence filename, and retention limits. The feature is enabled by default.
