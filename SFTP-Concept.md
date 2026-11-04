# SFTP on the Node's SSH Server: Concept

As of 2026-11-03, with the decisions of the same day in Part 3. Code: WWCP_Node `e05d41d`, Hermod `f3c0517c`, ChargingStation `3e1de56`.

Built on the same day (Part 4). Where the code went another way than this concept first said, the text below now says what was built: the limits (2.3), WinSCP's temporary name (2.4, F9), the kiosk's URL (2.4), `NodeSftpFileSystem` without `LocalSftpFileSystem` (2.8) and the TestKit's hook for the log files (2.8).

**Goal.** Every node already serves its command line over SSH. What is missing is the `sftp` subsystem, so that files can be moved efficiently in both directions: up, for example the logos of e-mobility providers that the display shows after a card is swiped; down, for example the log files. Not everybody may move everything: the node's role model decides who may read and who may write what.

Part 1 describes what is there, Part 2 the design, Part 3 the questions to decide, Part 4 the steps.

---

## Part 1: Current State

### 1.1 The node's SSH server

- Hermod's `SshServer`, configured in `WWCPNode.StartedSSHServer` (`WWCPNode.SSH.cs:453–460`) with a `ShellHandler` and nothing else. The class comment says so: "no shell of the machine, no files, no tunnels" (`:41–44`).
- A `subsystem sftp` request is refused by Hermod (`SshSessionChannel.cs:152`, `PolicyDeniedEvent("subsystem", "sftp", "not served")`), which `Audited` logs as "only the command line is served" (`WWCPNode.SSH.cs:901–903`).
- Who signs in is an account of the node, with one of its keys; with its password only where `ssh.passwords` says so, and never an account that has a second factor on the web interface. An account that holds none of the node's roles is refused (`NodeSSHAuthenticator`).
- A shell session is asked again whether it may still be in: before every command that needs a role, and every 5 s (`SSHSessionRecheckEvery`, `WhyNoLongerIn`). An account switched off or a key taken out ends the session with a sentence.
- Limits: 4 sessions per connection, 30 s to sign in, a keepalive every 60 s (`SSHServerLimits`).

### 1.2 What Hermod already has

| Part | What it does |
|---|---|
| `SftpServer` | SFTP version 3 over a channel, with the OpenSSH extensions `posix-rename`, `fsync`, `statvfs`, `fstatvfs`, `limits` |
| `ISftpFileSystem` | The seam behind the server: open, read, write, close, list, stat, mkdir, remove, rename, realpath, fsync, setstat |
| `LocalSftpFileSystem` | A real directory with a root jail (`..`, absolute paths and drive letters are refused), `FileShare.ReadWrite`, an optional `ReadOnly` |
| `SshAccessProfile` / `SftpPermissions` | Gates each operation: `Read`, `Write`, `Create`, `List`, `Delete`, `MakeDirectory`, `Rename`, `Stat` |
| `SftpLimits` | Per session: largest file, bytes written, files created, upload and download bandwidth; an overrun discards the partial upload |
| `SftpClient` | A client, for the tests |
| `SftpTranscript` | A JSON-lines record of every operation |

What it lacks for the node:

- **G1, one file system for everybody.** `SshServerOptions.SftpFileSystem`, `SftpProfile` and `SftpLimits` are the server's (`SshServer.cs:66–79`) and the same for every session (`:737–742`). Roles belong to accounts.
- **G2, operations rather than places.** `SftpPermissions` say "may write", not "may write the logos and only read the logs".
- **G3, no re-check.** Nothing ends an SFTP session whose account was switched off while it ran.
- **G4, not seen.** SFTP sessions are not among `CommandLineSessions`, so `who` and the SSH server page do not show them.
- **G5, no compression.** Hermod's SSH has no `zlib@openssh.com`; log files travel uncompressed.
- **G6, one request at a time** per channel (`SftpServer` doc comment). Clients pipeline, so the requests queue in the channel; enough for files of a few MB, not tuned for gigabytes.

### 1.3 Logos today

- `operator.logo` and `operator.emps[].logo` in the configuration file: an http(s) URL or a `data:image/…` URI of at most 64 Ki characters (`EMobilityProvider.MaxLogoLength`, `TryParseLogo`).
- No route of the web interface writes the `operator` section; only the file does. A CPO cannot change a logo at all today.
- After a swipe, `ProviderJSON` (`ChargingStation.Kiosk.cs:538`) gives the display `{ name, logo }`, matched by the card's UID prefix. The kiosk is served on a port of its own (2349 by default), so it is another origin than the web interface.
- A URL makes the screen depend on reaching somebody's web server from a station behind LTE; a `data:` URI makes the configuration file hard to read. A file on the station is the better home for a picture.

### 1.4 Logs today

- `FileLog`: one file per day, `logs/<prefix>-yyyy-MM-dd.log` (`WWCPNode.cs:104`).
- `SignedLog`, the metrological log: one file per day, `logs/metrological/<prefix>-yyyy-MM-dd.jsonl` (`SignedLog.cs:749`), **and in the same directory the private signing key `signing-key.pem`** beside `signing-key.pub.pem` (`LogSigner.cs:66–72`). Whatever shows that directory must show it by name, never by directory.
- Reading the log (Logs page, event stream, the `log` command) needs `ToReadTheLog` (`NodeHTTPAPI.Log.cs:67`): nothing beyond being signed in by default; a kind of node may ask for a permission.
- A whole day's file can only be had through the machine itself.

### 1.5 Roles

| Role | Permissions (abridged) |
|---|---|
| `admin` | `*:read`, `*:edit`, `*:run` |
| `viewer` | `*:read` |
| `cpo` (station) | `*:read`, … `display:edit`, `display:run`, `session:run`, … |
| `installer` (station) | `cpo` plus `power:edit`, `calibration:edit`, `meters:edit`, `meters:run` |

A permission is `resource:operation`; the configuration file may add roles and change what a role may do (`StationAccess.cs`, `Role.cs`).

---

## Part 2: Design

### 2.1 Principles

1. **No view of the machine.** SFTP shows a virtual tree of *areas*, each declared by the node or by its kind. Nothing else is reachable: not the configuration file (it holds the credentials of the connections), not the accounts, not the host key, not the command line's history, not the signing key.
2. **The role model answers, at every operation.** An area names the permission that reads it and the one that writes it, and the node asks `IsAllowed` at every operation, as the web interface does at every request. An account sees over SFTP what it sees on the web interface.
3. **Whole and checked, or not at all.** An upload is written under a hidden name, checked by the area's owner when it is closed, put in place with one rename, and only then announced. A half upload never reaches the display.
4. **Evidence is not edited.** The logs are read-only for every role, administrators included.
5. **Everything said.** Every upload, removal and download is in the node's log: who, what, how much, from where.
6. **Out means out.** A session that loses what let it in ends, as a shell does.

### 2.2 Areas

An `SftpArea` (WWCP_Node), declared before the node starts:

| Field | Meaning |
|---|---|
| `Name` | The top-level folder: `logs` |
| `Directory` | Where it lies on the disk |
| `Shows` | Which names are shown and readable: an allowlist (e.g. day files only), never "the whole directory" |
| `Read` | What listing and downloading need |
| `Write` | What uploading, replacing and removing need; none = read-only for everybody |
| `AcceptName`, `AcceptContent` | The owner's check: the name at open, the content at close; a refusal is a sentence |
| `Replaces` | The files a new one takes away: one picture to a provider, so `ionity.svg` takes `ionity.png` |
| `MaxFileSize`, `MaxFiles` | Largest file, most files |
| `Changed` | Raised after a file was put in place or removed |

The root `/` lists the areas the account may read or write, and only those. Opening an area the account may not read is refused with a sentence that says what it needs.

How the SFTP operations map:

| SFTP | Needs |
|---|---|
| `OPENDIR`, `READDIR`, `STAT`, `OPEN` for reading | the area's `Read`, and a name `Shows` admits |
| `OPEN` for writing | the area's `Write`, and `Accept` of the name |
| `CLOSE` after writing | `Accept` of the content; then rename into place; then `Changed` |
| `REMOVE` | the area's `Write` |
| `RENAME` | the area's `Write`, both names in the same area, the new one accepted |
| `MKDIR`, `RMDIR` | refused: an area's layout is its owner's |
| `SETSTAT` | the modification time of a file in a writable area (WinSCP sets it after every upload); permissions and owner refused |
| links | refused |

The areas proposed now:

| Path | Owner | Directory | `Shows` | `Read` | `Write` |
|---|---|---|---|---|---|
| `/logs` | node | `LogPath` | `<prefix>-yyyy-MM-dd.log` | the log's permission, `ToReadTheLog` (F2) | nobody |
| `/logs/metrological` | node | `MetrologicalLog.Path` | the day files and `signing-key.pub.pem`; never `signing-key.pem` | as `/logs` | nobody |
| `/logos` | station | `logos/` beside the configuration file, as `ssh/` | `operator.<ext>`, see 2.4 | `providers:read` (F3) | `providers:edit` (F3) |
| `/logos/providers` | station | `logos/providers/` | `<provider id>.<ext>`, see 2.4 | as `/logos` | as `/logos` |

### 2.3 Sessions

- **Hermod (G1).** `SshServerOptions` gets a per-session factory: given the signed-in session, it returns the file system, the profile, the limits and a token that ends the session, or null for "refused". The server-wide properties stay for the simple case.
- **Node.** For `subsystem sftp`: the account as in `ServeShellAsync`; a `NodeSftpFileSystem` for that account over the areas; the same re-check as the shell, at every operation and every `SSHSessionRecheckEvery` (G3); the session among the SSH sessions with its kind, `cli` or `sftp`, so that `who` and the SSH server page show it (G4).
- **Log.** At the start: "'cpo1' opened SFTP from 192.0.2.7." At the end, one line: "'cpo1' left SFTP after 3 min: 2 file(s) up (310 KiB), 1 down (1.4 MiB)." Each upload and removal as a notice, each download as one line when its handle is closed, not one per read.
- **Limits.** An area's limits are checked by `NodeSftpFileSystem` itself, at every write and at `OPEN`: a write past `MaxFileSize` is refused and the upload thrown away, and a new file past `MaxFiles` is refused. Hermod's `SftpLimits` is not used for them: when a limit is overrun, it closes the upload, which would put it in place, and then removes the target. No bandwidth cap.

### 2.4 Logos

Layout:

```
/logos/operator.svg
/logos/providers/<provider id>.png
```

- **The operator's logo too.** It is under `providers` with the others: for a session paid at the screen it stands where a provider's would (`ProviderJSON`).
- **Formats.** `.png`, `.jpg`/`.jpeg`, `.webp`, `.svg`. The content is checked (magic bytes; for SVG an XML parse), and the extension must match it.
- **One per provider.** `ionity.svg` put in place removes an `ionity.png` that was there.
- **Bound by name (F4).** The name must be `operator` or the id of a provider in the configuration file; anything else is refused at `OPEN`, before a byte is sent, with the known ids in the sentence. A provider id that is no file name (`/`, `\`, `..`, control characters) cannot have a file.
- **Limits (F5).** At most 512 KiB and 2048×2048 pixels (read from the header), at most `MaxProviders + 1` files.
- **SVG (F5).** Refused when it holds `<script>`, `<foreignObject>`, event attributes (`on…`), or references anything outside itself (`href`/`xlink:href` other than `#…`, `@import`, `url(http…)`). Served with `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; sandbox` and `X-Content-Type-Options: nosniff`.
- **WinSCP's temporary name (F9).** WinSCP uploads a file above 100 KB as `<name>.filepart` and renames it when it is done. Where `<name>` may be written, such an upload is checked at `CLOSE` as `<name>` would be, and then held for the session: its `STAT` sees it, no listing does. Renamed to `<name>`, it is put in place over the file of that name. One that is never renamed is thrown away with the session.
- **Served.** On the kiosk's port, under the kiosk API's path: `GET /api/kiosk/logos/<name>` and `/api/kiosk/logos/providers/<name>`, without sign-in, since the screen is public and shows the picture anyway. `ETag` is the SHA-256, and a kiosk that has the picture gets a 304. The kiosk's JSON says `"logo": "/api/kiosk/logos/providers/ionity.svg?v=<8 hex digits>"`. A name the area does not show is a 404; a path that climbs out of the folder is refused by Hermod with a 400 before any route.
- **Precedence (F4).** A file wins over the configuration file's `logo`; the URL or `data:` URI there stays as the fallback, and nothing written so far changes its meaning.
- **Changed.** The display shows the new logo at the next swipe, without a restart.
- **Log.** "'cpo1' put up the logo of 'ionity' over SFTP from 192.0.2.7: ionity.svg, 12 KiB, SHA-256 ab12…", tags `sftp`, `providers`. Removal likewise.

### 2.5 Logs

- `/logs` lists the day files of `LogPath` and the folder `metrological` where there is one; nothing else that may lie there.
- Read-only for everybody (F6). Today's file grows while it is read; a download gets what is there when it reaches the end.
- One line per downloaded file when its handle is closed: "'viewer1' downloaded cs-2026-11-02.log (1.4 MiB) over SFTP from 192.0.2.7.", tags `sftp`, `log`.
- The metrological day files with the public key are what is needed to check the chain somewhere else (F7).

### 2.6 Configuration

- `ssh.sftp`: `true` or `false` in the configuration file, a checkbox on the SSH server page, `sftp` in the SSH section of the JSON API, guarded by `ssh:edit` like the rest of that section.
- **Off unless it says `true`** (F1), for a node whose program says nothing more. A program may serve it unless the file says `false` (`SSHSettings.SftpByDefault`, WWCP_Node `4113a8e`), and since 2026-11-04 the charging station's does, as the energy meter's and the vehicle's do. Nothing is written into the file for that default. Switched on or off on the page, it takes effect for the next `subsystem sftp` request without a restart of the SSH server; sessions already open are ended when it is switched off, as by the re-check.
- The start's log line about the SSH server says whether SFTP is served; a refused `subsystem sftp` is logged as "SFTP is switched off on this station (ssh.sftp)", not as "only the command line is served".
- No switch on the command line; the file and the page are enough.
- SFTP needs the SSH server: with SSH off there is no SFTP, whatever `ssh.sftp` says.

### 2.7 Hermod

1. `SshServerOptions.SftpSessions`: `Func<SshSessionInfo, CancellationToken, ValueTask<SftpSessionOptions?>>`, `SftpSessionOptions` = file system, profile, limits, `Ends`. Null: refused, `PolicyDeniedEvent` as today.
2. The channel is closed when `Ends` fires.
3. Tests in `HermodTests/SSH/SFTP`: two sessions on one server with different file systems; a session ended by `Ends` during a transfer.

As a branch with a pull request, as Hermod's changes go.

### 2.8 WWCP_Node

- `SftpArea`, `WWCPNode.SftpAreas` and `AddSftpArea` for a kind before the start.
- `NodeSftpFileSystem`: the virtual root, the gates of 2.2, the limits of 2.3, staging and the rename into place. It works on each area's files by name and keeps them inside its directory itself (`SftpArea.FileOf`). It uses no `LocalSftpFileSystem`, whose constructor creates its root directory: a session that only looks would make an area's folder, such as `metrological` where no metrological log is written. A writable area's folder is made at its first upload.
- `WWCPNode.SSH.cs`: the factory, the re-check, the sessions with their kind, `Audited`'s sentence, the class comment.
- The `/logs` area.
- `ssh.sftp` in `SSHConfiguration`, the SSH server page and its route.
- The TestKit's conformance tests, over Hermod's `SftpClient`: an account without the log's permission sees no `/logs`; a viewer downloads a day file; nobody writes, removes or renames in `/logs`, an administrator included; `signing-key.pem` can be neither listed nor opened, nor reached by `..`; a session ends when its account is switched off during a transfer; the subsystem is refused while `ssh.sftp` is not `true`, which is the default, and open sessions end when it is switched off. The cases about the log files need a node that writes them; a kind gives one through the virtual `NewNodeWritingLogFiles`, and where it gives none they are inconclusive.
- `SftpUploadTests` in WWCP_Node_Tests, over an area of notes of their own, since no area of the node itself is written: staging, refusal, limits, `Replaces`, F9's held upload, times only, and the leftovers of a crash removed at the next start.
- Shared code: the vehicle gets SFTP with `/logs` at the same time. Announced before it lands.

### 2.9 ChargingStation

- A new resource `providers` in `StationAccess` (F3): read, which e-mobility providers this station knows and their logos; edited, a logo put up, replaced or removed - and later the providers themselves, where something other than the file edits them. The CPO gets `providers:edit`, and with it the installer; the viewer reads it through `*:read`. A configuration file that says differently what `cpo` may do has to name it, as for every other resource.
- The `/logos` area with its checks, the directory `logos/` beside the configuration file.
- The kiosk: the file before the configuration's `logo`, the route on the kiosk's port, the change to the screen.
- Tests: upload by a CPO; refused for a viewer, and for a role with `display:edit` but without `providers:edit`; an unknown provider id refused at `OPEN`; a broken PNG and an SVG with a script refused at `CLOSE`, with nothing left behind; the kiosk's JSON after the upload.

### 2.10 Not now

- **Firmware.** Needs signature checks and its place in OCPP's `UpdateFirmware`: a concept of its own.
- **The configuration file as a file.** It holds the connections' credentials, and writing it would go around "saved first, then in effect".
- **`exec`** (`ssh station command`) and the old scp protocol (`scp -O`). OpenSSH's `scp` speaks SFTP since 9.0 and works.
- **Compression** (G5) and **pipelining** in the server (G6).
- **A logo page** on the web interface: listing and replacing the logos in the browser.

---

## Part 3: Decisions

Decided by Achim on 2026-11-03. F1 and F3 differ from what this concept first recommended; F2 and F6–F8 were taken as recommended.

| # | Question | Decision | Why |
|---|---|---|---|
| F1 | Is SFTP on by default where SSH is on, or off until `ssh.sftp` says so? | **Off until `ssh.sftp: true`.** *Changed on 2026-11-04:* the charging station serves it unless the file says `"sftp": false`, as the energy meter and the vehicle do; a node whose program says nothing keeps it off. | After an update a station does nothing it did not do before; whoever wants files moved switches it on, in the file or on the SSH server page. The station's logos and log files are what SFTP is there for, so its program now says so itself (2.6). |
| F2 | What does downloading the logs need: what the Logs page needs (`ToReadTheLog`, today anybody signed in), or a permission of its own (`logs:read`)? | **The same as the Logs page.** | The file holds what the page shows; two answers to "may this account read the log" would drift apart. A kind that wants it narrower says so once, in `ToReadTheLog`. |
| F3 | What do the logos need: `display:read` / `display:edit`, or a new resource? | **A new station resource, `providers`.** | Dimming the screen and showing other companies' logos on it are different decisions. The resource is also where a later page that edits the providers themselves belongs. |
| F4 | How is a file bound to a provider: by name (`providers/<id>.<ext>`, the file winning over the configuration's `logo`), or by a reference in the configuration file (`"logo": "ionity.svg"`)? | **By name, the file winning.** | The CPO can put up a logo without touching the file, which only the administrator can; the configuration's `logo` stays as the fallback. |
| F5 | SVG yes or no; 512 KiB and 2048×2048 px? | **SVG yes, checked; the limits as proposed.** | Logos are mostly vectors and stay sharp on any screen; checked, and served sandboxed on the kiosk's own origin. |
| F6 | May an administrator remove old log files over SFTP? | **No.** | Logs are evidence, the metrological ones under calibration law; how long they are kept is the node's to decide, not a session's. |
| F7 | Does reading `/logs/metrological` need more than reading `/logs` (e.g. `calibration:read`)? | **No.** | The signed log is meant to be checked by others; it is tamper-evident, not secret. |
| F8 | More areas now (firmware, diagnostics, …)? | **No**, see 2.10. | |
| F9 | WinSCP uploads a file above 100 KB as `<name>.filepart` and renames it when it is done, while `/logos` takes only known names. Refuse that name, or hold the upload? | **Held in the session** (2.4). | Nobody has to change WinSCP's settings, and the check still comes before the file replaces the one there. Asked while step 2 was built. |

---

## Part 4: Steps

1. **Hermod**: the per-session factory and `Ends`, with tests (2.7). Branch and pull request.
2. **WWCP_Node**: areas, `NodeSftpFileSystem`, sessions, `/logs`, `ssh.sftp`, TestKit (2.8), on the Hermod of step 1.
3. **Superproject**: Hermod and WWCP_Node advanced.
4. **ChargingStation**: the `providers` resource, `/logos` and the kiosk (2.9).
5. **Superproject**: the station advanced.

Each step is committed on its own and builds and passes its tests on its own.

Done on 2026-11-03:
- Step 1: Hermod `1dcbdb06`, the merge of Vanaheimr/Hermod#220.
- Step 2: WWCP_Node `507ebf7`, and `08c7b3f` for F9 and the upload tests.
- Step 4: ChargingStation `b952b24`.
- Steps 3 and 5: one superproject commit pins all three, since both were ready.

Done on 2026-11-04: the station's program serves SFTP unless the file says `false` (F1 as changed), on WWCP_Node's `SSHSettings.SftpByDefault`.
