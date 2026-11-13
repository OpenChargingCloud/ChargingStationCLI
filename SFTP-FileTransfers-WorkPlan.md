# SFTP for Logfile Uploads and Firmware Downloads - Work Plan

**Status:** 2026-11-13. Steps 1 to 3 are built and on the master branches (Hermod, WWCP_Node, WWCP_OCPP), and so is the enrolment of a key over OCPP at the station; nothing is pinned here yet. Steps 4 to 10 are plan, apart from what they say is built.
**Concept:** the white paper [SFTP for Logfile Uploads and Firmware Downloads](https://github.com/OpenChargingTechnology/Whitepapers/blob/master/SFTP-FileTransfers/README.md)
(`Whitepapers/SFTP-FileTransfers/README.md`, version 0.3). This file says how the paper is built
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
| 2 | **One key per file server and login**, not one `SFTP` key. | A key store per peer and login in WWCP_Node; the lists `FileTransferPublicKeys` and `FileTransferKeyStates` of the peer; `SignSSHKey`/`SSHKeySigned` get `peer` and `login`; the station's single `id_ed25519_sftp` goes. |
| 3 | **Who writes the peers over OCPP is the station's signature policy** - for every SSH peer, and for what makes one a file server as for the rest. Enforced: a signer with the right to the connections; off: unsigned, as before. This replaced "file servers signed only" and then "every peer signed only, by a check in the component" of the same day. | No check of its own in `SSHPeerController`; the session of the SSH peers builds the policy part (WWCP_OCPP, ChargingStation) and takes out the check that `df25a599` and `af774257` put in. No signature of a `ChangeConfiguration` has to reach the keys' source, and a Local Controller needs no signers for its peers. |
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

Hermod `aa42d703` on the branch `sftp/stream-transfers`, on `e4c4e4b1`:
[Vanaheimr/Hermod#255](https://github.com/Vanaheimr/Hermod/pull/255), open. `SshClient` needed nothing: it offers the keys it is given and
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

**Built** - WWCP_Node `8f56b25` on the branch `sftp/file-transfers` (GitHub, git1, git2), on
`8da01db`, with Hermod's branch of step 1; not on master, which would not build before pull
request #255 is merged. New files only:

- `SecureShell/SftpLocation.cs`: the URL of chapter 3, read by hand - a password, a query, a
  fragment, `..` and control characters make it no location; hosts compared as text.
- `SecureShell/SSHFileServer.cs`: `SSHFileTransfers` (directions, logins, directories per
  direction with `{id}`, `dedicated`/`shared`; read from and written as JSON) and `SSHFileServer`
  (name, address, login, and the `NTSServerSSH` it is held to, whose `Believes` judges the host
  key).
- `WWCPNode.FileTransfers.cs`: `SetFileServers`, `JudgeFileTransfer` (sections 4.1 and 5.1, one
  reason code for everything the configuration lacks, `OnFileTransferRefused`), a key per file
  server and login (`FileTransferPublicKey`, `ConfirmFileTransferKey`; deleted with the login,
  and with a peer whose address or port changed), `UploadFileAsync` (`<name>.part`, `CREAT |
  EXCL`, the plain rename) and `DownloadFileAsync` (offset, limit), on a connection of its own or
  as a channel of `SharedSSHConnectionTo(Host, "sftp")`; `FileTransferResult.MayBeTriedAgain` is
  the table of section 5.4, and `OnFileServerHostKeyRefused` its security event.
- Tests: `FileTransferTests` (33), against a second node as the SFTP server; the whole
  WWCP_Node suite is green (1485).

**Agreed with the session that owns the SSH peers** ("DNS-over-SSH Whitepaper"): its configured
peers - `SSHPeerConfiguration` (id, address, user, `NTSServerSSH` trust), `"ssh": { "peers": [...] }`,
`TryUpdateSSHPeers`, `SSHPeerController` - are committed locally and to be pushed. No pins are
kept twice: a file server is such a peer with a `"fileTransfers"` object in its entry. One peer
per address there, so a request has to name the address and the port of the peer (section 4.3).

**Built after that push** - WWCP_Node `03948309`: `"fileTransfers"` in the peer's entry of the
configuration file (`SSHPeerConfiguration`), the file servers derived from the configured peers
in place of `SetFileServers`, the keys of logins and peers that are there no longer deleted when
the peers change - decided by what is on disk, with a marker for the host and port the keys were
made for - and `DefaultSSHPeerUser` as the login where neither a request nor the peer names one.

**Left of step 2:**

- Key rotation per login (a pending key beside the current one), section 9.3.
- A host certificate of a pinned CA, tested; a run against OpenSSH under WSL.
- What every node kind must do alike, into the TestKit.

**Found on the way:** the node's own SFTP server holds an upload under `<name>.filepart` and puts
it in place *over* a file of that name when it is renamed; `<name>.part` is a file like any
other to it, whose rename is refused where the name is taken. The upload area of step 7 has to
take `.part` and never replace.

### Step 3 - WWCP_OCPP: device model, reason codes, events

**Built** - WWCP_OCPP `10ed9951` on the branch `sftp/file-transfers` (GitHub, git1, git2), on
`bc018f17`, with the branches of steps 1 and 2; not on master. It took the OCPP half of steps 5
and 6 with it, as the station and the Local Controller share it:

- `WWCP_OCPP_Node/FileTransfers.cs`, for every OCPP version: the answer to a request, with
  `InvalidURL` and `FileServerNotPermitted` in a sentence that does not say what the node holds;
  after the answer the transfer, the retries of the request for what may be better in a minute
  and nothing else, a new request cancelling the one before it, a download that waits until it
  is due and is judged again then, a staging directory. `FileProvider` and `FileConsumer` are the
  two seams to the logic above.
- `DeviceModelBridge.FileTransfers.cs`: `GetLog` and `UpdateFirmware` of OCPP 2.1 in OCPP's
  words, the notifications up to `Downloaded`, and the security events `FileTransferRefused` and
  `InvalidFileServerHostKey` for 2.1 and 1.6.
- `IFileTransferNode` (WWCP_OCPPv2.1): a node that leaves both requests to a processor and sends
  what is told; `TestChargingStationNode` is one.
- Tests: `FileTransferBridgeTests` (8); WWCP_OCPP_Node_Tests 215 and the OCPP 2.1 tests 3297
  green.

**Built after that:**

- WWCP_OCPP `7ec2079f`: `GetLog`, `GetDiagnostics`, `UpdateFirmware` and `SignedUpdateFirmware`
  of OCPP 1.6 on the same file transfers, in the fewer words 1.6 has; `IFileTransferNode` of
  OCPP 1.6, which `TestChargePointNode` is.
- WWCP_OCPP `df25a599`: the seven variables of section 8.1 at `SSHPeerController` -
  `FileTransferPublicKeys` and `FileTransferKeyStates` as lists of the peer, no variable instance
  per login - their keys of OCPP 1.6, and `fileTransfers` carried through every change of the
  peers. Its check by pin set signers goes again (decision 3).
- WWCP_OCPP `9bfa9d93`: `peer` and `login` in `SignSSHKey` and `SSHKeySigned`, JSON and CBOR.

**Left of step 3:**

- The three station-wide variables of section 8.2; `FileTransferProtocols` with `SFTP`.
- `SignSSHKey` and `SSHKeySigned` as `DataTransfer` over OCPP 1.6 and 2.0.1.
- What follows below, as far as it is not named above.


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
- **Built** - ChargingStation `c51a4218`: the custom triggers `SignSSHKey:SFTP:<peer>:<login>`
  and `SignSSHKey:SFTP` show a key to the CSMS, and an `SSHKeySigned` that names the file server
  and the login confirms it (section 9.2).
- Who writes the peers over OCPP is the signature policy (decision 3): the session of the SSH
  peers maps `SSHPeerController` to the station's connections.
- Left: the station sends a `SignSSHKey` by itself when a login is added; a confirmation on the
  page; the station's single `SFTP` key and `id_ed25519_sftp` go.

### Step 5 - ChargingStation: uploads

- The handlers are WWCP_OCPP_Node's (step 3). The station hands its OCPP nodes to the bridge as
  it does already, and sets the provider.
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
  an `UpdateFirmware`; the write of the peer to the station, signed where its policy asks for it.
- On a `SignSSHKey` with a peer and a login: the key entered at that file server, and
  `SSHKeySigned` with both.
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
