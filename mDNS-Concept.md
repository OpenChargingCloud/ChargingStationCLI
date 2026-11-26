# Multicast DNS for Every Node: a Concept

As of 2026-11-25. Code read: Hermod `a95ddebc`, WWCP_Node `81a29fa`, WWCP_OCPP `2d035c51`, ChargingStation `6aa4ca2`.

**Goal**: a charging station finds a local controller in its own network by a name such as `lc1.local`, without a unicast name server that knows it. The work goes into WWCP_Node, so every node can do it, not only stations and local controllers: it can resolve `.local` names, and it can answer for its own name.

**Decisions by Achim (2026-11-25)**

1. Multicast DNS is **off by default**. It is built into WWCP_Node, so every node can switch it on.
2. **Which network interfaces** take part is set in the configuration, and can also be set on the web interface.
3. **A host found by Multicast DNS is not simply trusted.** The first key it shows is put to the admin in a trust-on-first-use dialog. Nothing goes over the connection until an admin has confirmed it. This builds on the existing state "written into the configuration, but not confirmed yet" (B.4).

Part A lists what exists. Part B describes what is to be built. Part C lists the questions; Achim accepted every proposal there on 2026-11-26.

**Status (2026-11-26):**
- B.1 without the responder: `dns.multicast` with `enabled`, `interfaces`, `ipv4` and `ipv6` (WWCP_Node `68af98f`, with the command `ping <host>`).
- B.2: every connection, the connection test and `queryDNS` resolve through `WWCPNode.NameResolver`, which asks Multicast DNS for `.local` where it is switched on; the last ten answers are kept for the DNS page (WWCP_Node `f4a0f448`, WWCP_OCPP `6cec77be`, ChargingStation `638fd465`).
- B.3: `dns.multicast.responder` with `enabled` and `hostName`; the node answers for its name with the addresses of its interfaces, says a conflict and keeps its name, and says goodbye when it stops (WWCP_Node `f6b1fce8`; tested on real UDP port 5353 under Windows and WSL).
- B.4: trust on first use "Ask" for ssh:// and wss://, by default for a host under `.local`; confirmed or turned down at `v1/configuration/connections/confirmKey` and `rejectKey` and with the command `connection` (WWCP_Node `2351c9ba`, WWCP_OCPP `ade7ada5`, ChargingStation `7010b867` with the tests end to end).
- B.5: the card "Multicast DNS" on the DNS page, the key that waits on the Connections page with Confirm and Turn down, a notice above every page, and `mdns status` / `mdns resolve` at the command line (WWCP_Node `17e80a89`; looked at in a browser against a stand-in).
- Still to be built: B.6 (DNS-SD) and B.7 (Device Model).

---

## Part A: What exists

### A.1 Hermod has Multicast DNS, and nobody uses it

Hermod has a complete implementation, with its own tests, under `Hermod/DNS/Multicast`:

| Piece | What it does |
|---|---|
| `UDPMulticastDNSTransport` | Sockets on `224.0.0.251` and `ff02::fb`, port 5353, on chosen interfaces (`UDPMulticastDNSTransportOptions.InterfaceNames`; when unset, every interface that is up and supports multicast). IPv4 and IPv6 can each be switched off. Loopback is excluded by default. |
| `MulticastDNSClient` | Queries for link-local names (RFC 6762 §5). |
| `HybridDNSClient` | Sends `.local` and the link-local reverse zones to the multicast client, and every other name to a unicast client (`IsMulticastName`, by default `MulticastDNS.IsLocalName`). |
| `MulticastDNSResponder` | Publishes records (`PublishAsync`, `UpdateAsync`, `WithdrawAsync`), probes for the name, and reports name conflicts (`OnMulticastDNSNameConflict`). |
| `MulticastDNSBrowser` | DNS-SD (RFC 6763): finds services by type. |

None of WWCP_Node, WWCP_OCPP_Node or ChargingStation uses any of these pieces.

### A.2 What a node does with `.local` today

Every connection resolves through the node's `DNSClient` (`WWCPNode.DNSClient`). Following RFC 6762 §22.1, that client does not send `.local` to unicast servers (`SendLinkLocalNamesToServers = false`). So `lc1.local` comes back NXDOMAIN. Falling back to the resolver of the operating system is not allowed (rule "Always pass DNS clients"). A station configured with `wss://lc1.local:…` or `ssh://lc1.local` therefore never connects.

**One trap:** when `HybridDNSClient` is given no unicast client, it creates a `DNSClient` that uses the resolvers of the operating system. A node must therefore always pass its own `DNSClient` as the unicast client.

### A.3 Trust on first use today

| | ssh:// (`SshHostKeyTrust`) | wss:// with key pins (`ServerKeyPinTrust`) |
|---|---|---|
| Setting | `sshTrustOnFirstUse`: `None` / `HostKey` | `trustOnFirstUse`: `None` / `Key` |
| Learned key | `sshLearnedHostKey`, `sshLearnedAt` | `serverKeyPinLearned` |
| States | `OnProbation` (learned, unconfirmed) | `KeyPinState`: `Configured` / `Learned` / `Confirmed` |
| Confirmed by | a signed pin set | a signed pin set |
| No confirmation within the probation time (600 s by default) | forgotten, and learned again | forgotten, and learned again |

While a key is on probation, the only thing the connection executes is such a signed pin set. **An admin has no way to confirm a learned key.** For a host found by Multicast DNS that is what is missing: in a local network there is often no CSMS that could send a signed pin set.

---

## Part B: What is to be built

### B.1 Configuration

A new subsection of the node's `dns` section. Like every section, it is written to the file first and only then put into effect:

```json
"dns": {
  "multicast": {
    "enabled":     false,
    "interfaces":  [ "eth0" ],
    "ipv4":        true,
    "ipv6":        true,
    "responder": {
      "enabled":   false,
      "hostName":  "lc1"
    }
  }
}
```

- `enabled` (default `false`): when it is on, the node resolves `.local` through Multicast DNS.
- `interfaces`: the names of the network interfaces. An empty list means every interface that is up and supports multicast, except loopback. This is passed to `UDPMulticastDNSTransportOptions.InterfaceNames`. A name the machine does not know is accepted and saved, but shown on the page as "not present" (Q4).
- `ipv4` / `ipv6`: which multicast groups to join.
- `responder.enabled` (default `false`): the node answers for `<hostName>.local` with the addresses of the chosen interfaces.
- `responder.hostName`: the label without `.local`. Default: the machine name, reduced to one valid DNS label.

Every change is logged as a diff line, like the rest of the DNS section, tagged `dns`, `config`, `mdns`.

### B.2 Resolving (WWCP_Node)

- When `multicast.enabled` is on, the node keeps a `HybridDNSClient(MulticastDNSClient, UnicastClient: node.DNSClient)`. Every connection, file transfer and time server is given that client instead of the bare `DNSClient`. When it is off, nothing changes.
- Switching it on or off at runtime takes effect at the next connection attempt. Connections that are already open stay open.
- The DNS page and the status API show whether Multicast DNS is on, which interfaces are in use, and the last `.local` answers with their addresses.
- A name under `.local` is never sent to the unicast servers. That remains the default of the `DNSClient`.

### B.3 Answering (WWCP_Node)

- With `responder.enabled`, the node runs a `MulticastDNSResponder` on the chosen interfaces and publishes A and AAAA records for `<hostName>.local`.
- Name conflicts (`OnMulticastDNSNameConflict`) are logged as a warning and shown on the page. The node does not rename itself (Q3).
- If the addresses of the interfaces change, it calls `UpdateAsync`. When the responder stops, it calls `WithdrawAsync`.
- DNS-SD, i.e. announcing `_ocpp._tcp` or `_ssh._tcp` with port and subprotocols, is a separate step (B.6).

### B.4 Trust on first use with confirmation by an admin

The existing state `Learned` / `OnProbation` is the state "written into the configuration, not confirmed yet". Two things are added:

1. **An admin as a second way to confirm.** Alongside the signed pin set, a user with the right to manage connections can confirm a learned key on the connections page. Confirming moves `Learned` to `Confirmed`. The configuration records who confirmed it and when, e.g. `serverKeyPinConfirmedBy: "admin:alice"`, `…ConfirmedAt` (Q6). For ssh://, the learned host key becomes a pin.
2. **A new mode, "Ask"**: `sshTrustOnFirstUse: "Ask"` and `trustOnFirstUse: "Ask"`. With it:
   - The first key shown is written into the configuration as learned. The connection is then **closed before anything goes over it**: no BootNotification, no OCPP, not even a signed pin set.
   - The connections page shows the connection as "waiting for confirmation". It shows the address the name resolved to, how it was resolved (`mDNS` or unicast), when it was seen, and the SHA-256 fingerprint of the key. For TLS it also shows the certificate's subject, issuer and validity.
   - An admin can **Confirm**: the key becomes a pin, the state becomes `Confirmed`, and the connection is dialled again right away. Or **Reject**: the key is forgotten, and the connection stays off until it is switched on again.
   - "Ask" has no probation time: the key waits until somebody decides.
   - If a different key shows up while one is waiting, the waiting key is replaced. Both fingerprints go into the security log, and the page says that it changed (Q7).
3. **The dialog**: on every page, a banner like the existing notices of the web interface: "lc1.local shows a key that nobody has confirmed yet". It links to the card of the connection on the connections page. The dialog there shows the fingerprint and asks for confirmation. It is only shown to users who may manage connections.

For hosts under `.local`, a connection defaults to `"Ask"` when it has neither pins nor TOFU settings of its own (Q2). A key that is already pinned is checked as usual. If it does not match, the existing `OnPinMismatch` applies, and nobody is asked.

The confirmation is part of WWCP_Node's connection store and web API, so every node gets it, not just the station.

### B.5 Web interface

- **DNS page**: a "Multicast DNS" card. It has switches for resolving and answering, and a checklist of interfaces with their addresses, taken from `NetworkInterface.GetAllNetworkInterfaces()`, showing only interfaces that are up and support multicast. It also has switches for IPv4 and IPv6, a field for the host name, and the current state (published or conflict).
- **Connections page**: the state "waiting for confirmation" with Confirm and Reject, and "Ask" as a third TOFU option for ssh:// and wss://.
- **Command line**: `mdns status`, `mdns resolve <name>`, `connection confirm-key <id>` and `connection reject-key <id>`. These go through the same API as the page.

### B.6 Later: finding services (DNS-SD)

A local controller announces `_ocpp._tcp.local` (`_ocpp._tls` / `_ocpp-ssh`? Q8) with its port and TXT records such as `subprotocols=ocpp2.1,ocpp1.6` and `id=<node id>`. On the connections page, a station shows "local controllers found in this network" via `MulticastDNSBrowser`, and offers a found one as a template for a new connection. That connection starts in "Ask" as well. Nothing is connected automatically.

### B.7 OCPP Device Model

OCPP's DNS controllers do not know Multicast DNS. Proposal: vendor variables on `DNSClientController` (`MulticastEnabled`, `MulticastInterfaces`, `MulticastResponderEnabled`, `MulticastHostName`), plus `ntske`-style keys for 1.6 (`dns.multicast.*`). Confirming a key stays local or is done by a signed pin set. A CSMS cannot click "Confirm" (Q9).

### B.8 Tests

- **Unit tests in WWCP_Node without a real network**: `InMemoryMulticastDNSNetwork` (exists in Hermod) as the transport. Node A answers for `lc1.local`, node B resolves it. Switched off: NXDOMAIN, and no packet sent to the unicast servers.
- **Configuration**: defaults, interfaces, a name the machine does not know, changes at runtime, the diff line.
- **TOFU "Ask"**: the first key is learned and nothing is sent. Confirm leads to dialling again and BootNotification. Reject leads to the connection being off. A different key while one is waiting. Rights: someone without the right to manage connections sees neither the banner nor the buttons.
- **End to end**: a station with `ssh://lc1.local`, a local controller with the responder on. The station waits for confirmation, an admin confirms, and OCPP flows. Run with `InMemoryMulticastDNSNetwork`, and once on the loopback interface with real UDP under WSL (Windows may hold port 5353 itself, Q10).
- **Frontend**: the cards on the DNS and connections pages, the banner.

### B.9 Order

1. Configuration and resolving (B.1, B.2), with tests. Commit.
2. Responder (B.3). Commit.
3. Confirmation by an admin and "Ask" (B.4), API and command line. Commit.
4. Web interface (B.5). Commit.
5. End to end station ↔ local controller. Commit.
6. Device Model (B.7) and DNS-SD (B.6), after their questions are decided.

---

## Part C: Questions

Decided on 2026-11-26: every proposal below was accepted as written.

| # | Question | Proposal (decided) |
|---|---|---|
| Q1 | Should resolving and answering be switchable separately? | Yes: a station often only resolves, a local controller only answers. |
| Q2 | Should `.local` hosts default to "Ask", or is "Ask" only used when it is set? | Default to "Ask" for `.local` hosts without pins. A `.local` name without a pin is never believed by a CA alone. |
| Q3 | What happens on a name conflict: rename to `lc1-2.local` (RFC 6762 §9) or stay silent and report it? | Report it, and do not rename: a station is configured with the name. |
| Q4 | Should an interface name the machine does not have be refused or saved? | Saved and shown as "not present", so a configuration can be copied to another machine. |
| Q5 | May the responder also announce the loopback interface? | No, except in tests (`IncludeLoopbackInterface`). |
| Q6 | What does "confirmed by" record: the account, or also the IP of the browser? | The account and the time; the IP goes into the security log. |
| Q7 | While a key is waiting, a different key shows up. Replace it, or keep the first and refuse the second? | Replace it, and log both. Whoever confirms sees the current key. |
| Q8 | Which DNS-SD service names? | `_ocpp._tcp` for ws:// and wss:// (TXT `tls=1`), `_ocpp-ssh._tcp` for ssh://. |
| Q9 | Should a CSMS be able to confirm a learned key by a variable, or only by a signed pin set? | Only by a signed pin set, as today. |
| Q10 | Windows' own mDNS responder holds port 5353. Share the port (`ReuseAddress`), or treat Windows as unsupported for the responder? | Share it; Hermod binds with address reuse already. Check it under Windows and WSL. |
