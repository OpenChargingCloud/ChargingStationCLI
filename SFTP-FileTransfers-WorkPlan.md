# SFTP for Logfile Uploads and Firmware Downloads - Work Plan

**Status:** 2026-11-13. Step 1 is built (Hermod, branch `sftp/stream-transfers`, not pushed); the rest is plan.
**Concept:** the white paper [SFTP for Logfile Uploads and Firmware Downloads](https://github.com/OpenChargingTechnology/Whitepapers/blob/master/SFTP-FileTransfers/README.md)
(`Whitepapers/SFTP-FileTransfers/README.md`, version 0.2). This file says how the paper is built
in the libraries of this repository, in which order, and what has to be decided first. Section
numbers without a name are the paper's.

---

## 1. Goal

A `GetLog`, `GetDiagnostics`, `UpdateFirmware`, `SignedUpdateFirmware` or `PublishFirmware` with an
`sftp://` URL is carried out only against a server the node holds as an SSH peer:

1. host, port, login and path are read from the URL;
2. the peer of that host and port is looked up in the node's own configuration, and with it whether
   it may be used for this transfer, under this login, on this path;
3. the host key comes from the peer (pins or CAs), **never** from the first handshake;
4. the node signs in with the key it made for that login at that peer, moves the file, and closes
   the connection - or, where the peer says `Shared` and the login is the one of the node's
   connection to it, moves the file over one more channel of that connection.

No peer, no transfer: the request is `Rejected`, and reported as a security event.

**A file is a file** (Achim, 2026-11-13): a `DiagnosticsLog`, a `SecurityLog` and a firmware image
are files to be transferred. What is in a log, how it is packed, whether an image's signature
holds and how it is installed is OCPP's logic and the vendor's, and not part of this plan. The
plan builds the transport and the two places where that logic meets it: *here is a file and its
name, put it there* and *fetch that file, here it is*.

---

## 2. What is there today

| Part | Where | State |
|---|---|---|
| SFTP client | `libs/Hermod/Hermod/SSH/SFTP/SftpClient.cs`, `SftpFileStream.cs`; `SshClient.OpenSftpClientAsync` | upload, download (whole file as bytes), streams, `PosixRenameAsync`, `LimitsAsync`, `StatAsync` |
| SSH peers of a node | WWCP_Node `8da01db`: `SecureShell/SSHPeer.cs`, `WWCPNode.SSHPeers.cs` | a peer is host, port and one shared connection; found by host; no file transfer attributes. Not in this checkout's pin (`5bc3efc`) yet |
| SSH peers of the station | ChargingStation `2229566`: `ChargingStation.SSHPeers.cs` | the hosts of its `ssh://` connections; pins in the connection's entry. Not pinned here yet (`a969bed`) |
| `SSHPeerController` | paper *OCPP over SSH*, sections 7.3 and 12.3 | described as built for OCPP 2.1 and the configuration file; **not found on `origin/master` of WWCP_OCPP or ChargingStation on 2026-11-13** - check with the session that wrote it before step 3 |
| The station's `SFTP` key | `ChargingStation.SSHClient.cs` (`id_ed25519_sftp`) | made, shown, rotated; nothing signs in with it |
| Pin sets and their signers | `ChargingStation.SSHClient.cs`, `pinSetSigners.json` | for the pins of `ssh://` connections |
| `GetLog`, `UpdateFirmware`, `PublishFirmware` | WWCP_OCPP `TestChargingStationNode.cs` (2.1) | answered `Accepted`, nothing transferred |
| `GetDiagnostics`, `UpdateFirmware` (1.6) | WWCP_OCPP `TestChargePointNode.cs` | handlers commented out |
| `FileTransferProtocols` | WWCP_OCPP `AChargingStationNode.cs` | `[ HTTPS ]` |
| The station itself | `libs/ChargingStation` | no handler for any of the five requests, no status notification |
| SFTP server areas | WWCP_Node `NodeSftpFileSystem.cs`, `SftpArea.cs`; [SFTP-Concept.md](SFTP-Concept.md) | logs and logos for accounts; firmware and diagnostics areas were put off (2.10 there) |
| `InstallSSHKey` at the Local Controller | LocalController `864e57e` | the station's own purpose only |

---

## 3. Decisions needed first

Decided by Achim on 2026-11-13 (the paper's section 13.1):

| | Decision | Follows |
|---|---|---|
| 1 | File servers are instances of `SSHPeerController` only, found by address and port. | An LC dialled over `ssh://` is entered there as well. |
| 2 | **One key per file server and login**, not one `SFTP` key. | A key store per peer and login in WWCP_Node; `FileTransferPublicKey[<login>]`; `SignSSHKey`/`SSHKeySigned` get `peer` and `login`; the station's single `id_ed25519_sftp` goes. |
| 3 | Writes of a file server over OCPP only when signed by a signer of pin sets. | The signers the station has for `SSHClientController` judge `SSHPeerController` writes too. |
| 4 | **A connection of its own, or a channel** of the node's connection to the peer (`FileTransferConnection` `Shared`, same login). | `SharedSSHConnectionTo(Host, "sftp")`; the server side must grant a station login `sftp`. |
| 5 | Logs and firmware images are only files; everything else is OCPP or vendor logic. | `Upload` and `Download`, no log types. The transport packs nothing, verifies no signature, installs nothing. |

Open (the paper's section 13.2), each with a proposal the steps follow until decided otherwise:

| | Question | Proposal | Touches |
|---|---|---|---|
| 1 | A password in the URL? | rejected | 2 |
| 2 | `Record`/`Accept` on a test bench? | no | 2 |
| 3 | Local Controller as file server of its stations' uploads? | later | 8 |
| 4 | Standardization | with OCPP over SSH | - |

---

## 4. Steps

One commit per step and repository, tests in the same commit, as for the NTS steps. Library work is
done in a scratch composite of clean clones, not in the shared `libs/`.

### Step 1 - Hermod: what the client lacked (built)

Hermod `2de0a48d` on the branch `sftp/stream-transfers` of a scratch clone, on `a58a53af`; not
pushed, to become a pull request. `SshClient` needed nothing: it offers the keys it is given and
no password, takes the node's `DNSClient`, requires strict KEX, and already told a refused login
and a refused subsystem apart.

- `SftpClient.UploadAsync(RemotePath, Stream, Exclusive, Progress, SyncToDisk)`: a stream,
  pipelined, never held in memory; `Exclusive` opens with `CREAT | EXCL`. The in-memory file
  system ignored that flag, and refuses a file that is there now.
- `SftpClient.DownloadAsync(RemotePath, Stream, Offset, MaxSize, Progress)`: from an offset on;
  a file over `MaxSize` is refused before a byte is read (`SftpFileTooLargeException`); a file
  that ends before the size the server said is an error.
- **A fault found on the way:** a READ answered by any status was taken for the end of the file,
  so a download that lost its permission half way returned the first half as the file. Only
  `SSH_FX_EOF` ends a file now.
- `SshHostKeyRejectedException` (a `SshWireException`), with the key shown: section 5.4 never
  retries it, and the security event names the fingerprint.
- Tests: `SftpTransferTests` (11), Hermod's SSH tests 644 green. Four checks mutated at once
  turned three tests red; which one stayed green was not looked up - probably the host-key
  exception in `SshHandshake`, which `HandshakeLoopbackTests` covers since it names the new type.
- Not done: host certificates (`HostCAs`) are checked by the policy the caller passes; whether
  the node's policy does is step 2. No run against OpenSSH under WSL yet.

### Step 2 - WWCP_Node: the file transfer of a node

What every node does alike lives here, so that the station and the Local Controller share it.

- **`SSHPeer` gets what a file server is**: `FileTransfers` (`Upload`, `Download`),
  `FileTransferUsers`, `UploadPaths`, `DownloadPaths`, `FileTransferConnection`; peers found by
  host **and port** (section 4.3). Announce to the session that owns `SSHPeer` first.
- **A key store per peer and login** (decision 2): made when a login is added, deleted with it,
  owner-only like the host key; current and pending key, confirmed or not; the public lines for
  the device model and the page.
- **Configuration file**: the peers' new entries, read and written with the others
  (`"fileTransfers"`, `"fileTransferUsers"`, `"uploadPaths"`, `"downloadPaths"`,
  `"fileTransferConnection"`).
- **`SftpLocation`**: the URL of chapter 3, parsed and refused there - password, query, fragment,
  `..`, control characters, `;fingerprint=`.
- **`FileTransferPolicy.Judge(Location, Direction)`**: the four lookups of section 4.1 and the five
  cases of section 5.1, as one function that returns the peer, the login and the key, or the
  reason. It reads no `OnPinMismatch`, and refuses a peer whose key is learned and not pinned.
- **`SftpTransfer`**: section 5.2 - resolve with the node's `DNSClient`, a connection of its own,
  strict KEX, host key from the peer, the key of the login, the `sftp` subsystem; or section 5.3 -
  `SharedSSHConnectionTo(Host, "sftp")` where the peer is `Shared` and the login is that
  connection's; `.part` and rename for
  uploads, offset and limit for downloads, time-outs, cancellation, retries by the table of
  section 5.4.
- The interface to the logic above is two calls and their events: *upload this file (a stream and
  a name) to that location* and *download that location into this place*; started, progress,
  ended and why. Nothing in it knows what a log or a firmware image is.
- Tests in WWCP_Node_Tests against a node's own SFTP server; what every node kind must do alike
  goes into the TestKit.

### Step 3 - WWCP_OCPP: device model, reason codes, events

- `SSHPeerController`: the seven variables of section 8.1 and the three of 8.2, through the
  `DeviceModelBridge` of WWCP_OCPP_Node; writes only when signed (decision 3).
- `SignSSHKey` and `SSHKeySigned` get `peer` and `login`; the custom trigger
  `SignSSHKey:SFTP:<peer>:<login>` (section 9.2).
- `OCPPCommCtrlr.FileTransferProtocols` lists `SFTP`, and so does
  `SupportedFileTransferProtocols` of 1.6.
- The reason codes `InvalidURL` and `FileServerNotPermitted`; the security events of section 8.5.
- OCPP 1.6: the `sshPeer.<name>.*` keys of section 8.3, beside the ones *OCPP over SSH* 9.5 has.
- The test nodes: `GetLog`, `UpdateFirmware` and `PublishFirmware` stop answering `Accepted` to
  everything; the 1.6 handlers are taken out of their comments.

### Step 4 - ChargingStation: peers that are file servers

- The Connections page: a peer's card shows whether it is a file server, for what, with which
  logins and paths, and the `SFTP` public key to enrol there. A peer without OCPP can be added
  (today the station's peers are the hosts of its `ssh://` connections only).
- The same at the command line and in the HTTP API, under the right that configures connections.
- Signed writes over OCPP, with the signers of pin sets the station already has.
- A login's key counts as confirmed after its `SSHKeySigned` or a confirmation on the page
  (section 9.2); the station's single `SFTP` key and `id_ed25519_sftp` go.

### Step 5 - ChargingStation: uploads

- Handlers for `GetLog` (2.1, 1.6) and `GetDiagnostics` (1.6): `Judge`, then ask the provider of
  the file, answer with its name, and upload in the background.
- **The provider of the file is a seam**, not part of this plan: an interface the vendor's logic
  implements (`logType`, time window → a stream and a name, or "nothing to send"). The reference
  station gets the plainest provider that makes the path testable - the node's log file of the
  day, as it is.
- `LogStatusNotification` / `DiagnosticsStatusNotification` by section 5.4; a second request
  cancels the first (`AcceptedCanceled`); a request survives a lost OCPP connection, and its
  notification is queued offline.
- `TriggerMessage` `LogStatusNotification` answers with the state of the running upload, or `Idle`.

### Step 6 - ChargingStation: downloads

- Handlers for `UpdateFirmware` (2.1, 1.6) and `SignedUpdateFirmware` (1.6): `Judge` when the
  request arrives and again at `retrieveDateTime`.
- The download into a staging directory with its limit; resume on retry;
  `FirmwareStatusNotification` `Downloading`, `DownloadPaused`, `Downloaded`, `DownloadFailed`.
- **The consumer of the file is a seam** as well: the downloaded file and the request are handed
  to the vendor's logic, which verifies, installs and reports from there. The reference station's
  consumer keeps the file and reports nothing further.
- `TriggerMessage` `FirmwareStatusNotification`.

### Step 7 - The file server in our own nodes (WWCP_Node, LocalController, CSMS)

- A station's own login may be granted `sftp` (decision 4), confined to the same two areas.
- Two SFTP areas for station logins, and for logins with a key of the purpose `SFTP`: `/upload` (per station:
  create and rename the own `.part`, nothing else; a quota) and `/firmware` (read only).
  SFTP-Concept 2.10 put these off; this is where they come back.
- `InstallSSHKey` with the purpose `SFTP` at the Local Controller; the CSMS enrols the key on
  `SignSSHKey` `SFTP`.
- The node's host key fingerprints are shown where an operator copies them into a peer.

### Step 8 - Local Controller

- `PublishFirmware`: download by the code of step 2, publish in `/firmware`, name the `sftp://`
  URL in `PublishFirmwareStatusNotification` (section 7.3).
- Its own `GetLog` and `UpdateFirmware`, through the same handlers as the station where the code
  can be shared (WWCP_OCPP_Node).
- Question 3 decides whether it takes its stations' uploads.

### Step 9 - CSMS

- A file server per station or group in its store, from which it builds the URL of a `GetLog` and
  an `UpdateFirmware`; the signed write of the peer to the station.
- Not in this repository; listed so the plan is whole.

### Step 10 - End to end, paper, pins

- EVChargingTestEnvironment: a real station, a real file server (a CSMS node and OpenSSH), a file
  up by `GetLog` and a file down by `UpdateFirmware`, in OCPP 2.1 and 1.6.
- Negative cases, each mutated once: an unknown host, the right host on another port, a peer that
  is no file server, a login not listed, a path outside, a password in the URL, a contradicting
  `;fingerprint=`, a foreign host key (no retry, security event), a peer on probation, a key the
  server does not know, a server without `sftp`, a file that exists, a file that is too large, a
  name that resolves elsewhere.
- The paper's chapter 12 with the commits, after their pushes went through; version 0.2.
- The pins of this superproject, once every sha is on GitHub, git1 and git2.

---

## 5. Repositories touched

| Repository | Steps |
|---|---|
| Hermod | 1 |
| WWCP_Node | 2, 7 |
| WWCP_OCPP (incl. WWCP_OCPP_Node) | 3, 8 |
| ChargingStation | 4, 5, 6 |
| LocalController, CSMS | 7, 8, 9 |
| ChargingStationCLI (this repository) | pins, README, this file |
| Whitepapers | chapter 12, the decisions |

---

## 6. Not in this plan

- What a log is, how it is collected and packed; verifying and installing firmware. OCPP's logic
  and the vendor's, behind the two seams of steps 5 and 6.
- HTTPS and FTPS transfers; the station lists `SFTP` and nothing weaker until somebody asks.
- A Local Controller that forwards its stations' uploads (question 3).
