# Secure DNS: Comparison with the White Paper and a Concept for the OCPP Device Model

As of 2026-10-29. Compared:

- **Paper** (Whitepapers `fb9b7ba`, which Part A compares; revised since as A.6 says):
  [Secure Name Resolution for OCPP](https://github.com/OpenChargingTechnology/Whitepapers/blob/master/SecureDNS/README.md) (**SDNS** below)
- **Code**: WWCP_Node `1a8db65`, Hermod `6fb98998`, WWCP_OCPP `b5d41d54`, ChargingStation `1499963`
- **Sibling**: [NTS-DeviceModel-Concept.md](NTS-DeviceModel-Concept.md) (**NTS concept** below). Whatever both bridges need is described there once and only referred to here.

Part A compares, Part B describes the missing glue code.

SDNS was written from this implementation, so Part A is shorter than in the NTS concept: no model is the wrong way round. The differences are what the paper asks for beyond the code, and a few places where the paper says more than the code does.

---

## Part A: Paper and Implementation

### A.1 What matches

| Topic | Paper | Implementation |
|---|---|---|
| Transports | SDNS §2.1: `UDP`, `TCP`, `TLS`, `HTTPS` (POST), `HTTPS_GET`, `HTTPS_JSON` | Hermod `DNSTransport`. It also has `HTTPS_Binary` (the same code path as `HTTPS`, `DNSClient.cs:1329`) and the unencrypted `HTTP`, `HTTP_Binary`, `HTTP_JSON` |
| Default ports | 53 / 853 / 443 by transport | `DNSServerConfig.DefaultPortFor` (Hermod); the file's `port` is optional |
| Strict profile | §2.2: a refused certificate ends the handshake, no fallback to UDP | ✔ the validator answers `Failed`, and no other transport is tried |
| Order of the checks | §2.3: name, then chain, then pins; the name is never forgiven | `WWCPNode.JudgeServer` (`WWCPNode.ServerCertificates.cs:258–340`) |
| Roots for name servers | `NameServerRootCertificate` | TLS roots of the node's store with usage `dns` (`TryAnchor`), plus a pinned root that the store holds as any kind |
| Pins | SHA-256 over the DER certificate or root, ≤ 16 each, with or without colons | `ServerPins`, `MaxPins = 16` |
| `OnPinMismatch` | `Refuse` / `Record` / `Accept`; `Record` also into the metrological log | `PinMismatch`; `WriteDown` writes `recorded` as evidence |
| Trust on first use | only from a certificate believed without a mismatch; nothing when a pin of that kind is set | `JudgeServer` (`:342–370`), `LearnNameServerPin` writes into the file under `reconfigureLock` |
| Change detection | §2.3: another certificate than before is a warning | `KnownServers.Observe`, persisted across restarts |
| Once per verdict | §2.8 | `WriteDown`: the same outcome with the same fingerprints only at debug level; "believed again" as a notice |
| Pins changed while connected (T11) | the connection is closed | `ApplyDNSConfiguration`: `CloseConnection` and the judgement dropped |
| Parallel race | §2.4: all servers at once, the first valid answer wins | `DNSClient.Query`, `Task.WhenAny` (`DNSClient.cs:840`) |
| Client settings | §3.1.1 `Enabled`, `MaxRetries` (0…255, 1), `UseCache` (true), `DNSSECOK` (false), `FollowCNAMEs` (true), `MaxCNAMEFollows` (0…255, 8), `MaxServers` 16 | identical names, ranges and defaults (`DNSConfiguration`, Hermod `DNSClient`); switched off, the client holds no servers and the list is kept |
| Robustness and privacy | §2.7 | Hermod: positive and negative cache (`negativeCacheTTL`), CNAME and DNAME (RFC 6672, `DNSClient.cs:988ff.`), cookies (`DNSClientCookies`), no client subnet by default, padding (`Helpers/DNSPadding.cs`), keepalive (`DNSKeepalivePolicy`, RFC 7828) |
| Logging changes | §2.8 | One diff line "DNS configuration changed: …" tagged `dns`, `config` (`WWCPNode.cs:1885`); the web API adds who (`NodeHTTPAPI.NamesAndTime.cs:97`) |
| Persist first | §3.1.3: what cannot be saved is not put into effect | `TryUpdateDNSConfiguration`: `TryMergeSection` before `ApplyDNSConfiguration`, refusals of the file as `NotSaved` |
| No empty list | §3.1.3 | "Switch name resolution off instead of emptying the list." (`WWCPNode.cs:1720–1724`) |

### A.2 Differences in the *DNS Client Controller* (SDNS §3.1.1)

| Variable | Paper | Implementation | Assessment |
|---|---|---|---|
| *Component* | `DNSClientController` | WWCP_OCPP has a placeholder `DNSCtrlr` (`WWCP_OCPPv2.1_ChargingStation/Components/Logical/DNS/DNSCtrlr.cs`, commit `06d64b5f` "New NTS contoller (wip)"). It is a search-and-replace copy of `AuthCtrlr` (`DNSorizeRemoteStart`, `MasterPassGroupId`, …) and is registered nowhere. | replace it, B.3 |
| `QueryTimeout` | default "*(system)*" | `DNSClient.DefaultQueryTimeout` = **10 s**; 0.1…120 ✔ | name the 10 s in the paper |
| `RecursionDesired` | default `Default` | The client starts with `RecursionDesired = true` (`DNSClient.cs:326`). A station built with nothing therefore reads `Set`; `Default` only after `"recursionDesired": null` was saved. | adjust the paper (A.6) |
| `UDPPayloadSize`, `ClientSubnet` | ro | fixed at construction (`DNSConfigurationJSON().fixed`), next to `ednsOptions`, `cacheCleanUpEvery`, `negativeCacheTTL` | fits; the other three are not needed |
| `EncryptedOnly` | rw | **not implemented** | small, in the node (B.4) |
| `ServerSource` | rw, `System` / `Manual` | The node cannot go back to `System`. It starts with the client's servers (`configuredDNSServers = [.. dnsClient.DNSServers]`, `WWCPNode.cs:778`), refuses an empty list and reads `"servers": null` as "not said". | ro in the bridge; rw needs B.4 point 7 |
| Emptying every slot | refused only while `Enabled = true` | refused **always** | adjust the paper (A.6) |
| `Enabled = false` | "the CSMS shall switch name resolution off instead" | Switched off, the client holds no servers and answers nothing (`DNSClient.cs:603`). There is no fallback to the resolver of the operating system, and the CSMS connection resolves through this client (A.4 point 8). | the paper contradicts itself (A.6) |

### A.3 Differences in the *DNS Server Controller* (SDNS §3.1.2)

| Variable | Paper | Implementation |
|---|---|---|
| *Instance* | fixed slots `"1"` … `"16"` | a list `servers[]` without ids. A server **is** its transport, address and port (`NameOf`, `WWCPNode.NameServerCertificates.cs:84`); the timeout does not count. WWCP_OCPP's placeholder `DNSServerCtrlr` is another `AuthCtrlr` copy. |
| `Address` | IP address or domain name, ≤ 253 | ✔ `address` (or `domainName`, or a plain string). `ServerJSON` writes IPv6 in the short form of RFC 5952, `NameOf` (log, known servers) in the long one. |
| `Port` | default by transport | ✔ when reading the file. But `ServerJSON` always writes the resolved port, so after the first save through the node the file holds explicit ports (A.4 point 4). |
| `Transport` | six values | ✔ the six, plus four more (A.1). The node takes any member case-insensitively, and through `Enum.TryParse` also any **number**, even an undefined one (B.15). |
| `QueryTimeout` | 0.1…120 s | ✔ |
| `CertificatePins`, `RootPins` | string, "≤ 16"; 15 fit into 1000 characters | ≤ 16 each, up to 200 characters per entry, colons allowed. 16 fingerprints take 1039 characters: more than `attributeValue` takes in OCPP 2.0.1 (1000), less than in OCPP 2.1 (2500), and they need a `ReportingValueSize` ≥ 1039. |
| `OnPinMismatch` | only with a pin or trust on first use | ✔ `ServerPins.TryParse` refuses `onMismatch`, even `refuse`, on a server held to nothing and learning nothing (`ServerPins.cs:325–332`). When the last pin goes on the page, `onMismatch` goes with it (`TryAfterAPageSaved`). |
| `TrustOnFirstUse` | ✔ | ✔ The learned pin does **not** go through `ApplyDNSConfiguration`: no "DNS configuration changed" line, but "held to … from now on" from `WriteDown`. |
| `AuthenticationName` | RFC 8310 ADN | **not implemented**: the certificate is checked against the address that is dialled. Hermod's `DNSServerConfig` has no field for it. |
| `RootCAs` | a `certificateGroup` | **not implemented**: root pins instead, as decided for NTS on 2026-10-20 |
| `Enabled`, `Priority` | per server | **not implemented**: Hermod asks every server in one race |
| `Problem`, `CertificateStatus`, `CertificateFingerprint`, `RootFingerprint` | ro | ✔ from `LastJudgementOf`. Judgements live in memory only, so after a restart these are empty until the next handshake. |
| `KnownSince` | ro | ✔ `KnownServers`, persisted |
| Revocation | §2.3: own roots like the paper says; chains to roots of the operating system "are judged by the operating system" | Own roots: `RevocationMode.Online`, unknown status ignored (`WWCPNode.ServerCertificates.cs:491–494`). Chains to roots of the operating system: Hermod's TLS client defaults to `X509RevocationMode.NoCheck` (`Hermod/Hermod/TCP/TCPClient/ATLSClient.cs:490`), and the DNS client passes nothing else, so **revocation is not checked at all**. |

### A.4 Properties of the implementation that shape the mapping

1. **A race instead of an order.** Every server is asked at once, so slot numbers are identifiers, not priorities, and the security is that of the weakest server (SDNS §2.4).
2. **A list without ids.** Changing the transport, the address or the port makes **another server** of it: its judgement, its entry in the known servers, its connection and `pinsAsShown` no longer apply, and the diff line says the old one is "held to no fingerprint".
3. **Pins come only with the list.** `ApplyDNSConfiguration` takes pins only when `servers` comes along. Every change to any slot means sending the **whole** list with all pins.
4. **The port is resolved on writing.** `DNSConfiguration.ServerJSON` (`DNSConfiguration.cs:375–382`) always writes `port`. Whoever reads and writes back cannot tell "set" from "default" (B.9, the port rule).
5. **Trust on first use writes concurrently.** `LearnNameServerPin` writes into the file at the first handshake after a save, under `reconfigureLock`. A writer that read the list before must send `pinsAsShown`, or it takes the learned pin away again (measured so on the LC, see `ServerPins.TryAfterAPageSaved`).
6. **One path.** `TryUpdateDNSConfiguration(JSON, out Error, out NotSaved)`: parse, refuse an empty list, merge `pinsAsShown` under the lock, write the file, apply, write one diff line. OCPP gets no second path.
7. **A root without a usage vouches for everything.** `CertificateEntry.IsFor` (`Certificates/CertificateEntry.cs:152–155`) treats a TLS root that was never told its use as one for every use, name servers included. Roots that come through OCPP must therefore always get a usage (B.10).
8. **The CSMS connection resolves through this client.** The station hands the node's `DNSClient` to `ConnectOCPPWebSocketClient` (`ChargingStation/ChargingStation/ChargingStation.Connections.cs:320, 329`, relative to `libs/`). A CSMS that sets name servers which cannot answer, whose certificates are refused, or that switches name resolution off, cuts itself off. It does not notice at once: the open WebSocket stays up, and the station fails at the next reconnect, possibly days later. The NTS bridge has no such loop (B.6).

### A.5 The RFCs compared with the implementation

| RFC | Implementation |
|---|---|
| RFC 7858 §3.4, RFC 7828: connection reuse, keepalive | ✔ |
| RFC 7858 §4.2: out-of-band key-pinned privacy profile with **SPKI** pins (RFC 7469: base64 SHA-256 over the SubjectPublicKeyInfo) | Pins are SHA-256 over the **whole** certificate, in hex. A pin set that a DoT operator publishes cannot be used as it is, and a certificate renewed with the same key breaks a certificate pin. Root pins survive renewals of the server certificate. |
| RFC 8310 §5: strict usage profile | ✔ |
| RFC 8310 §8: authentication domain name | not implemented (A.3) |
| RFC 8484: DoH with POST and GET | ✔ (`HTTPS`, `HTTPS_GET`); `application/dns-json` is proprietary |
| RFC 7830, RFC 8467: padding | padded on TCP, TLS and HTTPS; which RFC 8467 policy was not checked |
| RFC 7873 cookies, RFC 7871 client subnet, RFC 2308 negative caching, RFC 6672 DNAME | ✔ |
| RFC 3225, RFC 4035: DO bit, validation | DO bit ✔, no inline validation (`DNSSECValidator` exists); RFC 8198 dormant |
| RFC 9462 (DDR), RFC 9463 (DNR) | not implemented; SDNS leaves it to the CPO's policy |
| RFC 9250 (DoQ) | not implemented; out of scope of SDNS |

### A.6 Where the paper must be revised

All points except 10 and 11, which concern the NTS concept and the names in WWCP_OCPP, were worked into the paper on 2026-10-29 (Whitepapers `bdfe66a`, pushed to origin, github and git2), together with point 15. Point 11 followed the same day (Whitepapers `0c0a01e`), once the names were settled together with NTS (B.13 point 2); point 10 is left. For point 8, SDNS §3.1.3 now requires the pre-flight of B.6 with `reasonCode` `CSMSUnreachable`, for *DeleteCertificate* as well (T19–T21). For point 12, the `valuesList` of `Transport` is now `UDP, TCP, TLS, HTTPS, HTTPS_GET`.

**Technical errors (first)**

1. *§2.3 Revocation*: Chains to roots of the operating system are not "judged by the operating system" but not checked at all (A.3, last row). Either the paper says so, or the node's DNS client asks the operating system as well (`Online`, unknown status ignored, as `TryAnchor` does). B.13 point 8.
2. *§2.5*: "authenticate them by a certificate issued for that address, by pins, or both" contradicts §2.3 point 1: a pin never replaces the name check. As long as there is no `AuthenticationName`, a server dialled by its IP address needs a certificate with that IP address as a *Subject Alternative Name*. Pins only narrow what is accepted then.
3. *§3.1.3 and T14*: "Emptying … SHALL be rejected … The CSMS shall switch name resolution off instead." Switching off has the same effect as emptying: nothing resolves, the CSMS included (A.2, last row). The paper must say that a station whose CSMS is dialled by name refuses both, and it needs the pre-flight from point 8.

**Model and inconsistencies**

4. *§1 against §2.3 point 2*: The paper calls the complete Web PKI bundle "overly broad trust", yet a root of the operating system vouches for any name server that is not pinned. The NTS concept (A.1, "Restricting root CAs") finds the same for NTS. The paper should recommend `RootPins` for every server, or define a switch that leaves the roots of the operating system out.
5. *§2.3*: Name the pin format against RFC 7858 §4.2 (A.5): whole certificate, not SPKI. Optionally accept SPKI pins as a second notation (e.g. `sha256/<base64>`), which needs a node change.
6. *§3.1.1*: The default of `RecursionDesired` is `Set` in the reference implementation; `QueryTimeout` defaults to 10 s there.
7. *§3.1.2*: The limits column of `CertificatePins`/`RootPins` says "≤ 16", the text says 15 fit. Both are right for one version only: 15 fit into the 1000 characters of OCPP 2.0.1, all 16 into the 2500 of OCPP 2.1. Reading 16 takes 1039 characters, so `ReportingValueSize` must be at least that.
8. **Missing: protection against a CSMS cutting itself off** (A.4 point 8). OCPP has this for `SetNetworkProfile` (`NetworkConfigurationPriority`, fallback slots); SDNS has nothing comparable. Proposal: a change through OCPP is applied only if the host names of the CSMS connections still resolve with the new configuration, and is otherwise `Rejected` (B.6).
9. *§3.1.3*: `additionalInfo` "at most 512 characters" is the OCPP 2.0.1 limit. The OCPP 2.1 Edition 1 schema says 1024 (`OCPPStateMachines/docs/schemas/v21/SetVariablesResponse.json`), as the NTS concept (B.10) has it; WWCP_OCPP's `StatusInfo` comment still says 512 (B.15).
10. *§3.1.2*: The data type of the pin lists is `string` here and `SequenceList` in the NTS concept (B.9). A `SequenceList` takes only values from the `valuesList`, the same as a `MemberList`, so `string` is right; the NTS concept should follow (B.13 point 5).
11. *Names*: The paper said `DNSClientCtrlr` and `DNSServerCtrlr`, WWCP_OCPP's placeholders are `DNSCtrlr` and `DNSServerCtrlr`. As for NTS (NTS concept A.6 point 22, B.14 point 2), settle names in paper and code together. Settled: `DNSClientController` and `DNSServerController` (B.13 point 2).
12. *§3.1.2 `Transport`*: `HTTPS_JSON` is "only for diagnostics" (§2.1) but listed in the `valuesList`; `HTTPS_Binary` exists in the reference implementation as a second name for `HTTPS`. Decide what OCPP offers (B.13 point 4).

**Editorial**

13. *§3.5*: "Both validate against a CA bundle, which should contain only the root CAs of the configured resolvers" holds for Unbound's `tls-cert-bundle`. systemd-resolved uses the bundle of the operating system, which cannot be narrowed per resolver.
14. *§2.7*: Say that the reference implementation pads (it does) and keeps connections alive (RFC 7828), as §2.1 already says for the latter.
15. *§3.1.3, the InstallCertificateRequest schema*: `certificate` has `maxLength` 11000, taken over from the Secure Time Synchronization paper, which has the same. The OCPP 2.1 Edition 1 schema says 10000.

---

## Part B: Glue Code between WWCP_Node and the OCPP Device Model

### B.1 Principle: one truth, two views

As in the NTS concept (B.1 there): the node's `dns` section (the file plus the runtime state of its `DNSClient`) remains **the only source**. The Device Model is a projection of it and keeps no state of its own.

- **Reading**: every variable gets a `ValueGetter` that reads live from the node (`DNSConfigurationJSON()`). A report takes one snapshot per request, because 16 slots with about 15 variables each would otherwise build the JSON some 250 times.
- **Writing**: the transaction hook (NTS concept B.3 point 3) hands the bridge all DNS entries of a request at once. The bridge builds the same JSON that the web page sends with `PUT /api/v1/configuration/dns`, and calls the same `WWCPNode.TryUpdateDNSConfiguration`.
- **Events**: changes from elsewhere (web page, CLI, trust on first use) reach OCPP through node events (B.11).

Validation, the pin rules, writing the file, the log and applying live happen **exactly once, and the same way everywhere**. The bridge checks only what is OCPP's: component, instance, variable, attribute type, data type and range. Every rule about DNS (pins only for TLS and HTTPS, `onMismatch` only with a pin, ≤ 16 servers, no empty list) stays in the node, and its sentence goes to the CSMS.

The bridge lives in the station program next to the NTS bridge and `ConfigureWebPayments`, and is written against `WWCPNode` alone, so that both bridges can move into one adapter assembly later (NTS concept B.1).

```
 Web page / CLI / API ───┐                           ┌── CSMS: Get/SetVariables, GetReport,
                         ▼                           ▼   InstallCertificate, …
         WWCPNode.TryUpdateDNSConfiguration ◄── DNSDeviceModelBridge (ChargingStation)
                         │                       ▲    ValueGetter, Tx hook, slot table,
     file "dns" + DNSClient (runtime)            │    pre-flight
                         │                       │            │
 LearnNameServerPin ─────┤                       │            ▼
 (trust on first use)    └── OnDNSConfigurationChanged   WWCP_OCPP Device Model
                             OnServerNews ───────┘      (DNSClientController,
                                                         DNSServerController 1…16)
```

### B.2 Current state in WWCP_OCPP and in the station program

Everything in NTS concept B.2 applies unchanged: only the first subscriber is dispatched, `BASE` and `ACID` produce no results, names are compared case-sensitively, no `NotifyReport` is ever sent, monitors are not stored, InstallCertificate keeps one certificate per type in memory (even on `Rejected`), and nothing sends security events. Specific to DNS (paths relative to `libs/`):

| Area | Today |
|---|---|
| Components | `DNSCtrlr` and `DNSServerCtrlr` in `WWCP_OCPP/WWCP_OCPPv2.1_ChargingStation/Components/Logical/DNS/`: `AuthCtrlr` copies with "DNSorization" variables, never registered. |
| Certificates | `InstallCertificateUse.NetworkTimeRootCertificate` (2.1, `InstallCertificateUse.cs:235`) and `CertificateUse.NetworkTimeRootCertificate` (1.6) exist; `NameServerRootCertificate` does not. |
| Security events | `SecurityEventType.ReconfigurationOfSecurityParameters` exists (`SecurityEventType.cs:284`); the three of SDNS §3.1.4 do not. Limits: `type` ≤ 50, `techInfo` ≤ 255 characters. |
| Limits | OCPP 2.1 Edition 1: `SetVariableData.attributeValue` ≤ 2500, `StatusInfo.additionalInfo` ≤ 1024, `reasonCode` ≤ 20, `InstallCertificate.certificate` ≤ 10000. WWCP_OCPP's schema comments still carry the 2.0.1 values (B.15). |
| Station | The OCPP 2.1 node is built with the node's `DNSClient` (`ChargingStation.Configuration.cs:1706`), and so is every CSMS connection (`ChargingStation.Connections.cs:320, 329`). The connection test resolves the CSMS host through it as well (`:725`). |
| WWCP_Node | no OCPP references, no event for DNS changes: only the log line tagged `dns`, `config` |

### B.3 What WWCP_OCPP lacks

Points 1–9 of NTS concept B.3, built **once** for both bridges. For DNS, in addition:

1. **Replace the placeholders** with `DNSClientController` and `DNSServerController`, built from variables with a source as in NTS concept B.3 point 1, with the characteristics of tables B.7 and B.8.
2. **`NameServerRootCertificate`** in `InstallCertificateUse` (2.1) and `CertificateUse` (1.6).
3. **Security events** `InvalidNameServerCertificate`, `NameServerPinMismatchTolerated` and `NameServerCertificateChanged` in `SecurityEventType` (2.1) and `SecurityEvent` (1.6).
4. **One source for two component names**: the transaction hook must hand the entries of `DNSClientController` and `DNSServerController` to the bridge together, because both end up in the one `dns` section.

### B.4 What the node lacks

1. **Change event**: `OnDNSConfigurationChanged(IReadOnlyList<String> Changes, String Origin)` at the end of `ApplyDNSConfiguration` when `changed.Count > 0`, **and** in `LearnNameServerPin`, which does not go through `Apply`. The same pattern as NTS concept B.4 point 1.
2. **Origin**: an optional parameter for `TryUpdateDNSConfiguration` (`web:<account>`, `cli:<account>`, `ocpp:<CSMS id>`), named in the diff line; `tofu` and `start` for the node's own changes. As NTS concept B.4 point 2.
3. **Certificate news**: `OnServerNews(ServerJudgement, ServerNews)` raised exactly where `WriteDown` says news, not in its debug branch: `Refused`, `Tolerated`, `Recorded`, `BelievedAgain`, `Learned`, `CertificateChanged`. `WriteDown` serves name servers and NTS-KE servers alike, so the NTS bridge gets certificate events for its servers from the same place.
4. **Read, change and write under one lock**: `TryUpdateDNSConfiguration(Func<JObject, JObject?> Change, String Origin, out Error, out NotSaved)`, with the snapshot and the write under one `reconfigureLock`. `pinsAsShown` protects the pins, but not the list: a server added on the page between the bridge's read and its write would be lost. `SemaphoreSlim` is not reentrant, so today's body becomes a private method that expects the lock to be held.
5. **A dry run of a configuration**: `Task<DNSPreflight> TryResolveWithAsync(JObject Section, IEnumerable<DomainName> Names, TimeSpan Within)`. It makes a separate `DNSClient` with the servers and pins of the section, judges certificates against those pins **without** writing news, learning pins or keeping judgements, and answers which names resolved, and what each server came to. `ResolveAsync` of the diagnostics only asks the configured servers. Needed for B.6.
6. **`EncryptedOnly`** (small): refuse unencrypted transports while set, and stop asking such servers (SDNS §2.4).
7. **`ServerSource` writable**: remember the servers the node was constructed with, and read `"servers": null` in a save as "remove the key", as for `recursionDesired`.
8. **Close connections when a root goes**: when a TLS root leaves the store or loses the usage `dns`, close the connections of the servers whose last judgement ended at it, as `ApplyDNSConfiguration` does for changed pins. Nothing subscribes to `CertificateStore.OnChanged` (`Certificates/CertificateStore.cs:219`) for this today.

A stable server id as in NTS concept B.4 point 3 is not needed here, because slots are the instances (B.5; B.13 point 3).

### B.5 Components and instances

| OCPP component | Instance | In the node | Life cycle |
|---|---|---|---|
| `DNSClientController` *(new)* | none | the `dns` section without `servers` | fixed |
| `DNSServerController` *(new)* | `"1"` … `"16"` | `servers[n-1]` | fixed; an empty `Address` means unused |

**Slots instead of ids** (unlike NTS): the node keeps no ids, there are no groups, the order means nothing in a race, and a standard CSMS cannot create instances. With fixed slots, adding a server is an ordinary *SetVariables* of its `Address`.

**Compacting**: the node holds a list without gaps, so servers behind an emptied slot move up. That happens **after** a request: within one request, slot numbers keep referring to the servers they referred to at its start (the slot table, B.9).

**Registration**: all 17 instances, empty slots included, after every build and rebuild in `BuildOCPPNodes`, next to `ConfigureWebPayments` (`ChargingStation.Configuration.cs:1715`). GetVariables on an empty slot answers `Accepted` with `""`, not `UnknownComponent`: the instance exists.

### B.6 No standard counterpart, and the CSMS connection

OCPP 2.x has no component for name resolution, and the `NetworkConnectionProfile` of *SetNetworkProfile* has no DNS settings (SDNS §3). Unlike `ClockCtrlr` for NTS, there is nothing standard to fill.

What OCPP does have for networks is a fallback: `SetNetworkProfile` writes a slot, and a station that cannot connect through the new profile falls back to the next one in `NetworkConfigurationPriority`. A DNS change can cut the CSMS off just as well (A.4 point 8), but without such a fallback, and only at the next reconnect. The bridge therefore runs a **pre-flight** before every change from OCPP:

1. The names to check: the hosts of all configured CSMS connections that are not IP addresses.
2. The node resolves them (`A` and `AAAA`) through the **candidate** configuration (B.4 point 5), bounded by the candidate's query timeout times its tries, and by at most 10 s per change.
3. A name that resolves with the current configuration but not with the candidate ⇒ every entry of the change `Rejected`, with `reasonCode` `CSMSUnreachable` (SDNS §3.1.3) and in `additionalInfo` the host plus what each server came to, e.g. "tls://192.0.2.53:853: untrusted". Nothing is applied.
4. `DNSClientController.Enabled = false` while a CSMS is dialled by name ⇒ `Rejected` without asking anything.
5. Names that did not resolve before do not block a change, so a CSMS can repair a broken configuration.

A timer that rolls back after the change does not help here: the open WebSocket stays up, so there is nothing to observe until the next reconnect. The pre-flight tests what matters before anything changes. Whether NTS-KE hosts (the legal time) are checked as well, and whether a refusal there blocks or only warns, is open (B.13 point 6). Changes on the web page are not checked; the page has its own test (`POST /api/v1/configuration/dns/query`).

### B.7 Variables: `DNSClientController`

| Variable | Type (characteristics) | Mutability | Node key | Note |
|---|---|---|---|---|
| `Enabled` | boolean | rw | `enabled` | `false` ⇒ pre-flight (B.6) |
| `QueryTimeout` | decimal s, 0.1…120 | rw | `queryTimeoutSeconds` | Actual is the client's (10 s when never set) |
| `MaxRetries` | integer 0…255 | rw | `maxRetries` | |
| `RecursionDesired` | OptionList `Set,Unset,Default` | rw | `recursionDesired` | `true` / `false` / `null`; `Default` removes the key (`RemovesRecursionDesired`) |
| `UseCache` | boolean | rw | `useCache` | |
| `DNSSECOK` | boolean | rw | `dnssecOK` | |
| `FollowCNAMEs` | boolean | rw | `followCNAMEs` | |
| `MaxCNAMEFollows` | integer 0…255 | rw | `maxCNAMEFollows` | |
| `MaxServers` | integer | ro | `limits.maxServers` | 16 |
| `UDPPayloadSize` | integer | ro | `fixed.udpPayloadSize` | |
| `ClientSubnet` | string | ro | `fixed.clientSubnet` | `null` ⇒ `""` |
| `ServerSource` | OptionList `System,Manual` | ro, rw after B.4 point 7 | the file has `servers` | read from the file, not from the client |
| `EncryptedOnly` | boolean | n/a until B.4 point 6 | `encryptedOnly` | not in the report until then ⇒ `UnknownVariable` |

### B.8 Variables: `DNSServerController[<slot>]`

| Variable | Type (characteristics) | Mutability | Node key | Note |
|---|---|---|---|---|
| `Address` | string[253] | rw | `servers[n-1].address` | `""` empties the slot; IPv6 in the short form |
| `Port` | integer 1…65535 | rw | `port` | the port rule in B.9 |
| `Transport` | OptionList `UDP,TCP,TLS,HTTPS,HTTPS_GET` | rw | `transport` | B.13 point 4 |
| `QueryTimeout` | decimal s, 0.1…120 | rw | `queryTimeoutSeconds` | `""` ⇒ the client's |
| `CertificatePins` | string[2500], ≤ 16 comma separated | rw | `certificateFingerprint` and `certificateFingerprints` | the two keys add up when reading |
| `RootPins` | string[2500], ≤ 16 comma separated | rw | `rootFingerprint` and `rootFingerprints` | |
| `OnPinMismatch` | OptionList `Refuse,Record,Accept` | rw | `onMismatch`, missing = `Refuse` | B.9 |
| `TrustOnFirstUse` | OptionList `None,Root,Certificate` | rw | `trustOnFirstUse`, missing = `None` | the learned pin changes `RootPins`/`CertificatePins` without a request |
| `Problem` | boolean | ro | `judgement.accepted` | `true` when a judgement exists and refused |
| `CertificateStatus` | OptionList `Accepted,Recorded,Tolerated,PinMismatch,Untrusted,WrongName,NoCertificate` | ro | `judgement.outcome` | `""` before the first handshake, after a restart, and for unencrypted transports |
| `CertificateFingerprint`, `RootFingerprint` | string | ro | `judgement.certificate`, `judgement.root` | |
| `KnownSince` | dateTime | ro | `known.since` | |
| `AuthenticationName`, `RootCAs`, `Enabled`, `Priority` | n/a | n/a | not implemented | not in the report ⇒ `UnknownVariable` until the node supports them |

An empty slot reports `""` for every variable.

### B.9 Writing: OCPP → node

**Pre-check per entry** (in all modes), as in NTS concept B.10:

| Case | Status | `reasonCode` |
|---|---|---|
| instance not `1`…`16` | `UnknownComponent` | none |
| unknown variable | `UnknownVariable` | none |
| `attributeType` ≠ Actual | `NotSupportedAttributeType` | none |
| ReadOnly | `Rejected` | `ReadOnly` |
| outside min/max, more than 16 pins | `Rejected` | `ValueOutOfRange` |
| type or format error | `Rejected` | `InvalidValue` |
| `OldAttributeValue` ≠ the getter's current value | `Rejected` | as in the NTS concept (`ValueChanged`, to be settled in the Safe & Secure paper) |
| the same variable twice in the request | `Rejected` | `InvalidValue` |

**The slot table.** At the start of a request, the bridge reads slots 1…16 from one snapshot, filled up with empty slots. The table stays addressed by slot for the **whole** request, even though the node holds a compacted list after every change. Otherwise, after slot 2 was emptied, a later entry for slot 3 would hit the server that moved up into it.

**Depending on `DataConsistencyModel`** (SDNS §3.1.3, the same semantics as NTS concept B.10):

- **`IndependentRequests`** (and without the field): every entry is one change, in request order, each with its own result. **Exception**: all entries of a slot that was empty at the start of the request form **one** change, applied where its `Address` entry stands, so that a new encrypted server never starts as a UDP server. Without an `Address` in the request, these entries are `Rejected` ("slot N is unused; set its Address in the same request").
- **`ACID`**: all DNS entries of the request are one change. The causing entries `Rejected`, all others `Rolledback`.
- **`BASE`**: like `ACID`, but each component instance (`DNSClientController`, each slot) is a change of its own.

**Building the section**, per change:

- Variables of `DNSClientController` become keys at the top level; `RecursionDesired = Default` becomes `"recursionDesired": null`.
- As soon as a `DNSServerController` entry is part of it: apply the change to the slot table, then `servers` from the used slots **in slot order**, i.e. the whole list (A.4 point 3).
- Per server: `address`, `transport`, `queryTimeoutSeconds`, `certificateFingerprints`, `rootFingerprints`. `trustOnFirstUse` only when ≠ `None`. `onMismatch` only when ≠ `Refuse` **and** the slot keeps a pin or learns one; otherwise it is dropped, as the node does for a page (`TryAfterAPageSaved`), and a monitor on `OnPinMismatch` sees the change.
- **The port rule**: when the change switches the transport and names no `Port`, `port` is left out if the old port was the default of the old transport. Otherwise "UDP on 53" would become a DoT server on port 53 after `Transport = TLS` (A.4 point 4).
- **`pinsAsShown`** on every server, with the pins as the bridge read them at the start of the request, or wrote them itself since. The node then merges a pin that trust on first use learned in the meantime instead of overwriting it (A.4 point 5).

**Pre-flight** (B.6), then **one** call of `TryUpdateDNSConfiguration` per change, with `Origin = ocpp:<CSMS id>`; under one lock once B.4 point 4 exists. With `IndependentRequests` these are several calls in a row, each with its own diff line, like several saves on the page.

**Results**, per change for all its entries:

- Success ⇒ `Accepted`. `RebootRequired` never occurs: the node applies everything live.
- `NotSaved` ⇒ `Rejected` with `InternalError`; nothing was put into effect (SDNS §3.1.3, T17).
- A refusal by the node ⇒ `Rejected` with `InvalidValue` (with `ACID`: the causing entries `Rejected`, all others `Rolledback`). `additionalInfo` is the node's sentence, with `'dns.servers[i]'` (the index in the compacted list) rewritten to `DNSServerController[<slot>]`, cut to the 1024 characters of the OCPP 2.1 schema (A.6 point 9). The node does not say which entry caused the refusal; the bridge takes the slot from the rewritten sentence, otherwise all entries of the change.

The admin role check in the SetVariables handler stays in front of all this. Until `Origin` exists, the bridge logs "CSMS '<id>' changed the name resolution of this charging station" with the tags `dns` and `ocpp`, like `PutDNSConfiguration` does for the page.

### B.10 Certificates: `NameServerRootCertificate`

The station answers InstallCertificate, GetInstalledCertificateIds and DeleteCertificate for `NameServerRootCertificate` through the store interface of NTS concept B.3 point 7, backed by the node's `CertificateStore`.

- **InstallCertificate**(`NameServerRootCertificate`, `certificateGroup = G`) ⇒ a TLS root in the node's store with usage `dns`, labelled `G`, or its common name without a group. Should `RootCAs` come after all, the group mark of NTS concept B.11 applies (`dnsca-<g>`).
- **Every other root type gets a usage as well** (`CSMSRootCertificate` ⇒ e.g. `ocpp`), never none: a root without a usage vouches for name servers too (A.4 point 7), and SDNS T4 forbids exactly that. This is a rule for the shared store interface, not just for DNS.
- **GetInstalledCertificateIds**(`NameServerRootCertificate`) lists the roots that vouch for name servers: usage `dns`, whether imported through OCPP or the web page, and roots without a usage. **DeleteCertificate** on a root without a usage ⇒ `Failed`, because it would be gone for every service; that is done locally.
- **DeleteCertificate** of a root that a configured server's chain ends at ⇒ the pre-flight of B.6 with the store as it would be afterwards. A refusal ⇒ `Failed`.
- Installing and deleting are logged with the tags `dns`, `certificates`, `security`. `TryAnchor` reads the store at every handshake, so a changed root counts from the next one. Nothing closes an open connection when a root goes, though, and connections stay open for long (B.4 point 8).

### B.11 Node → OCPP: changes from elsewhere

1. **Values**: nothing to do, because the getters read live.
2. **Structure**: fixed slots, so no components come or go. A server removed on the page moves the servers behind it up, and monitors on every moved slot fire.
3. **Monitoring**: `OnDNSConfigurationChanged` ⇒ the bridge compares the projected values with the last reported state and calls `VariableChanged` for each changed variable, which yields `NotifyEvent` with `Delta` (NTS concept B.3 point 6). `OnServerNews` does the same for `Problem`, `CertificateStatus`, the fingerprints and `KnownSince`.
4. **`OnServerNews` ⇒ `SecurityEventNotification`**, with `techInfo` = the server's name (`tls://192.0.2.53:853`) and the fingerprints involved, ≤ 255 characters:

   | News | Security event |
   |---|---|
   | `Refused` | `InvalidNameServerCertificate` |
   | `Tolerated`, `Recorded` | `NameServerPinMismatchTolerated` |
   | `CertificateChanged` | `NameServerCertificateChanged` |
   | `Learned` | `ReconfigurationOfSecurityParameters` |
   | `BelievedAgain` | none; in the log only |

5. **Local changes**: `OnDNSConfigurationChanged` with an `Origin` other than `ocpp:…` (web, cli) ⇒ `ReconfigurationOfSecurityParameters` with the node's diff line as `techInfo`, cut to 255 characters and complete in the log (SDNS §3.1.4, T16).
6. **No echo**: the bridge's own changes carry `Origin = ocpp:…`, so point 5 does not apply to them. Monitors fire nevertheless, because the node normalizes (IPv6 short form, ports written out, a dropped `onMismatch`).
7. **Offline**: security events must be queued while the CSMS is unreachable, and with name resolution broken it is unreachable precisely when they matter. Whether WWCP_OCPP queues them is to be checked along with NTS concept B.3.
8. **Manual edits** of the file take effect only at the next start, as before.

### B.12 OCPP 1.6

The same pattern applies to the keys of SDNS §3.2, once WWCP_OCPP can register keys with a getter and setter (NTS concept B.3 point 9). `GetConfiguration` computes the CSL values from the server list. `ChangeConfiguration` becomes one change of the section through the same method. The node always checks the **whole** resulting section, which is exactly what SDNS §3.2 asks for ("against the configuration as it would be after the change"). `dns.certificatePins.<n>` and `dns.rootPins.<n>` are resolved by the position in `dns.servers`.

Limits: one key per request, so there is no exception for new servers. With the CSL rule "the last value repeats", a server added at the end of `dns.servers` takes the last transport of `dns.transports`, so an all-TLS list stays TLS; a mixed list passes through the last transport. Pins for the new server follow in a request of their own, and until then it is held to its chain only. The pre-flight (B.6) applies in 1.6 as well; `Rejected` carries no reason there. InstallCertificate with `NameServerRootCertificate` and `certificateGroup` goes through the same store.

### B.13 Decisions

Points 2 and 7 were settled on 2026-10-29, together with the NTS decisions (NTS concept B.14). The others are open; the numbers stay, because the rest of this document refers to them.

1. **Location of the bridge**: in ChargingStation first, then together with the NTS bridge into an adapter assembly (NTS concept B.1). Decide for both together.
2. **Names** (settled): `DNSClientController` and `DNSServerController`, so that both extensions are named alike, as NTS concept B.14 point 2 asks: `NTPController`, `NTPClientGroupController` and `NTPClientController` there. The paper uses these names since 2026-10-29. WWCP_OCPP's placeholders `DNSCtrlr` and `DNSServerCtrlr` are replaced (B.3 point 1). The alternative was the OCPP-customary `…Ctrlr` of the standard components, which this concept had recommended.
3. **Slots or ids**: slots `1…16` are chosen because a standard CSMS can then add a server. NTS has since chosen the `id` field (NTS concept B.4 point 3, B.14 point 4) and ruled out positions for its servers, because they shift when a server is removed. The node gets the mechanism anyway, so `dns.servers[]` could carry slot numbers as ids; compacting would go away, and slots would stay stable across requests.
4. **Transports over OCPP**: `UDP, TCP, TLS, HTTPS, HTTPS_GET`, as recommended here and now written into the paper (`bdfe66a`); to be confirmed. `HTTPS_JSON` only for diagnostics, `HTTPS_Binary` is the same as `HTTPS`, `HTTP*` only for tests. A server configured locally with another transport still reads back as it is.
5. **Data type of pin lists**: `string` (SDNS) or `SequenceList` (NTS concept B.9). Recommendation: `string` with a max limit of 2500 for both (A.6 point 10).
6. **Scope of the pre-flight** (B.6): the CSMS hosts only, or the NTS-KE hosts of the legal group as well, and whether those block or only warn.
7. **May the CSMS change the name servers at all?** (settled) Yes, always, with no switch to lock them, as NTS concept B.14 point 1 decides for the legal group. What protects the station is the pre-flight (B.6) and the record: every change goes to the security log with its `Origin` (B.4 point 2), and changes from the web page or the CLI reach the CSMS as `ReconfigurationOfSecurityParameters` (B.11 point 5). SDNS §3.1.3 says so since 2026-10-29. The alternative was a switch `dns.ocpp: "readOnly"` for operators who set the resolvers locally.
8. **Revocation for chains to the roots of the operating system** (A.6 point 1): check them (online, unknown status ignored) or document that they are not checked.
9. **Mixed transports**: only warn (SDNS §2.4), or build `EncryptedOnly` right away (B.4 point 6).

### B.14 Implementation steps

The shared steps are those of NTS concept B.15 and are built **once**. DNS is then the smaller of the two bridges and suits being first.

1. **WWCP_OCPP**: reports and NotifyReport, removing components, the lookup bug, the extended `ValueSetterResponse`, the transaction hook (NTS concept B.15 steps 1–2).
2. **Node**: B.4 points 1–4 (event, `Origin`, certificate news, the locked update), together with NTS concept B.15 step 3, with kit tests.
3. **WWCP_OCPP**: replace the placeholders with `DNSClientController`/`DNSServerController`, plus `NameServerRootCertificate` and the security event types (B.3).
4. **Bridge, reading**: sources against the node, registration in `BuildOCPPNodes`, GetVariables, the report.
5. **Node and bridge, writing**: the dry run (B.4 point 5), then B.9: `IndependentRequests` with the slot table and the exception for empty slots, then `ACID`/`BASE`, with the pre-flight.
6. **Certificates** (B.10), through the shared store interface, with B.4 point 8.
7. **Monitoring and security events** (B.11).
8. **OCPP 1.6** (B.12).
9. **Equivalence tests**: the same change through `PUT /api/v1/configuration/dns` and through *SetVariables* yields the same file, the same state, and the same log except for `Origin`. Also SDNS T1–T18; emptying a slot and changing one behind it in the same request; the port rule; `pinsAsShown` against trust on first use between reading and writing; a compare-and-set conflict between the two paths; the pre-flight refusing name servers that cannot resolve the CSMS and `Enabled = false`; `NotSaved` (a read-only file) ⇒ `InternalError` with nothing in effect; after `BuildOCPPNodes` (an EVSE change) the components are still there.
10. **Paper**: revised per A.6 in `bdfe66a`, and with the decisions of B.13 points 2 and 7 on 2026-10-29; point 10 remains, with the NTS concept.

### B.15 Side findings

- `DNSConfiguration.TryParseServer` reads the transport with `Enum.TryParse(…, ignoreCase: true, …)` (`DNSConfiguration.cs:253`), which also takes numbers. `"transport": "3"` is `HTTP`. `"99"` is an undefined transport: port 53, asked over UDP (the default arms in `DNSServerConfig.DefaultPortFor` and `DNSClient.GetOrCreateTransportClient`), and named `99://…` in the log. The same pitfall as for other enums read this way; `Enum.IsDefined` plus a refusal of digits would close it.
- WWCP_OCPP's OCPP 2.1 classes carry the schema limits of OCPP 2.0.1 in their comments: `attributeValue` 1000 (`SetVariableData.cs:143`), `additionalInfo` 512 (`StatusInfo.cs:51`). The OCPP 2.1 Edition 1 schema says 2500 and 1024.
- The doc comment of `PostDNSQuery` (`NodeHTTPAPI.NamesAndTime.cs`) says a query without `server` goes "to the servers in turn, until one answers"; the client asks all of them at once (`DNSClient.cs:840`).
- WWCP_OCPP's commit `06d64b5f` also added `NTSCtrlr` and `NTSServerCtrlr` in `WWCP_OCPPv2.1_ChargingStation/Components/Logical/NTS/`. Only `NTSCtrlr` is an `AuthCtrlr` copy like the two DNS placeholders; `NTSServerCtrlr` is a sketch with about 40 NTP and NTS-KE properties. NTS concept B.2 lists only `NTPClientController` and `NTPClientGroupController` in `WWCP_OCPPv2.1/Components/Logical/`. WWCP_OCPP `1bf9af1f` removed `NTSCtrlr` and kept `NTSServerCtrlr` (NTS concept B.3 point 10).
