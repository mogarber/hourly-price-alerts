# Hourly Price Alerts architecture

Hourly Price Alerts shows ComEd Hourly Pricing data on iPhone and sends push alerts when the price
crosses limits the user sets. No user accounts. Not affiliated with ComEd. The home screen label is
"Price Alerts".

```
ComEd feed ──> iPhone app (prices, read directly from ComEd)
    │              │ device token + alert rules (HTTPS)
    ▼              ▼
Azure Function (timer, every minute) ──> Table Storage
    │ new sample? evaluate rules
    ▼
   APNs ──> iPhone notification
```

The phone reads prices straight from ComEd, so the price screens keep working if the backend is down.

## Repo layout and ownership

| Path | Contents |
|---|---|
| `backend/HourlyPriceAlerts.Core` | net10.0 class library. No Azure dependencies: models, feed parser, price math, alert engine, APNs client, poll and device services, store interfaces |
| `backend/HourlyPriceAlerts.Functions` | .NET 10 isolated worker. Thin HTTP and timer functions, Table Storage store |
| `backend/HourlyPriceAlerts.Tests` | xUnit tests, in-memory store, fake HTTP handlers |
| `infra/` | Bicep for all Azure resources |
| `.github/workflows/` | `ci.yml` (tests), `deploy.yml` (OIDC deploy) |
| `ios/` | XcodeGen `project.yml`, `App/`, `Config/`, `Packages/HourlyPriceKit` |

## ComEd feed

- `GET https://hourlypricing.comed.com/api?type=5minutefeed` returns about 24 hours, newest first:
  `[{"millisUTC":"1793642400000","price":"1.4"}, ...]`. Both fields are strings. Price is ¢/kWh and can be negative.
- `GET https://hourlypricing.comed.com/api?type=currenthouraverage` returns one element of the same shape.
- Treat the feed as untrusted: tolerate gaps, duplicates, any ordering and unparseable rows (skip those rows). Sort ascending and de-duplicate by stamp after parsing.
- Send `User-Agent: HourlyPriceAlerts/<version>`.

### Hour buckets (verified against the live feed on 2026-11-02)

A sample stamped exactly on the hour **closes the previous hour**. An hour ending at `E` holds samples with stamps in `(E-60min, E]`.
For a sample at stamp `S`, `E` is `S` rounded up to the next whole UTC hour, or `S` if already on the hour.
Observed: stamp 18:00 returned 0.1 (mean of the 12 samples 17:05 to 18:00 was 0.14); stamp 18:10 returned 1.4 (mean of 18:05 and 18:10).

Compute averages locally from the 5-minute samples. Round every value to 1 decimal (midpoint away from zero) **before** comparing or showing it, so alerts never contradict the number displayed.

## API

Base path `/api`. JSON is camelCase. Bodies are capped at 16 KB. Never log tokens or secrets.

The Functions use anonymous auth level on purpose: a function key embedded in an open-source app would not be secret. Each install authenticates instead:

- The app generates `installId` (lowercase UUID) and `secret` (32 random bytes, 64 lowercase hex chars), and stores them in the Keychain.
- Every `/devices/...` call sends `Authorization: Bearer <secret>`.
- The first `PUT` of an `installId` stores the SHA-256 hex of the secret. Later calls compare with a fixed-time comparison. Mismatch or missing header is `401`.

| Method and route | Body | Result |
|---|---|---|
| `PUT /v1/devices/{installId}` | `{ "apnsToken", "apnsEnvironment": "sandbox"\|"production", "timeZone" }` | `200` device row created or updated |
| `DELETE /v1/devices/{installId}` | | `204` removes device and all its rules |
| `GET /v1/devices/{installId}/rules` | | `200 { "rules": [Rule] }` |
| `PUT /v1/devices/{installId}/rules/{ruleId}` | `Rule` | `200 Rule` |
| `DELETE /v1/devices/{installId}/rules/{ruleId}` | | `204` |
| `POST /v1/devices/{installId}/test` | | `202`, sends a test push; `429` if under 60 s since the last test |
| `GET /health` | | `200 { "lastSampleAt", "lastPollAt", "stale" }`, no auth; `stale` when the latest sample is over 20 min old |

Errors: `400 { "error": "<human readable>" }`, `401`, `404`, `429`.

Validation: `apnsToken` is 64 to 200 hex chars; `timeZone` is a valid IANA id; maximum 10 rules per device.

### Rule

```json
{
  "id": "lowercase uuid, must match the route",
  "kind": "above | below | move",
  "signal": "hourAverage | fiveMinute",
  "thresholdCents": 10.0,
  "direction": "up | down | either",
  "windowMinutes": 30,
  "cooldownMinutes": 60,
  "quietHours": { "startMinute": 1320, "endMinute": 420 },
  "enabled": true
}
```

- `thresholdCents`: -100 to 1000 for `above` and `below`; 0.1 to 200 for `move`. Maximum 1 decimal.
- `direction`: required for `move`, otherwise null.
- `windowMinutes`: required for `move` with `fiveMinute`: multiple of 5, from 5 to 180. Otherwise null.
- `cooldownMinutes`: 0 to 1440.
- `quietHours`: nullable. Minutes after local midnight, 0 to 1439, in the device's `timeZone`. Wraps past midnight when start is after end. Start equal to end means no quiet hours. Start is inclusive, end exclusive.
- Each `PUT` resets `armed` to true and keeps `lastFiredAt`.

## Alert engine

A pure function with no I/O:
`Evaluate(rule, state, samples, nowUtc, timeZone) -> (newState, stateChanged, notification?)`, where `state = { armed, lastFiredAt }` and a new rule starts armed.

1. Disabled rule: return unchanged.
2. Stale guard: if `nowUtc - latestSample > 20 min`, return unchanged.
3. Compute the signal. If it is unavailable, return unchanged.
   - `above`/`below` + `hourAverage`: mean of the current hour bucket. Needs at least 3 samples, so one 5-minute reading can't trip an hourly alert.
   - `above`/`below` + `fiveMinute`: the latest sample.
   - `move` + `hourAverage`: current hour mean minus the previous hour's mean. Current needs at least 3 samples, previous at least 6. `windowMinutes` is unused.
   - `move` + `fiveMinute`: take samples in `[latest-window, latest)`, at least 1. Up magnitude is `latest - min`; down magnitude is `max - latest`. `either` uses the larger, and a tie goes up.
   - For `hourAverage` moves, delta above 0 is up and below 0 is down. A rule with direction `up` ignores down moves, and vice versa.
4. Hysteresis `H`: 0.5¢ for `hourAverage`, 1.0¢ for `fiveMinute`.
   - `above`: met when `v >= t`, re-arms when `v <= t - H`.
   - `below`: met when `v <= t`, re-arms when `v >= t + H`.
   - `move`: met when `magnitude >= t`, re-arms when `magnitude < t / 2`.
5. Not armed: if the re-arm condition holds, set armed and stop. No notification on this tick.
6. Armed and met: if `nowUtc - lastFiredAt < cooldown`, or local time is in quiet hours, hold (stay armed, no state change) so it fires once allowed if still true. Otherwise notify, set `armed = false`, `lastFiredAt = nowUtc`.

`stateChanged` is true only when `armed` or `lastFiredAt` changed. Only then is the row written, which keeps storage transactions low.

### Notification text

Numbers use invariant culture, 1 decimal, and ASCII minus: `-1.4¢`. Times are `h:mm tt` (en-US) in the device's time zone, using the latest sample's stamp. `<signal>` is `Hourly average` or `5-minute price`.

| Case | Title | Body |
|---|---|---|
| above | `Price above 10.0¢` | `<signal> is 12.4¢ as of 2:05 PM.` |
| below | `Price below 2.0¢` | `<signal> is 1.5¢ as of 2:05 PM.` |
| move up, hour | `Price up 3.2¢` | `Hourly average is 9.1¢, up 3.2¢ from last hour.` |
| move down, hour | `Price down 3.2¢` | `Hourly average is 2.1¢, down 3.2¢ from last hour.` |
| move up, 5-min | `Price up 6.0¢` | `5-minute price is 9.1¢, up 6.0¢ in the last 30 minutes.` |
| move down, 5-min | `Price down 6.0¢` | `5-minute price is 2.1¢, down 6.0¢ in the last 30 minutes.` |

## Poller

Timer `0 * * * * *` (every minute):

1. Fetch the 5-minute feed. On failure or an empty result, log and exit. The next minute retries.
2. Update `lastPollAt`. If the latest stamp is not newer than `lastSampleAt`, exit. This avoids reading any rule on most runs.
3. Fetch `currenthouraverage` and log a warning if it differs from the local hour mean by more than 0.1¢. Keep going on failure. This keeps checking the bucket rule in production.
4. Load enabled rules, evaluate each, send notifications, then write changed state, then set `lastSampleAt`. A crash between send and write can produce a rare duplicate, which is preferable to a missed alert.
5. Isolate per-rule failures: one failure must not stop other rules.

## APNs

- HTTP/2 only (`HttpVersion.Version20`, `RequestVersionExact`) to `api.push.apple.com` (production) or `api.sandbox.push.apple.com` (sandbox), chosen per device.
- `POST /3/device/<token>` with `authorization: bearer <JWT>`, `apns-topic: <bundle id>`, `apns-push-type: alert`, `apns-priority: 10`, `apns-expiration: <now + 1 hour>`, `apns-collapse-id: <rule id>`.
- Payload: `{ "aps": { "alert": { "title", "body" }, "sound": "default" }, "ruleId": "...", "valueCents": 12.4 }`.
- JWT: ES256 (`ECDsa` with IEEE P1363 signature format), header `{alg, kid}`, claims `{iss: teamId, iat}`. Cache and reuse for 40 minutes. Thread safe.
- Send at most 20 pushes concurrently.
- `410`, or `400` with `BadDeviceToken` or `Unregistered`: delete the device and its rules.
- `403` with `ExpiredProviderToken`: refresh the JWT and retry once.
- `429` or `5xx`: retry once after a short delay, then log and move on.
- Config: `Apns__KeyId`, `Apns__TeamId`, `Apns__BundleId`, `Apns__PrivateKey` (PEM text).

## Storage (Azure Table Storage)

- `Devices`: PK `installId`, RK `device`. `SecretHash`, `ApnsToken`, `ApnsEnvironment`, `TimeZone`, `LastTestAt`.
- `Rules`: PK `installId`, RK `ruleId`. Rule fields plus `Armed`, `LastFiredAt`. Store decimals as strings or doubles consistently and round on read.
- `PollState`: PK `poll`, RK `state`. `LastSampleAt`, `LastPollAt`.
- Configured by `Storage__TableServiceUri` with `DefaultAzureCredential`, or `Storage__ConnectionString` for local development.

Only install IDs, push tokens, time zones and rules are stored. No names, emails, or locations.

## Azure resources (`infra/main.bicep`)

Flex Consumption (FC1, Linux, .NET 10 isolated, 512 MB, no always-ready instances), a storage account (deployment container and tables; identity-based access, shared keys disabled), Key Vault (RBAC) holding the APNs key through a Key Vault reference, Log Analytics and Application Insights with a daily cap, system-assigned identity with least-privilege roles, HTTPS only, and a subscription budget alert. No VNet: the cost isn't justified for this data.

## iOS app

- iOS 17, SwiftUI, Swift Charts, XcodeGen. No third-party dependencies. Follow the conventions in `../little-baby-log`: `project.yml`, `Config/App.xcconfig` with optional git-ignored `Config/Local.xcconfig`, and logic in a Swift package testable with `swift test`.
- Default bundle id `io.github.mogarber.hourlypricealerts`. The team comes from `Local.xcconfig`. `API_BASE_URL` comes from xcconfig through Info.plist.
- `aps-environment` entitlement: `development` for Debug, `production` for Release. `apnsEnvironment` sent to the API is `sandbox` for Debug, `production` for Release.

### HourlyPriceKit (Swift package, no UIKit or SwiftUI)

Feed parser, price math matching the bucket and rounding rules above, `ComEdClient`, `BackendClient` protocol with a real and a mock implementation, `Rule` models matching the API JSON, number formatting and `PriceBand`. Unit tests must cover the bucket examples above.

### Screens

- **Prices tab:** large current hour average with its band shown as text and color, never color alone; the latest 5-minute price; last updated time; a 24-hour chart with an Hourly (bars, default) and 5-minute (line) picker and drag to inspect. Refreshes on appear, on pull, and every 60 s only while the scene is active. Shows the last cached data with a clear stale label when offline. Note on screen that ComEd bills on the hourly average.
- **Alerts tab:** rule list with enable toggles, swipe to delete, add and edit form. The form picks a kind in plain language ("Price goes above", "Price goes below", "Price moves by"), signal (Hourly average is the default, with a one-line note that 5-minute prices are noisy), threshold, direction and window where relevant, cooldown, and quiet hours. Shows a banner with a link to Settings when notifications are denied, and a clear state when `API_BASE_URL` is empty.
- **Settings tab:** send test notification, delete my data (confirm, then `DELETE` device and clear local state), data source attribution, "Not affiliated with ComEd", source code link, privacy summary. **No tip or donation links inside the app** (App Store guideline 3.2.1).

### Registration flow

Adding the first alert explains why notifications are needed, then requests permission, registers for remote notifications, `PUT`s the device, and then `PUT`s the rule. Re-`PUT` the device on launch when the token or time zone changed. Show a clear error if a token can't be obtained. Rules are cached locally for offline display; the server is the source of truth.

Launch argument `-MockBackend` swaps in the mock `BackendClient` and skips the permission flow, so the alert screens can be exercised in the simulator.

### Bands (defaults, shown as relative labels only)

Below 0 `Negative`, below 3 `Low`, below 7 `Moderate`, below 14 `Elevated`, otherwise `High`. Must read correctly in light and dark mode.
