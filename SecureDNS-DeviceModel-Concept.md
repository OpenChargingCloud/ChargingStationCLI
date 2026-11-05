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
| Roots for name servers | `NameServerRootCertificate` | TLS roots of the node's store with usage `dns` (`TryAnchor`), plus a pinned root that the store holds as any kind; a server with a `rootGroup` only at the roots marked `dnsca-<group>` |
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
| `EncryptedOnly` | rw | **not implemented** | small, in the node (B.4); done on 2026-11-05 (B.4 point 6) |
| `ServerSource` | rw, `System` / `Manual` | The node starts with the client's servers (`configuredDNSServers = [.. dnsClient.DNSServers]`, `WWCPNode.cs:778`) and refuses an empty list. It could not go back to `System` and read `"servers": null` as "not said" until B.4 point 7, done on 2026-11-04. | rw since B.4 point 7 |
| Emptying every slot | refused only while `Enabled = true` | refused **always** | adjust the paper (A.6) |
| `Enabled = false` | "the CSMS shall switch name resolution off instead" | Switched off, the client holds no servers and answers nothing (`DNSClient.cs:603`). There is no fallback to the resolver of the operating system, and the CSMS connection resolves through this client (A.4 point 8). | the paper contradicts itself (A.6) |

### A.3 Differences in the *DNS Server Controller* (SDNS §3.1.2)

| Variable | Paper | Implementation |
|---|---|---|
| *Instance* | fixed slots `"1"` … `"16"` (ids since B.13 point 3) | a list `servers[]` without ids (an optional `id` since B.4 point 9). A server **is** its transport, address and port (`NameOf`, `WWCPNode.NameServerCertificates.cs:84`); the timeout does not count. WWCP_OCPP's placeholder `DNSServerCtrlr` is another `AuthCtrlr` copy. |
| `Address` | IP address or domain name, ≤ 253 | ✔ `address` (or `domainName`, or a plain string). `ServerJSON` writes IPv6 in the short form of RFC 5952, `NameOf` (log, known servers) in the long one. |
| `Port` | default by transport | ✔ when reading the file. But `ServerJSON` always writes the resolved port, so after the first save through the node the file holds explicit ports (A.4 point 4). |
| `Transport` | six values | ✔ the six, plus four more (A.1). The node takes any member case-insensitively, and through `Enum.TryParse` also any **number**, even an undefined one (B.15). |
| `QueryTimeout` | 0.1…120 s | ✔ |
| `CertificatePins`, `RootPins` | string, "≤ 16"; 15 fit into 1000 characters | ≤ 16 each, up to 200 characters per entry, colons allowed. 16 fingerprints take 1039 characters: more than `attributeValue` takes in OCPP 2.0.1 (1000), less than in OCPP 2.1 (2500), and they need a `ReportingValueSize` ≥ 1039. |
| `OnPinMismatch` | only with a pin or trust on first use | ✔ `ServerPins.TryParse` refuses `onMismatch`, even `refuse`, on a server held to nothing and learning nothing (`ServerPins.cs:325–332`). When the last pin goes on the page, `onMismatch` goes with it (`TryAfterAPageSaved`). |
| `TrustOnFirstUse` | ✔ | ✔ The learned pin does **not** go through `ApplyDNSConfiguration`: no "DNS configuration changed" line, but "held to … from now on" from `WriteDown`. |
| `AuthenticationName` | RFC 8310 ADN | **not implemented**: the certificate is checked against the address that is dialled. Hermod's `DNSServerConfig` has no field for it. |
| `RootCAs` | a `certificateGroup` | ✔ since 2026-11-03: the server's `rootGroup` (WWCP_Node `4e86012`, from NTS concept B.15 step 6), up to 24 letters, digits, `-` or `_`. Root pins narrow it further. |
| `Enabled`, `Priority` | per server | **not implemented**: Hermod asks every server in one race |
| `Problem`, `CertificateStatus`, `CertificateFingerprint`, `RootFingerprint` | ro | ✔ from `LastJudgementOf`. Judgements live in memory only, so after a restart these are empty until the next handshake. |
| `KnownSince` | ro | ✔ `KnownServers`, persisted |
| Revocation | §2.3: own roots like the paper says; chains to roots of the operating system "are judged by the operating system" | Own roots: `RevocationMode.Online`, unknown status ignored (`WWCPNode.ServerCertificates.cs:491–494`). Chains to roots of the operating system: Hermod's TLS client defaults to `X509RevocationMode.NoCheck` (`Hermod/Hermod/TCP/TCPClient/ATLSClient.cs:490`), and the DNS client passes nothing else, so **revocation is not checked at all**. Since 2026-11-05, the node asks itself (B.13 point 8). |

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

All points except 10 and 11, which concern the NTS concept and the names in WWCP_OCPP, were worked into the paper on 2026-10-29 (Whitepapers `bdfe66a`, pushed to origin, github and git2), together with point 15. Point 11 followed the same day (Whitepapers `0c0a01e`), once the names were settled together with NTS (B.13 point 2). Point 10 is settled since 2026-10-30, when the NTS paper and concept made their lists `string` as well (NTS concept A.6, Whitepapers `4e5e43d`; B.13 point 5). For point 8, SDNS §3.1.3 now requires the pre-flight of B.6 with `reasonCode` `CSMSUnreachable`, for *DeleteCertificate* as well (T19–T21). For point 12, the `valuesList` of `Transport` is now `UDP, TCP, TLS, HTTPS, HTTPS_GET`.

**Technical errors (first)**

1. *§2.3 Revocation*: Chains to roots of the operating system are not "judged by the operating system" but not checked at all (A.3, last row). Either the paper says so, or the node's DNS client asks the operating system as well (`Online`, unknown status ignored, as `TryAnchor` does). B.13 point 8: asked since 2026-11-05.
2. *§2.5*: "authenticate them by a certificate issued for that address, by pins, or both" contradicts §2.3 point 1: a pin never replaces the name check. As long as there is no `AuthenticationName`, a server dialled by its IP address needs a certificate with that IP address as a *Subject Alternative Name*. Pins only narrow what is accepted then.
3. *§3.1.3 and T14*: "Emptying … SHALL be rejected … The CSMS shall switch name resolution off instead." Switching off has the same effect as emptying: nothing resolves, the CSMS included (A.2, last row). The paper must say that a station whose CSMS is dialled by name refuses both, and it needs the pre-flight from point 8.

**Model and inconsistencies**

4. *§1 against §2.3 point 2*: The paper calls the complete Web PKI bundle "overly broad trust", yet a root of the operating system vouches for any name server that is not pinned. The NTS concept (A.1, "Restricting root CAs") finds the same for NTS. The paper should recommend `RootPins` for every server, or define a switch that leaves the roots of the operating system out.
5. *§2.3*: Name the pin format against RFC 7858 §4.2 (A.5): whole certificate, not SPKI. Optionally accept SPKI pins as a second notation (e.g. `sha256/<base64>`), which needs a node change.
6. *§3.1.1*: The default of `RecursionDesired` is `Set` in the reference implementation; `QueryTimeout` defaults to 10 s there.
7. *§3.1.2*: The limits column of `CertificatePins`/`RootPins` says "≤ 16", the text says 15 fit. Both are right for one version only: 15 fit into the 1000 characters of OCPP 2.0.1, all 16 into the 2500 of OCPP 2.1. Reading 16 takes 1039 characters, so `ReportingValueSize` must be at least that.
8. **Missing: protection against a CSMS cutting itself off** (A.4 point 8). OCPP has this for `SetNetworkProfile` (`NetworkConfigurationPriority`, fallback slots); SDNS has nothing comparable. Proposal: a change through OCPP is applied only if the host names of the CSMS connections still resolve with the new configuration, and is otherwise `Rejected` (B.6).
9. *§3.1.3*: `additionalInfo` "at most 512 characters" is the OCPP 2.0.1 limit. The OCPP 2.1 Edition 1 schema says 1024 (`OCPPStateMachines/docs/schemas/v21/SetVariablesResponse.json`), as the NTS concept (B.10) has it; WWCP_OCPP's `StatusInfo` comment said 512 until 2026-11-05 (B.15).
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

The bridge was built in the station program next to the NTS bridge and `ConfigureWebPayments`, written against `WWCPNode` alone, so that both bridges could move into one adapter assembly later. Since 2026-11-05 both live in `WWCP_OCPP_Node`, a project of its own in the WWCP_OCPP repository (B.13 point 1, NTS concept B.1).

```
 Web page / CLI / API ───┐                           ┌── CSMS: Get/SetVariables, GetReport,
                         ▼                           ▼   InstallCertificate, …
         WWCPNode.TryUpdateDNSConfiguration ◄── DeviceModelBridge (WWCP_OCPP_Node)
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
| Limits | OCPP 2.1 Edition 1: `SetVariableData.attributeValue` ≤ 2500, `StatusInfo.additionalInfo` ≤ 1024, `reasonCode` ≤ 20, `InstallCertificate.certificate` ≤ 10000. WWCP_OCPP's schema comments carried the 2.0.1 values until 2026-11-05 (B.15). |
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

   *Points 1 to 4 done on 2026-11-02*, on top of NTS concept B.15 step 3 (WWCP_Node `eb2066c`), with its `ChangeOrigin` and its way of telling listeners, now shared as `Tell` in `WWCPNode.Listeners.cs`:
   - `OnDNSConfigurationChanged(Changes, Origin)` is told once per save, after the lock is let go, to each listener on its own. A save that changes nothing tells nothing. `LearnNameServerPin` tells "`<server>` held to …" by `tofu`. `Origin` is `String?`, as for NTS: null where nobody said, and the start is told to nobody.
   - `TryUpdateDNSConfiguration(JSON, out Error, out NotSaved, Origin = null)` names the origin in the log line, "… (by web:root).", and `PutDNSConfiguration` passes `web:<account>`. Without an origin the line reads as before.
   - `OnServerNews(ServerJudgement, ServerNews)` is told where `WriteDown` says news, after it is written, once per news, and never in a dry run (`Remember: false`).
   - `TryUpdateDNSConfiguration(Func<JObject, JObject?> Change, out Error, out NotSaved, Origin = null)` asks the change for the section as `DNSConfigurationJSON` says it under the lock. A change that says null changes nothing. The JSON overload goes the same way.
5. **A dry run of a configuration**: `Task<DNSPreflight> TryResolveWithAsync(JObject Section, IEnumerable<DomainName> Names, TimeSpan Within)`. It makes a separate `DNSClient` with the servers and pins of the section, judges certificates against those pins **without** writing news, learning pins or keeping judgements, and answers which names resolved, and what each server came to. `ResolveAsync` of the diagnostics only asks the configured servers. Needed for B.6. *Done on 2026-10-30* as `WWCPNode.ResolveWithAsync(Section, Names, Within)`, which returns a `DNSPreflight` with an `Error` for a section that would not be saved, and `JudgeServer(…, Remember: false)`, with kit tests.
6. **`EncryptedOnly`** (small): refuse unencrypted transports while set, and stop asking such servers (SDNS §2.4).

   *Done on 2026-11-05*, with the warning of B.13 point 9:
   - **The node**:
     - `"encryptedOnly"` in the `dns` section, `WWCPNode.DNSEncryptedOnly`, and `settings.encryptedOnly` in `GET /api/v1/configuration/dns`.
     - While it is on, the client is given the servers over TLS or HTTPS alone. Those in clear text (UDP, TCP, plain HTTP) stay in the file, and are asked again once it is off.
     - A change after which it is on is refused (`TryKeepEncryptedOnly`) for a server in clear text the node does not have: a new one, or one whose address, port or transport changed, naming `dns.servers[<n>]`; its timeout does not count. So is a list without an encrypted server, as an empty list is, and `"servers": null` where the servers the node was started with are in clear text, as the operating system's are.
     - The dry run (B.4 point 5) refuses the same, and asks as the change would.
     - The change line says `encrypted only = True, not asking udp://…`. A list that mixes the two is warned about (`dns`, `config`, `security`) at the start and whenever the servers change.
   - **The bridge**: `DNSClientController.EncryptedOnly` is read and written as `encryptedOnly`, and the node's refusal names `DNSServerController[<id>]` as any other does. The 1.6 key `dns.encryptedOnly` is no longer NotSupported. WWCP_OCPP had the variable since step 3; only its description changed.
   - **The page**: the switch "Only encrypted name servers" among the settings; a row in clear text says "not asked" while it is on, and a list that mixes the two is warned about above it while it is off.
   - **Paper**: SDNS §2.4 (what the reference implementation warns of), §3.1.1, §3.1.3 (the rule), §3.2 (every key), T12.
   - **Tests**:
     - 10 `EncryptedOnlyTests` in the node, with 13 cases, one of which counts the questions at a server on the loopback.
     - 2 page tests.
     - In the station: 2 more `DNSDeviceModelWritingTests`, one more `DNSKeys16Tests`, T12 in `DNSTestCriteriaTests`, and `EncryptedOnly` in `DNSDeviceModelTests` and in the settings of `DNSEquivalenceTests`.
   - **20 mutations**, each red in exactly its tests:
     - in the node: the file's `encryptedOnly` not read; not written; servers in clear text asked all the same; a server in clear text the node does not have taken; one it has refused as well; no encrypted server at all taken; the servers it was started with not looked at; the dry run refusing nothing of it; the dry run asking servers in clear text; the change line not saying what is not asked; a list that mixes the two not warned about; `encryptedOnly` not shown; a change of it not put into effect;
     - in the bridge: `EncryptedOnly` not read; not writable; written under another key; no 1.6 key;
     - on the page: the switch not sent; a row in clear text not said to be not asked; a list that mixes the two not warned about.
7. **`ServerSource` writable**: remember the servers the node was constructed with, and read `"servers": null` in a save as "remove the key", as for `recursionDesired`.

   *Done on 2026-11-04.*
   - **The node:**
     - It keeps the servers its DNS client held when it was made (`startDNSServers`). For `ChargingStationCLI` those are the operating system's, as `new DNSClient()` looks for them.
     - `"servers": null` is `DNSConfiguration.RemovesServers`. A save of it takes `servers` out of the file and asks the start servers again, held to nothing: a section without a list has no pins.
     - The change line says "servers = …, the ones it was started with". The same servers coming from another place are a change as well, in both directions.
     - The dry run of B.4 point 5 asks the start servers for `"servers": null`.
     - `GET /api/v1/configuration/dns` says `"serversFrom": "file"` or `"start"`. The README says all of this.
   - **The bridge:**
     - `ServerSource` reads `Manual` for the file's servers and `System` for the start ones.
     - `System` sends `"servers": null`, after the pre-flight, and cannot be one change with a `DNSServerController` entry. After `System`, the server table is the node's again.
     - `Manual` writes the servers asked into the file.
     - OCPP 1.6 has no key for it.
   - **The page** offers it since 2026-11-05 (WWCP_Node `8f019f7`). Under the list there is a button "Back to the servers it was started with" while the servers are the file's, and a line saying where they are from either way. The button asks first, because the list goes with what each server is held to. Then it sends `"servers": null` alone and shows the servers the node now has. What is typed into the settings stays, as it does when name resolution is switched. A refusal, such as `EncryptedOnly` with start servers in clear text, is said under the list.
8. **Close connections when a root goes**: when a TLS root leaves the store or loses the usage `dns`, close the connections of the servers whose last judgement ended at it, as `ApplyDNSConfiguration` does for changed pins. Nothing subscribes to `CertificateStore.OnChanged` (`Certificates/CertificateStore.cs:219`) for this today.

   *Done on 2026-11-03* (`NameServerRootsChanged` in `WWCPNode.NameServerCertificates.cs`): at every change of the store, a server whose last accepted judgement was anchored at a root of the store that no longer anchors it loses its connection and its judgement, with a line tagged `dns`, `certificates`, `security`. That covers a root deleted, switched off, no longer for `dns`, or no longer marked for the server's group. `TryAnchor` and this check take the anchors from the same `AnchorsFor`. A server believed through a root the machine trusts keeps its connection. NTS-KE is not touched.

9. **Server ids** (B.13 point 3): an optional field `"id"` per entry of `dns.servers`, with the syntax of NTS concept B.4 point 3 and unique within the list, as the instance of `DNSServerController`. Entries without `id` are `dns-<n>`. *Done on 2026-11-05*, see B.13 point 3.

### B.5 Components and instances

| OCPP component | Instance | In the node | Life cycle |
|---|---|---|---|
| `DNSClientController` *(new)* | none | the `dns` section without `servers` | fixed |
| `DNSServerController` *(new)* | the server id (B.4 point 9) | the entry of `servers` with that `id` | added and removed through `DNSClientController.Servers` |

**Ids instead of slots** (since 2026-11-05, B.13 point 3), as for NTS: a server keeps its id whatever its address becomes and whatever is added or taken away around it, so nothing compacts and an instance means the same server across requests. A CSMS adds a server by naming a new id in `DNSClientController.Servers` together with the server's `Address`, as it adds a time server through `NTPClientGroupController.Clients`. Before, the instances were fixed slots `"1"` … `"16"`: a standard CSMS could add a server with an ordinary *SetVariables* of an `Address`, but slot numbers shifted when a server was emptied, and only the slot table of one request held them still.

**Registration**: `DNSClientController` and one `DNSServerController` per server, on the OCPP 2.1 node built last, together with the model as an `INewInstancesSource`. The station follows every `OnDNSConfigurationChanged` (`FollowDNSServers`, as `FollowNTSGroups`), so a server added or taken away on the page comes or goes as an instance at once. A server the station does not have is `UnknownComponent`, unless the request's `Servers` names it anew (B.9).

### B.6 No standard counterpart, and the CSMS connection

OCPP 2.x has no component for name resolution, and the `NetworkConnectionProfile` of *SetNetworkProfile* has no DNS settings (SDNS §3). Unlike `ClockCtrlr` for NTS, there is nothing standard to fill.

What OCPP does have for networks is a fallback: `SetNetworkProfile` writes a slot, and a station that cannot connect through the new profile falls back to the next one in `NetworkConfigurationPriority`. A DNS change can cut the CSMS off just as well (A.4 point 8), but without such a fallback, and only at the next reconnect. The bridge therefore runs a **pre-flight** before every change from OCPP:

1. The names to check: the hosts of all configured CSMS connections that are not IP addresses.
2. The node resolves them (`A` and `AAAA`) through the **candidate** configuration (B.4 point 5), bounded by the candidate's query timeout times its tries, and by at most 10 s per change.
3. A name that resolves with the current configuration but not with the candidate ⇒ every entry of the change `Rejected`, with `reasonCode` `CSMSUnreachable` (SDNS §3.1.3) and in `additionalInfo` the host plus what each server came to, e.g. "tls://192.0.2.53:853: untrusted". Nothing is applied.
4. `DNSClientController.Enabled = false` while a CSMS is dialled by name ⇒ `Rejected` without asking anything.
5. Names that did not resolve before do not block a change, so a CSMS can repair a broken configuration.

A timer that rolls back after the change does not help here: the open WebSocket stays up, so there is nothing to observe until the next reconnect. The pre-flight tests what matters before anything changes. The NTS-KE hosts (the legal time) are to be checked as well. A name there that would no longer resolve warns. The change is blocked only where none of the legal group's NTS-KE hosts would resolve any more while one resolves now (B.13 point 6, decided and built on 2026-11-05). Changes on the web page are not checked; the page has its own test (`POST /api/v1/configuration/dns/query`).

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
| `Servers` | string, comma separated ids, ≤ 16 | rw | the `id`s of `servers`, in order | A new id ⇒ a new server, whose `Address` must come in the same request. A missing id ⇒ the server is removed. The order is the order in the file, not a priority. Read from the instances, so it cannot disagree with them. |
| `MaxServers` | integer | ro | `limits.maxServers` | 16 |
| `UDPPayloadSize` | integer | ro | `fixed.udpPayloadSize` | |
| `ClientSubnet` | string | ro | `fixed.clientSubnet` | `null` ⇒ `""` |
| `ServerSource` | OptionList `System,Manual` | rw (B.4 point 7) | `serversFrom`: `file` ⇒ `Manual`, `start` ⇒ `System` | `System` writes `"servers": null`, `Manual` the servers asked |
| `EncryptedOnly` | boolean | rw (B.4 point 6) | `settings.encryptedOnly` | Servers in clear text stay and are not asked; one coming is refused by the node, named as `DNSServerController[<id>]` |

### B.8 Variables: `DNSServerController[<id>]`

| Variable | Type (characteristics) | Mutability | Node key | Note |
|---|---|---|---|---|
| `Address` | string[253] | rw | `servers[id].address` | `""` ⇒ `Rejected`: a server is removed through `Servers`; IPv6 in the short form |
| `Port` | integer 1…65535 | rw | `port` | the port rule in B.9 |
| `Transport` | OptionList `UDP,TCP,TLS,HTTPS,HTTPS_GET` | rw | `transport` | B.13 point 4 |
| `QueryTimeout` | decimal s, 0.1…120 | rw | `queryTimeoutSeconds` | `""` ⇒ the client's |
| `CertificatePins` | string[2500], ≤ 16 comma separated | rw | `certificateFingerprint` and `certificateFingerprints` | the two keys add up when reading |
| `RootPins` | string[2500], ≤ 16 comma separated | rw | `rootFingerprint` and `rootFingerprints` | |
| `OnPinMismatch` | OptionList `Refuse,Record,Accept` | rw | `onMismatch`, missing = `Refuse` | B.9 |
| `TrustOnFirstUse` | OptionList `None,Root,Certificate` | rw | `trustOnFirstUse`, missing = `None` | the learned pin changes `RootPins`/`CertificatePins` without a request |
| `RootCAs` | string[50] | rw | `rootGroup` | `""` ⇒ no group; a name the store cannot mark is refused before the node is asked; kept with the server when its address changes (B.9) |
| `Problem` | boolean | ro | `judgement.accepted` | `true` when a judgement exists and refused |
| `CertificateStatus` | OptionList `Accepted,Recorded,Tolerated,PinMismatch,Untrusted,WrongName,NoCertificate` | ro | `judgement.outcome` | `""` before the first handshake, after a restart, and for unencrypted transports |
| `CertificateFingerprint`, `RootFingerprint` | string | ro | `judgement.certificate`, `judgement.root` | |
| `KnownSince` | dateTime | ro | `known.since` | |
| `AuthenticationName`, `Enabled`, `Priority` | n/a | n/a | not implemented | not in the report ⇒ `UnknownVariable` until the node supports them |

A server the station does not have is not there: GetVariables answers `UnknownComponent`.

### B.9 Writing: OCPP → node

**Pre-check per entry** (in all modes), as in NTS concept B.10:

| Case | Status | `reasonCode` |
|---|---|---|
| an instance the station does not have, and that the request's `Servers` does not name anew | `UnknownComponent` (WWCP_OCPP, before the bridge) | none |
| `Address = ""` | `Rejected` | `InvalidValue`: removed through `Servers` |
| `Servers` empty, an id not of the id syntax, an id twice | `Rejected` | `InvalidValue` |
| `Servers` with more than `MaxServers` ids | `Rejected` | `ValueOutOfRange` |
| unknown variable | `UnknownVariable` | none |
| `attributeType` ≠ Actual | `NotSupportedAttributeType` | none |
| ReadOnly | `Rejected` | `ReadOnly` |
| outside min/max, more than 16 pins | `Rejected` | `ValueOutOfRange` |
| type or format error | `Rejected` | `InvalidValue` |
| `OldAttributeValue` ≠ the getter's current value | `Rejected` | `ValueChanged`, as in the NTS concept, until the Safe & Secure paper settles a code |
| the same variable twice in the request | `Rejected` | `InvalidValue` |

**The server table.** At the start of a request, the bridge reads the servers by their ids from one snapshot. After a change it holds what the node holds. Ids do not shift, so an entry means the same server whatever an entry before it took away.

**New servers.** An entry of `DNSServerController[<id>]` for an id the station does not have reaches the bridge only through `INewInstancesSource` (NTS concept B.10): the bridge makes a stand-in (`DNSServerController.NewInstance`) where the request's `DNSClientController.Servers` names the id, and only there.

**Depending on `DataConsistencyModel`** (SDNS §3.1.3, the same semantics as NTS concept B.10):

- **`IndependentRequests`** (and without the field): every entry is one change, in request order, each with its own result. **A new server cannot be one**, as for NTS concept B.10: each entry of a stand-in is `Rejected` ("'DNSServerController[x].Address' is not there yet: a new name server is made together with its Address, with the DataConsistencyModel ACID or BASE"), and a `Servers` entry that names a new id, alone, has no `Address` for it ("'DNSServerController[x]' is new: set its Address in the same request, with the DataConsistencyModel ACID or BASE"). Nothing changes, so a new encrypted server never starts as a UDP server either. Decided by Achim on 2026-11-05: until then, the entries of a new server went as one change with the `Servers` entry that added it, as the paper had promised for unused slots since T2a; with ids, adding a server needs the extension variable `Servers` anyway, so the reason was gone, and the two papers now say the same.
- **`ACID`**: all DNS entries of the request are one change. The causing entries `Rejected`, all others `Rolledback`. A server `Servers` removes cannot be set in the same change: those entries and `Servers` are `Rejected`.
- **`BASE`**: like `ACID`, but each server the station has is a change of its own, and `DNSClientController` one together with the servers its `Servers` adds (`BaseUnitOf`).

**Building the section**, per change:

- Variables of `DNSClientController` become keys at the top level; `RecursionDesired = Default` becomes `"recursionDesired": null`.
- `Servers` first: the servers in its order, the ones it names anew new, the ones it leaves out gone.
- As soon as a `DNSServerController` entry or `Servers` is part of it: apply the change to the server table, then `servers` from the table **in its order**, i.e. the whole list (A.4 point 3), each server with its `id`. So the ids the node derived by place are written into the file at the first change through OCPP, as NTS concept B.4 point 3 has it.
- Per server: `id`, `address`, `transport`, `queryTimeoutSeconds`, `certificateFingerprints`, `rootFingerprints`. `trustOnFirstUse` only when ≠ `None`. `onMismatch` only when ≠ `Refuse` **and** the server keeps a pin or learns one; otherwise it is dropped, as the node does for a page (`TryAfterAPageSaved`), and a monitor on `OnPinMismatch` sees the change.
- **The port rule**: when the change switches the transport and names no `Port`, `port` is left out if the old port was the default of the old transport. Otherwise "UDP on 53" would become a DoT server on port 53 after `Transport = TLS` (A.4 point 4).
- **`pinsAsShown`** on every server, with the pins as the bridge read them at the start of the request, or wrote them itself since. The node then merges a pin that trust on first use learned in the meantime instead of overwriting it (A.4 point 5).

**Pre-flight** (B.6), then **one** call of `TryUpdateDNSConfiguration` per change, with `Origin = ocpp:<sender>`, under one lock (B.4 point 4). With `IndependentRequests` these are several calls in a row, each with its own diff line, like several saves on the page.

- **The sender** is the start of the request's network path, which WWCP_OCPP hands every entry (`SetVariableEntry.Request`). A CSMS connected directly is `CSMS` there, not its own id, so the origin is `ocpp:CSMS`. An empty path counts as `CSMS` too.
- **A list changed elsewhere**: the change is made from the list the node holds under the lock. If that list is not the one in the server table, the change is refused with `ValueChanged` and nothing is sent. "Not the one" is judged as the node tells servers apart, ids included: the IPv6 short form and a port written out are the same server, another id is not. An example is a server added on the page while the request was being made. Sending the whole list back would write that server away, and the pre-flight was run against the table's list. The CSMS reads again and decides.

**Results**, per change for all its entries:

- Success ⇒ `Accepted`. `RebootRequired` never occurs: the node applies everything live.
- `NotSaved` ⇒ `Rejected` with `InternalError`; nothing was put into effect (SDNS §3.1.3, T17).
- A refusal by the node ⇒ `Rejected` with `InvalidValue` (with `ACID`: the causing entries `Rejected`, all others `Rolledback`). `additionalInfo` is the node's sentence, with `'dns.servers[i]'` (the index in the list sent) rewritten to `DNSServerController[<id>]`, cut to the 1024 characters of the OCPP 2.1 schema (A.6 point 9). The node does not say which entry caused the refusal; the bridge takes the server from the index, otherwise all entries of the change.

The admin role check in the SetVariables handler stays in front of all this. Before each change the bridge logs "A CSMS changes the name resolution of this charging station (by ocpp:CSMS): <change>" with the tags `dns` and `ocpp`, like `PutDNSConfiguration` does for the page. The node's diff line ends in "(by ocpp:CSMS)".

### B.10 Certificates: `NameServerRootCertificate`

The station answers InstallCertificate, GetInstalledCertificateIds and DeleteCertificate for `NameServerRootCertificate` through the store interface of NTS concept B.3 point 7, backed by the node's `CertificateStore`.

- **InstallCertificate**(`NameServerRootCertificate`, `certificateGroup = G`) ⇒ a TLS root in the node's store with usage `dns`, labelled `G`, or its common name without a group. Should `RootCAs` come after all, the group mark of NTS concept B.11 applies (`dnsca-<g>`).
- **Every other root type gets a usage as well** (`CSMSRootCertificate` ⇒ e.g. `ocpp`), never none: a root without a usage vouches for name servers too (A.4 point 7), and SDNS T4 forbids exactly that. This is a rule for the shared store interface, not just for DNS.
- **GetInstalledCertificateIds**(`NameServerRootCertificate`) lists the roots that vouch for name servers: usage `dns`, whether imported through OCPP or the web page, and roots without a usage. **DeleteCertificate** on a root without a usage ⇒ `Failed`, because it would be gone for every service; that is done locally.
- **DeleteCertificate** of a root that a configured server's chain ends at ⇒ the pre-flight of B.6 with the store as it would be afterwards. A refusal ⇒ `Failed`.
- Installing and deleting are logged with the tags `dns`, `certificates`, `security`. `TryAnchor` reads the store at every handshake, so a changed root counts from the next one. Nothing closes an open connection when a root goes, though, and connections stay open for long (B.4 point 8).

*Done on 2026-11-03* as `DNSRootCertificates` in the station (`ChargingStation/OCPP/DNSRootCertificates.cs`), an `ICertificateSource` registered on the OCPP 2.1 and the OCPP 1.6 node, after `NTSRootCertificates`:
- **InstallCertificate** marks a group as `dnsca-<g>` (a name the store cannot mark is `Rejected`). The root is labelled as the store labels it, by its common name, not by `G`. A root installed again keeps what it was for and gains `dns` and the mark.
- **GetInstalledCertificateIds** and **DeleteCertificate** as above. A root kept for another service loses `dns` and its `dnsca-` marks and stays for that service.
- **DeleteCertificate names a certificate, not a type**: `CertificateSources` deletes it from every source that holds it, after asking all of them. A root for name and time servers is therefore deleted for both, and the check of either one can keep it.
- **The check before a deletion** is the one B.6 makes for a change: when the CSMS is dialled by a name and some name server is asked over TLS, the names are resolved twice, with the store as it is and without the root (`ResolveWithAsync(…, WithoutRoots)`, which `JudgeServer` passes to `TryAnchor`). A name that resolves now and would not then keeps the root: `Failed` with `CSMSUnreachable` (SDNS T21). The NTS-KE servers of the legal time are resolved alongside since 2026-11-05 (B.13 point 6): none of them left keeps the root as `Failed` with `NTSKEUnreachable` (SDNS T23); some of them lost lets it go, `Accepted` with a status info of that reason code. For this, the shared `ICertificateSource.CheckDeletion` became `CheckDeletionAsync`, agreed with the NTS session.
- **`RootCAs`** of `DNSServerController` is the server's `rootGroup` (B.8).
- **The rule for other root types** needs nothing more: they stay in WWCP_OCPP's `InMemoryCertificates` and never reach the node's store, so they vouch for no name server.

### B.11 Node → OCPP: changes from elsewhere

1. **Values**: nothing to do, because the getters read live.
2. **Structure**: a server added or removed on the page is an instance that comes or goes, at the same `OnDNSConfigurationChanged` (B.5, `FollowDNSServers`). The servers behind a removed one keep their ids where the page sends them back, as it does since 2026-11-05. Until then, slots moved up and monitors on every moved slot fired.
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

*Done on 2026-11-03* (`ChargingStation.DNSEvents.cs`), through the NTS bridge's `TellCSMS` and queue:
- A change by `web:…`, `cli:…` or `tofu` is `ReconfigurationOfSecurityParameters`, with `techInfo` = `dns: <the node's change line> (by <origin>)`. A change by `ocpp:…` is not told.
- `Refused` is `InvalidNameServerCertificate`, `Tolerated` and `Recorded` are `NameServerPinMismatchTolerated` (with what the server is held to), and `CertificateChanged` is `NameServerCertificateChanged`. Each names the server and the fingerprints.
- `Learned` is not told again: `LearnNameServerPin` tells its pin as a change by `tofu`, so the CSMS hears of it once (point 5). `BelievedAgain` stays in the log.
- Every variable of both controllers supports monitoring (WWCP_OCPP), and every change and every news asks the monitors.
- The node kept a judgement only after telling its news, so a monitor on `Problem` read the verdict before it. It now keeps it first (WWCP_Node), which serves the NTS monitors on `CertificateStatus` alike.
- Point 7: the queue is the station's `OfflineQueue` on disk (NTS concept B.3), 1000 messages for 30 days.

### B.12 OCPP 1.6

The same pattern applies to the keys of SDNS §3.2, once WWCP_OCPP can register keys with a getter and setter (NTS concept B.3 point 9). `GetConfiguration` computes the CSL values from the server list. `ChangeConfiguration` becomes one change of the section through the same method. The node always checks the **whole** resulting section, which is exactly what SDNS §3.2 asks for ("against the configuration as it would be after the change"). `dns.certificatePins.<n>` and `dns.rootPins.<n>` are resolved by the position in `dns.servers`.

Limits: one key per request, so there is no exception for new servers. With the CSL rule "the last value repeats", a server added at the end of `dns.servers` takes the last transport of `dns.transports`, so an all-TLS list stays TLS; a mixed list passes through the last transport. Pins for the new server follow in a request of their own, and until then it is held to its chain only. The pre-flight (B.6) applies in 1.6 as well; `Rejected` carries no reason there. InstallCertificate with `NameServerRootCertificate` and `certificateGroup` goes through the same store.

*Done on 2026-11-03* (`DNSDeviceModel.Keys16.cs`), on the pattern of NTS (`NTSDeviceModel.Keys16.cs`):
- Every key of SDNS §3.2; `dns.encryptedOnly` since B.4 point 6 on 2026-11-05, NotSupported until then. The paper gained `dns.rootCAs` with it: without that key, a `certificateGroup` installed over 1.6 held no server to anything.
- `dns.queryTimeouts` reads an empty or missing entry as the client's timeout, as `dns.ports` reads one as the transport's port. That is the paper's second exception to the CSL rules, since its table already said "empty: `dns.queryTimeout`".
- A key becomes the SetVariables entries of `DNSClientController` and the servers, with ACID where it is several, and goes to the same model with the central system in its network path. So the checks, the pre-flight and the log line `(by ocpp:<id>)` are those of 2.1, and B.11 point 6 holds as well.
- `dns.servers` changes addresses at their places, and ids and pins stay with their server. It adds servers at the end with the last server's `Transport`, `OnPinMismatch`, `TrustOnFirstUse` and `RootCAs`, but with no pins and with the default port and timeout. It removes the servers beyond its end; removing all of them is refused. Since B.13 point 3, the *n*-th place is the *n*-th id of `Servers`, a new server takes the id the node would give it at its place (`DNSConfiguration.IdsOf`, the next free `dns-<n>`), and adding or removing goes through a `Servers` entry with the others.
- The station has one `DNSDeviceModel` for both OCPP nodes now, so one request at a time holds across the versions.

### B.13 Decisions

Points 2 and 7 were settled on 2026-10-29, together with the NTS decisions (NTS concept B.14), point 5 with the NTS paper on 2026-10-30, point 3 by Achim on 2026-11-04 (built on 2026-11-05), point 9 by Achim on 2026-11-05 (built the same day), points 4 and 6 by Achim on 2026-11-05, and point 1 by Achim on 2026-11-05 (built the same day). The others are open; the numbers stay, because the rest of this document refers to them.

Settled with point 3 on 2026-11-05: under `IndependentRequests`, a new server is refused and `ACID` or `BASE` is asked for, as NTS concept B.10 does for a new client (B.9).

1. **Location of the bridge** (settled by Achim on 2026-11-05, built the same day): a project of its own in the WWCP_OCPP repository, `WWCP_OCPP_Node`, for both bridges and both versions, with the wiring and with the tests as a kit that every kind of node runs against itself. How it is built is in NTS concept B.1.
2. **Names** (settled): `DNSClientController` and `DNSServerController`, so that both extensions are named alike, as NTS concept B.14 point 2 asks: `NTPController`, `NTPClientGroupController` and `NTPClientController` there. The paper uses these names since 2026-10-29. WWCP_OCPP's placeholders `DNSCtrlr` and `DNSServerCtrlr` are replaced (B.3 point 1). The alternative was the OCPP-customary `…Ctrlr` of the standard components, which this concept had recommended.
3. **Slots or ids** (settled on 2026-11-04: **ids**, as for NTS). *Done on 2026-11-05*:
   - **The node**: `dns.servers[].id` (B.4 point 9), read by `DNSConfiguration` with the syntax and `IdsOf` of the time servers, `dns-<n>` where the file gives none, and refused twice in one list. `GET /api/v1/configuration/dns` names every server with its id, `DNSServerIds` says them, the change line says `server ids = …` where they change, and a learned pin keeps the file's ids. "servers": null gives back servers without ids.
   - **The page** sends each server's id back, and shows it as the address field's tooltip.
   - **WWCP_OCPP**: `IDNSControllerSource.ServerIds` and `ServerValue(Server, Variable)` replace `Slots`. `DNSServerController` is made for an id, and refused for one the source does not have; `NewInstance` makes a stand-in, as `NTPClientController.NewInstance` does. `DNSClientController.Servers` is read from the ids, and `Address` may no longer be empty.
   - **The bridge** (B.5, B.8, B.9): `Servers` adds, removes and orders servers; a new server comes through `INewInstancesSource` and needs its `Address`. `IndependentRequests` refuses a new server and asks for `ACID` or `BASE`, as for NTS (decided by Achim the same day, in a commit of its own), and `BASE` sets `DNSClientController` as one with the servers it adds. The table is a list by id, the ids go into the file with every change, and a refusal names `DNSServerController[<id>]`. `FollowDNSServers` keeps the components in step with every change.
   - **OCPP 1.6** (B.12): places are the order of `Servers`, and a new server takes the next free id.
   - **Paper** (SDNS §3.1.1–§3.1.3, §3.2, §3.3, §3.4, T2a, T14, T14a, T18), Whitepapers.
   - **Tests**: 7 more in the node (`DNSConfigurationTests` 3 with 7 cases, `DNSChangeTests` 4) and the id of a learned pin in `NameServerPinningTests`; one page test; WWCP_OCPP's `DNSControllerTests` by ids, with one more for a stand-in; in the station 4 more `DNSDeviceModelWritingTests` and 3 rewritten for what slots did (an unused slot, a slot emptied, every address emptied), one more `DNSKeys16Tests`, and every other DNS test moved to ids.
   - **26 mutations**, each red in exactly its tests:
     - in the node: the ids not read; two servers of one id; a learned pin dropping the ids; new ids not told; the ids compared after the servers changed; no ids shown; no ids written;
     - in WWCP_OCPP: `Servers` not read; a controller for a server that is not there;
     - in the bridge: no stand-in; a stand-in for any id; a new server's entries apart from `Servers` (before the decision), and, after it, a new server set entry by entry and one not pointed to `ACID`; a new server without an address; a server taken away and set; the order not kept; an `Address` emptied; `Servers` naming none; the components not following; a refusal naming the place; 1.6 counting ids by place; no ids sent; the ids not compared; `BASE` setting a new server apart;
     - on the page: the id not sent back.

   Before: slots `1…16` are chosen because a standard CSMS can then add a server. NTS has since chosen the `id` field (NTS concept B.4 point 3, B.14 point 4) and ruled out positions for its servers, because they shift when a server is removed. The node gets the mechanism anyway, so `dns.servers[]` could carry slot numbers as ids; compacting would go away, and slots would stay stable across requests.
4. **Transports over OCPP** (settled on 2026-11-05): `UDP, TCP, TLS, HTTPS, HTTPS_GET`, as recommended here and written into the paper (`bdfe66a`). `DNSServerController.Transport` offers exactly these. `HTTPS_JSON` only for diagnostics, `HTTPS_Binary` is the same as `HTTPS`, `HTTP*` only for tests. A server configured locally with another transport still reads back as it is.
5. **Data type of pin lists** (settled): `string`, in both papers. NTS concept B.9 had `SequenceList`, which takes only values of the `valuesList`. Since 2026-10-30 the NTS paper and concept have comma separated strings for every list (NTS concept A.6, Whitepapers `4e5e43d`), as SDNS always had (A.6 point 10).
6. **Scope of the pre-flight** (B.6; decided by Achim on 2026-11-05): the NTS-KE hosts of the legal group are checked as well. A failure there only warns, "unless the errors are really serious", in which case it blocks as the CSMS hosts do. Serious, as Achim confirmed the same day: none of the legal group's NTS-KE hosts would resolve any more, while at least one resolves now, so that the legal time would be lost. One host of several that would no longer resolve only warns.

   *Done on 2026-11-05*, in the bridge's new place (point 1), and the paper's §3.1.3 says it, with T22 and T23:
   - `DeviceModelBridge.LegalTimeNamesOf` gives the DNS model and `DNSRootCertificates` the host names of the enabled legal groups' enabled sources. IP addresses are left out, since they need no name resolution.
   - The pre-flight resolves them together with the CSMS names. A name in both lists counts as a CSMS name, and a CSMS cut off is `CSMSUnreachable` whatever happens to the legal time.
   - If none would resolve any more while one does now, every entry is `Rejected` with `NTSKEUnreachable`. If only some would no longer resolve, every entry is `Accepted` with a status info of that reason code, naming the hosts lost and those kept, and the node logs the same.
   - Switching name resolution off counts as nothing resolving. Names that do not resolve now block nothing.
   - Deleting a `NameServerRootCertificate` follows the same rule: `Failed`, or `Accepted` with the reason. For this, `CertificateSources.DeleteAsync` now passes on the first answer that carries a reason code.
   - **Tests**: 9 more in the kit, so they run on the bare node and in the station:
     - 5 `DNSDeviceModelWritingTests`: all lost, some lost, a host not resolving now, switching off, the CSMS first;
     - 2 `DNSEquivalenceTests`: T22 by both versions, and a legal group that is switched off;
     - 2 `DNSRootCertificatesTests`: T23 against a name server over TLS, and some lost through a seam that resolves without the root.
   - **14 mutations**, each red in exactly its tests:
     - in the bridge: no legal names for the model, none for the roots, a switched-off legal group counting;
     - in the model: any host lost blocking, a host not resolving now blocking, the warning not told to the CSMS, the warning not logged, a name of both counted for the legal time too, the legal time before the CSMS, the legal names not asked;
     - for the roots: a root the legal time needs going, a root the legal time can lose going without a word, the legal names not asked, the sources dropping the reason of a deletion.
7. **May the CSMS change the name servers at all?** (settled) Yes, always, with no switch to lock them, as NTS concept B.14 point 1 decides for the legal group. What protects the station is the pre-flight (B.6) and the record: every change goes to the security log with its `Origin` (B.4 point 2), and changes from the web page or the CLI reach the CSMS as `ReconfigurationOfSecurityParameters` (B.11 point 5). SDNS §3.1.3 says so since 2026-10-29. The alternative was a switch `dns.ocpp: "readOnly"` for operators who set the resolvers locally.
8. **Revocation for chains to the roots of the operating system** (A.6 point 1; decided by Achim on 2026-11-05): checked as for the station's own roots, soft-fail. A certificate known to be revoked is refused; a status that cannot be had is not held against it. The fetch has a short time limit, because it runs inside the handshake. The alternative was to only document that they are not checked. Revocation offers little against an attacker on the path, since one who can block the resolver can block the OCSP or CRL fetch as well; pins and root groups are the stronger protection, and the paper says so.

   *Done on 2026-11-05* in WWCP_Node's shared `JudgeServer` (`WWCPNode.ServerCertificates.cs`):
   - **Where the handshake did not ask itself** (`ChainPolicy.RevocationMode == NoCheck`, as Hermod's DoT and DoH clients build the chain), the node asks. It rebuilds the machine's chain against the root it ended at, online, with `RevocationWait` (3 s) as `UrlRetrievalTimeout`. It asks only where a certificate in the chain, the root aside, names a CRL distribution point or an OCSP responder.
   - **The verdict:** revoked is refused before any pin is asked, as `untrusted`. The error step names the certificate: "its certificate …" or "the certificate of '…' in its chain". A status that could not be had adds a notice step and nothing else.
   - **Own roots:** `TryAnchor` asks with the same time limit (it had none) and reports a revoked certificate the same way, instead of "chains to no root".
   - **NTS:** NTS-KE servers and the station's meters go through `JudgeServer` as well. For their own roots, the time limit and the revoked step reach them too.

     Norn's NTS-KE client builds the machine's chain online itself (`NTSKE_TLSClient.NotifyServerCertificate`: `Online`, `EntireChain`, `NoFlag`, no time limit). For roots of the operating system it is therefore hard-fail today: a list out of reach is a chain error, then `TryAnchor`, then `untrusted`. The node does not ask such a chain again.

     Achim decided on 2026-11-06 that NTS becomes soft-fail there too, like DNS ("dann 2."). The other options were hard-fail with a 3 s time limit in Norn, and a switch per group. Norn is to stop checking, so that the node asks, with `RevocationWait` and the step naming what is revoked. This is the NTS session's to build, in Norn; it is not built yet.
   - **Tests:** 11 `ServerRevocationTests` in the node. They serve the lists over HTTP on the loopback, signed by the test's own CAs:
     - revoked, and on no list;
     - a revoked CA in the chain;
     - a list out of reach, and an OCSP responder out of reach;
     - a list never sent, both for the machine's chain and for an own root, each bounded by the wait;
     - a certificate naming no list;
     - a chain the handshake built online;
     - revoked under an own root, said as revoked;
     - a list of a root nobody trusts, which says nothing.

     They also pass under Linux (WSL, from the same build), together with the pinning tests: 69 green.
   - **11 mutations**, 10 of them red in exactly their tests. The eleventh lets a list of a root nobody trusts count; it cannot be seen on Windows, which does not report such a certificate as revoked (the mutants were run there only). The test stands for it all the same.
9. **Mixed transports** (settled on 2026-11-05: both): a list that mixes them is warned about, and `EncryptedOnly` is built (B.4 point 6).

### B.14 Implementation steps

The shared steps are those of NTS concept B.15 and are built **once**. DNS is then the smaller of the two bridges and suits being first.

1. **WWCP_OCPP**: reports and NotifyReport, removing components, the lookup bug, the extended `ValueSetterResponse`, the transaction hook (NTS concept B.15 steps 1–2).
2. **Node**: B.4 points 1–4 (event, `Origin`, certificate news, the locked update), together with NTS concept B.15 step 3, with kit tests. *Done on 2026-11-02* (B.4). The station's bridge passes `ocpp:<sender>` and refuses a change when the list was changed elsewhere (B.9).

   Tests:
   - 7 `DNSChangeTests` and 4 more `NameServerPinningTests` (6 cases) in the node.
   - One kit test over the JSON API, and an assertion in the kit's dry run that it tells no news.
   - 3 more `DNSDeviceModelWritingTests` in the station.
   - `ASourceIsToldWhoSentTheRequest_Test` in WWCP_OCPP.
   - 7 mutations, each red in exactly its test:
     - the change asked before the lock;
     - listeners told inside the lock;
     - the same verdict told again;
     - a learned pin not told;
     - the list not checked;
     - the list compared as written;
     - the request not handed on.
3. **WWCP_OCPP**: replace the placeholders with `DNSClientController`/`DNSServerController`, plus `NameServerRootCertificate` and the security event types (B.3).
4. **Bridge, reading**: sources against the node, registration in `BuildOCPPNodes`, GetVariables, the report.
5. **Node and bridge, writing**: the dry run (B.4 point 5), then B.9: `IndependentRequests` with the slot table and the exception for empty slots, then `ACID`/`BASE`, with the pre-flight.

   *Steps 3 to 5 done on 2026-10-30*, without `NameServerRootCertificate` and the security events:
   - WWCP_OCPP has the request-wide hook of NTS concept B.3 point 3: `ISetVariablesSource`, which a `ComponentConfig` or a single `VariableConfig` can name. ACID holds per source, and a source defines its BASE unit. `DNSVariable` became the shared `ControllerVariable`, and the extension statuses such as `Rolledback` are now written and parsed.
   - The node has the dry run.
   - The station's `DNSDeviceModel` writes through `TryUpdateDNSConfiguration`.

   B.4 points 1 to 4 followed on 2026-11-02 (step 2), point 7 on 2026-11-04, and point 6 on 2026-11-05.
6. **Certificates** (B.10), through the shared store interface, with B.4 point 8. *Done on 2026-11-03*, with `RootCAs`.

   Tests:
   - 5 more `NameServerPinningTests` cases in the node: a root that goes or no longer vouches takes its connections along, another root does not, and the dry run without a root, for a root for `dns` and for one of a group.
   - 7 `DNSRootCertificatesTests` in the station, with T21 against a name server over TLS on the loopback.
   - `RootCAsIsTheGroupOfRootsAServerIsHeldTo` in `DNSDeviceModelWritingTests`.
   - 8 mutations, each red in its tests:
     - the roots left out ignored;
     - the store not followed;
     - every connection let go of;
     - no check before a deletion;
     - the root not left out of that check;
     - no source on the charge point;
     - `RootCAs` not written;
     - `RootCAs` not checked.
7. **Monitoring and security events** (B.11). *Done on 2026-11-03.*

   Tests:
   - 9 `DNSEventsTests` in the station: a change on the page told to both versions dialled, one through OCPP not told back, a refusal told once, a mismatch told as refused, tolerated or recorded, another certificate, a learned pin told once, and monitors on the client and on a slot.
   - `EveryVariableMayBeMonitored_Test` in WWCP_OCPP.
   - `WhatIsNewsIsToldOnceTheJudgementIsKept` in the node.
8. **OCPP 1.6** (B.12). *Done on 2026-11-03.*

   Tests: 7 `DNSKeys16Tests`, covering the keys read from the node, a key changed as 2.1 changes its variable, the CSL rules with ACID, the two exceptions, `dns.servers`, the pins of the n-th server, and T19.

   20 mutations for steps 7 and 8, each red in exactly its tests:
   - the judgement kept after it is told (in the node, and in the station);
   - a slot, and the client, not monitored;
   - a change through OCPP told back;
   - a refusal, a mismatch used all the same, or another certificate not told;
   - a learned pin told again;
   - the monitors not asked at a change, or at news;
   - the service not tagged;
   - a port left out repeating the one before;
   - a new server taking nothing of the last;
   - a list not changed as one;
   - the central system not named;
   - the first server's pins for every n;
   - an empty entry not the one before;
   - no keys on the charge point.
9. **Equivalence tests**: the same change through `PUT /api/v1/configuration/dns` and through *SetVariables* yields the same file, the same state, and the same log except for `Origin`. Also SDNS T1–T18; emptying a slot and changing one behind it in the same request; the port rule; `pinsAsShown` against trust on first use between reading and writing; a compare-and-set conflict between the two paths; the pre-flight refusing name servers that cannot resolve the CSMS and `Enabled = false`; `NotSaved` (a read-only file) ⇒ `InternalError` with nothing in effect; after `BuildOCPPNodes` (an EVSE change) the components are still there. *Done on 2026-11-04* (station `035b358`, with the B.15 fixes in WWCP_Node `8bb5d49`).

   Tests:
   - 8 `DNSEquivalenceTests` in the station. The page goes through a started station that is signed in to; *SetVariables* and the 1.6 keys go through twins built from its configuration. The cases:
     - the client's settings changed on the page, through *SetVariables* and through the 1.6 keys;
     - the first server taken away and the two behind it changed, on the page and through *SetVariables* in one request, with the port of TLS left to the transport;
     - what one way changed, the others read;
     - a page change between a CSMS's read and its write is refused as `ValueChanged`;
     - a pin the CSMS set while the page was open is kept by the page's save;
     - a pin learned between a CSMS's read and its write is kept by its change, and refused as `ValueChanged` where the CSMS names the empty list it read;
     - T20 against two name servers of Hermod's own on the loopback over TLS, refused through *SetVariables* and through `dns.ports`, and accepted once the CSMS has installed the second server's root;
     - T17, with a directory where the file is written first: the page gets 500, OCPP 2.1 `InternalError` and OCPP 1.6 `Rejected`, and nothing is in effect.
   - 16 `DNSTestCriteriaTests` cases for the criteria that no other test tried as the paper states them (table below).
   - `ATransportIsTakenByItsNameAlone` (4 cases) in the node, for B.15.
   - The components after `BuildOCPPNodes` were already tried by `DNSDeviceModelTests.TheOCPPNodeReadsTheStationsNameResolution`.

   **What the files say.** The page sends every setting of its form and every server with its port, and the node writes what it is sent. OCPP writes only what it changes. So the same change leaves files that differ as text: `"port": 853` or `"useCache": true` is written out after the page's save, and left to its default after the CSMS's. Both files say the same, and a station started from either holds the same. The tests compare the files that way, and the node's state without its `file`.

   **T1 against the paper.** The paper expects "the causing entry `Rejected`". A server moved to UDP and given a root in one request is refused by the node as a server, and the bridge rejects every entry of that server, as neither alone would be refused. Entries of other components are `Rolledback`. The paper should say "the entries of that server" (to be confirmed).

   | T | Tests |
   |---|---|
   | T1 | `DNSTestCriteriaTests.APinForATransportThatCannotCarryItIsRefusedAsAWhole`; `DNSDeviceModelWritingTests.ARefusalOfTheNodeRollsTheRestBack` |
   | T2 | `DNSTestCriteriaTests.APinBeforeItsTransportIsRefusedOnlyEntryByEntry` (4 cases) |
   | T2a | `DNSTestCriteriaTests.ANewServerIsNeverAskedInClearText`; `DNSDeviceModelWritingTests.ANewServerComesWithItsAddress` |
   | T3 | `DNSTestCriteriaTests.ACertificateForAnotherAddressIsRefusedWhateverItIsHeldTo`; the node's `TimeServerPinningTests.ACertificateForAnotherNameIsRefusedWhateverItIsHeldTo`, through the same `JudgeServer` |
   | T4, T5 | `DNSTestCriteriaTests.OnlyARootForNameServersVouchesForANameServer`, installed by the CSMS as `CSMSRootCertificate`, `NetworkTimeRootCertificate` and `NameServerRootCertificate`; the node's `NameServerPinningTests.ARootForTheTimeServersAloneVouchesForNoNameServer` and `ANameServerOverTLSIsBelievedThroughARootOfTheNodesOwnForDNS` |
   | T6 | `DNSTestCriteriaTests.AFingerprintThatDoesNotMatchIsLoggedAsItsServerSays` (3 cases); `DNSEventsTests.AFingerprintThatDoesNotMatchIsTold` (3 cases) |
   | T7 | `DNSEventsTests.APinLearnedOnFirstUseIsToldOnce`; `DNSTestCriteriaTests.ARootLearnedOnFirstUseIsHeldAfterARestart`; the node's `NameServerPinningTests.ANameServerLearnsItsCertificateOnFirstUse` |
   | T8 | `DNSTestCriteriaTests.AServerHeldToARootAlreadyLearnsNone` |
   | T9 | `DNSEventsTests.AnotherCertificateThanBeforeIsTold`; the node's `NameServerPinningTests.AnotherCertificateThanBeforeIsNews` |
   | T10 | `DNSEventsTests.ARefusedNameServerCertificateIsToldOnce`; the node's `NameServerPinningTests.WhatIsNewsIsToldOnceAndBelievingAgainToo` |
   | T11 | the node's `NameServerPinningTests.AChangeToWhatANameServerIsHeldToIsSaidAndCountsAtOnce` |
   | T12 | `DNSTestCriteriaTests.OnlyEncryptedServersAreAskedAndNoneComesInClearText`; the node's `EncryptedOnlyTests.ServersInClearTextAreNotAskedWhenOnlyEncryptedOnesAre`, counted at the server, and `SwitchedOnTheServersInClearTextStayAndAreAskedAgainWhenItIsSwitchedOff` for the warning |
   | T13 | the node's `NameServersAskedTests.ANameServerOnItsOwnIsAskedOverWhatItIsConfiguredFor` (over TCP, never over UDP); Hermod's client asks a server over its own transport only (`DNSClient.GetOrCreateTransportClient`) |
   | T14 | `DNSDeviceModelWritingTests.NoServerAtAllIsRefused`; `DNSTestCriteriaTests.NoServerAtAllIsRefusedSwitchedOffToo` |
   | T14a | `DNSDeviceModelWritingTests.AServerKeepsItsIdWhenTheOneBeforeItIsTakenAway` |
   | T15 | WWCP_OCPP's `DNSControllerTests.TryCheck_SaysWhatThePaperAllows` (17 pins, timeouts and ports, `ValueOutOfRange`); `Servers` with more than `MaxServers` ids in `DNSDeviceModelWritingTests.ServersSaysWhichThereAreAndInWhichOrder` |
   | T16 | `DNSEventsTests.AChangeOnThePageIsToldToEachVersionThatIsDialled`; `DNSEquivalenceTests`, where the log line names `web:root` |
   | T17 | `DNSEquivalenceTests.WhatTheFileCannotTakeIsChangedNowhere`; `DNSDeviceModelWritingTests.WhatCannotBeSavedIsAnInternalError` |
   | T18 | WWCP_OCPP's `DNSControllerTests.GetBaseReport_ListsEveryServerWithItsCharacteristics_Test`; `DNSDeviceModelTests.TheOCPPNodeReadsTheStationsNameResolution`, with a server added on the page |
   | T19 | `DNSDeviceModelWritingTests.SwitchedOffWhileTheCSMSIsDialledByNameIsRefused`; `DNSKeys16Tests.SwitchedOffWhileTheCentralSystemIsDialledByNameIsRejected` |
   | T20 | `DNSEquivalenceTests.ANameServerTheCSMSWouldNotResolveThroughIsRefusedByEitherVersion`; `DNSDeviceModelWritingTests.AChangeThatCutsTheCSMSOffIsRefused` |
   | T21 | `DNSRootCertificatesTests.ARootTheNamesOfTheCSMSResolveThroughIsKept` |
   | T22 | `DNSEquivalenceTests.ANameServerTheLegalTimeWouldNotResolveThroughIsRefusedByEitherVersion`; `DNSDeviceModelWritingTests.AChangeThatLeavesTheLegalTimeNoNTSKEServerIsRefused`, `AChangeThatKeepsTheLegalTimeAnNTSKEServerIsMadeAndSaysWhatItLoses` |
   | T23 | `DNSRootCertificatesTests.ARootTheLegalTimeResolvesThroughIsKeptByTheBridge`, `ARootTheLegalTimeCanDoWithoutGoesAndTheAnswerSaysWhatItCosts` |

   17 mutations, each red in exactly its tests:
   - the port rule gone;
   - a learned pin forgotten by the bridge, with nothing said of what it showed;
   - a pin the page never showed taken away by its save (in the node);
   - compare and set gone;
   - the pre-flight asking the configuration as it is;
   - the node putting a change into effect before it is saved;
   - the page told 400 for a file that cannot be written;
   - `UseCache` written as `DNSSECOK`;
   - only the first entry of a refused server rejected;
   - independent requests made as one;
   - the entries of an unused slot not gathered by its address;
   - a certificate for another name forgiven where a pin matches;
   - a root of the time servers vouching for name servers as well;
   - a pin learned while one of its kind is held, past both guards;
   - a learned pin not written into the file;
   - a recorded mismatch kept out of the metrological log;
   - a transport taken by its number (in the node).
10. **Paper**: revised per A.6 in `bdfe66a`, and with the decisions of B.13 points 2 and 7 on 2026-10-29. Point 10 was settled by the NTS paper on 2026-10-30 (B.13 point 5).

### B.15 Side findings

- `DNSConfiguration.TryParseServer` reads the transport with `Enum.TryParse(…, ignoreCase: true, …)` (`DNSConfiguration.cs:253`), which also takes numbers. `"transport": "3"` is `HTTP`. `"99"` is an undefined transport: port 53, asked over UDP (the default arms in `DNSServerConfig.DefaultPortFor` and `DNSClient.GetOrCreateTransportClient`), and named `99://…` in the log. The same pitfall as for other enums read this way; `Enum.IsDefined` plus a refusal of digits would close it. *Fixed on 2026-11-04* (WWCP_Node `8bb5d49`): a transport is taken by its name alone, in any case, and anything else is refused with the known transports (`DNSConfigurationTests.ATransportIsTakenByItsNameAlone`). Lists of names such as `"TLS, UDP"` were taken as well, ORed together.
- WWCP_OCPP's OCPP 2.1 classes carry the schema limits of OCPP 2.0.1 in their comments: `attributeValue` 1000 (`SetVariableData.cs:143`), `additionalInfo` 512 (`StatusInfo.cs:51`). The OCPP 2.1 Edition 1 schema says 2500 and 1024. *Fixed on 2026-11-05* (WWCP_OCPP `2de93d9a`), along with the nine others a comparison of every schema comment in `WWCP_OCPPv2.1/DataStructures/Complex` with the schemas of `OCPPStateMachines/docs/schemas/v21` found: APN `apn`, `apnUserName`, `apnPassword`; VPN `server`, `user`, `group`, `password`; `Firmware.location`; `NetworkConnectionProfile.ocppCsmsUrl`. Only comments changed; no code checks these lengths.
- The doc comment of `PostDNSQuery` (`NodeHTTPAPI.NamesAndTime.cs`) says a query without `server` goes "to the servers in turn, until one answers"; the client asks all of them at once (`DNSClient.cs:840`). *Fixed on 2026-11-04* in the same commit: the comment says all at once, and the first usable answer counts.
- WWCP_OCPP's commit `06d64b5f` also added `NTSCtrlr` and `NTSServerCtrlr` in `WWCP_OCPPv2.1_ChargingStation/Components/Logical/NTS/`. Only `NTSCtrlr` is an `AuthCtrlr` copy like the two DNS placeholders; `NTSServerCtrlr` is a sketch with about 40 NTP and NTS-KE properties. NTS concept B.2 lists only `NTPClientController` and `NTPClientGroupController` in `WWCP_OCPPv2.1/Components/Logical/`. WWCP_OCPP `1bf9af1f` removed `NTSCtrlr` and kept `NTSServerCtrlr` (NTS concept B.3 point 10).
- On Linux, two roots of the store that signed themselves under the same name, without key identifiers, are one issuer to the chain: it takes the first root of that name, its signature does not fit, and the server whose root is the second one stays untrusted. Windows tells them apart. T20 in `DNSEquivalenceTests` was red in the Debian job for that alone, with two name servers' certificates both called `CN=localhost`; each has a name of its own since 2026-11-04. A real root CA carries a subject key identifier, by which a renewed one under the same name with a new key should be told apart; that was not tried. Two roots of one name without one would meet the same fate in the field.
