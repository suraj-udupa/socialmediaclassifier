# Social Classifier

A .NET 10 command-line tool that reads **your own posts and replies on X**, optionally scores each one with a
[Laya](#laya) classifier, and writes the results to an Excel file. It can also **delete your replies** from X.

Categories scored per post (configurable in `appsettings.json`): Trolling, Intolerance, Aggressive, Racism,
Hate Crime, Hate, Harassment.

## Projects

| Project | Purpose |
|---|---|
| `SocialClassifier.Core` | Models, interfaces, the analysis pipeline and the delete pipeline |
| `SocialClassifier.XApi` | X API v2 client: timeline reader, reply deleter, OAuth 1.0a signing |
| `SocialClassifier.Laya` | Laya classifier client |
| `SocialClassifier.Excel` | Excel export (`Analysis` sheet) and deletion log (`Deleted` sheet) |
| `SocialClassifier.Cli` | The executable; config in `appsettings.json` |
| `SocialClassifier.Tests` | xUnit tests |

## Requirements

- .NET 10 SDK
- An X developer app with credits (pay-per-use) — only for the X modes
- A running Laya server at `http://localhost:8000` — only when Laya is enabled
  <!-- TODO: add how to install/start Laya -->

## Secrets (X credentials)

Get four values from the X developer portal for your app: **API Key** (consumer key), **API Key Secret**,
**Access Token** and **Access Token Secret**.

For **deleting**, the app must have **Read and write** permission. Change it in the portal, then
**regenerate the access token and secret** — old tokens keep read-only permission and X answers `403`.

Store them with .NET user secrets (kept outside the repo). From the repo root:

```powershell
cd src/SocialClassifier.Cli
dotnet user-secrets set "XApi:ConsumerKey"       "<api key>"
dotnet user-secrets set "XApi:ConsumerSecret"    "<api key secret>"
dotnet user-secrets set "XApi:AccessToken"       "<access token>"
dotnet user-secrets set "XApi:AccessTokenSecret" "<access token secret>"
dotnet user-secrets list      # check what is set
```

Alternative: put an `XApi` section in `src/SocialClassifier.Cli/appsettings.Local.json`. That file is in
`.gitignore`; never commit credentials. Optionally set `XApi:UserId` to skip one `/users/me` call.

## Running

All commands run from the repo root. In Rider, put the arguments under
*Run → Edit Configurations → Program arguments*.

### 1. Sample run (no X, no cost)

```powershell
dotnet run --project src/SocialClassifier.Cli
```

Reads `sample-posts.json` (`Input:Source` defaults to `json`), classifies it with Laya, writes the Excel file.
The date filters do not apply to the JSON source.

### 2. Analyze your posts from X

```powershell
dotnet run --project src/SocialClassifier.Cli -- --Input:Source=x --Input:From=2020-01-01
dotnet run --project src/SocialClassifier.Cli -- --Input:Source=x --Input:From=2026-11-01 --Input:To=2026-11-07
```

- `From` / `To` are **UTC calendar days**; `To` is inclusive. Omit `To` to run up to now.
- Fetches your posts and replies (retweets excluded) and, for replies, the text of the post you replied to.
- Output: `output/analysis.xlsx` (relative to the working directory — in Rider usually `bin/Debug/net10.0/output/`).

### 3. Analyze without Laya

```powershell
dotnet run --project src/SocialClassifier.Cli -- --Input:Source=x --Laya:Enabled=false
```

Or set `"Laya": { "Enabled": false }` in `appsettings.json`. Laya is never contacted and need not be running.
The Excel file then has no Decision Probability, Top Label or category columns — just the posts.

### 4. Delete your replies

```powershell
# Dry run (default): lists the replies that would be deleted, deletes nothing
dotnet run --project src/SocialClassifier.Cli -- --Mode=delete --Input:From=2020-01-01

# Live: actually deletes
dotnet run --project src/SocialClassifier.Cli -- --Mode=delete --Input:From=2020-01-01 --Delete:Confirm=true
```

- Deletes **replies only**; your original posts are never touched.
- **Deletion is permanent.** Always do the dry run first, and try a one-day range before the full range.
- Laya is not needed in this mode.
- Progress prints as `deleted i/N`. Each deletion is logged to the **`Deleted`** sheet of the Excel file
  (`Output:ExcelPath`), saved every 25 deletions and when the run ends or fails.
- Re-running skips IDs already on the `Deleted` sheet, so an interrupted run can be resumed.
- A reply X reports as already gone (404) is logged as "Already gone".
- Running the analysis again **overwrites** the Excel file and its `Deleted` sheet. Use a different
  `--Output:ExcelPath` for analysis runs if you want to keep the log.

## Output

`Analysis` sheet columns: Main Post, My Reply, Post Type, Decision Probability, Top Label, one column per
category, Date (UTC), Tweet ID, URL. (The probability, label and category columns are omitted when Laya is disabled.)

`Deleted` sheet columns: Tweet ID, My Reply, Deleted At (UTC), Result.

## Cost and limits

Prices and limits are from the X docs and can change — check the
[pricing page](https://docs.x.com/x-api/getting-started/pricing) and
[rate limits](https://docs.x.com/x-api/fundamentals/rate-limits).

- Post reads: $0.005 per resource; **owned reads** (your own timeline) $0.001 per resource.
  Whether the parent posts fetched for replies are billed separately is not documented — check the
  Developer Console usage page after a small test run.
- A resource is billed once per **UTC day** (dedup window resets at midnight UTC — 6 PM CST / 5 PM CDT).
  The docs call this a "soft guarantee".
- The timeline endpoint returns only your most recent ~3,200 posts; older ones cannot be fetched or deleted this way.
- Deleting a post is limited to **50 per 15 minutes** per user. The delete price is not listed on the pricing page.
- Credits are prepaid in the Developer Console.

## Configuration reference

Every setting can be overridden on the command line as `--Section:Key=value`.

| Setting | Default | Meaning |
|---|---|---|
| `Mode` | _(unset)_ | `delete` switches to delete mode |
| `Delete:Confirm` | _(unset)_ | `true` makes delete mode really delete; otherwise dry run |
| `Input:Source` | `json` | `json` or `x` |
| `Input:JsonFile` | `sample-posts.json` | File used when source is `json` |
| `Input:From` / `Input:To` | _(empty)_ | UTC days; `To` inclusive |
| `Output:ExcelPath` | `output/analysis.xlsx` | Excel file (analysis and deletion log) |
| `Laya:Enabled` | `true` | `false` skips classification |
| `Laya:BaseUrl` | `http://localhost:8000` | Laya server |
| `Laya:Model` | `english` | Laya model |
| `Laya:MaxRetries` | `3` | Retries on Laya 503 |
| `Laya:RawLogPath` | `output/laya-raw.jsonl` | Raw Laya responses, one per line |
| `XApi:*` | — | Credentials, see [Secrets](#secrets-x-credentials) |
| `Categories` | 7 built in | `Key`, `DisplayName`, `Instruction` per category |

## Laya

Laya answers one yes/no question per category for each post; the answer is the probability of "yes". The top
category and its probability become the row's label and Decision Probability.

## Tests

```powershell
dotnet test
```

## Troubleshooting

| Symptom | Cause |
|---|---|
| Exit code 1, "No categories configured" | `Categories` is empty and Laya is enabled |
| Exit code 2, "Laya is not reachable" | Start Laya, fix `Laya:BaseUrl`, or set `Laya:Enabled=false` |
| Exit code 3, "X credentials missing" | Set the four `XApi:*` secrets |
| `X API ... returned 401` | Wrong or expired credentials |
| `X API ... returned 402` / `403` on reads | No credits, or the app lacks access |
| `403` when deleting | App is read-only — set Read and write and regenerate the tokens |
| `rate limited, waiting Ns` | Normal; the app waits for X's reset and retries (up to 3 times) |
| Ran with no arguments and got 3 posts | Default source is the sample JSON; add `--Input:Source=x` |
