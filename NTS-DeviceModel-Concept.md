# NTS Groups: Comparison with the White Papers and a Concept for the OCPP Device Model

As of 2026-10-29. Compared:

- **Papers** (Whitepapers `ae72394`, identical to GitHub):
  [Secure Time Synchronization for OCPP](https://github.com/OpenChargingTechnology/Whitepapers/blob/master/SecureTimeSync/README.md) (**STS** below) and
  [Critical Legal Time Services](https://github.com/OpenChargingTechnology/Whitepapers/tree/master/CriticalLegalTimeServices) (**CLTS**)
- **Code**: WWCP_Node `1a8db65`, Norn `bd9d767`, WWCP_OCPP `b5d41d54`, ChargingStation `1499963`

Part A compares, Part B describes the missing glue code.

---

## Part A: Papers and Implementation

### A.1 What matches

| Topic | Paper | Implementation |
|---|---|---|
| Groups as free instances, "legal" and "local" well-known | STS §3.2.2 | `nts.groups.<id>`, id `^[a-z][a-z0-9-]*$` (≤ 32); the page suggests `legal`, `local`, `grid` (`NTSSection.cs`) |
| Priority per server: lower first, equal ones in parallel | STS §3.2.1 `Priority` (Byte) | `NTSServerConfiguration.Priority` (Byte), Norn `TimeSourceGroup.Bands()` |
| Quorum | STS `MinServers`, CLTS `quorum.minAgreeingServers: 2` | `minServers`, default **2** |
| Logging the deviation between servers | STS `MaxDeviation` 60 s | `maxDeviationSeconds`, default 60 s (0.001…3600) |
| Logging errors in a row, restricting features | STS `ErrorLogging` 5, `MaxErrors` 10 (local 50) | `errorLogging` 5, `maxErrors` 10, 50 for `local`; `IsLimited` plus the event `OnNTSGroupLimited` (`WWCPNode.NTSErrors.cs`) |
| Ports | NTS-KE 4460, NTP 123 | `ntsKEPort`, `ntpPort` per server, default 4460/123 |
| Logging configuration changes and certificate changes | STS §3: security log; root CA changes into the metrological log | Every change is logged as a diff line (`ApplyNTSConfiguration`). The legal group writes to the metrological log, all others to the normal log. News about time server certificates always goes to the metrological log (`WWCPNode.TimeServerCertificates.cs`). |
| Restricting root CAs | STS §2.1: not the whole browser bundle | **only partly**: by default the machine's roots plus the node's own TLS roots with usage `nts`; only the root pins per server narrow this (A.3) |
| Default servers | STS example: ptbtime1–3, `minServers 2` | without configuration the four PTB servers ptbtime1–4, quorum 2 |
| Hourly check | STS to-do (PTB-A 50.7) | `legalTimeMaxAgeSeconds` default 3600, `checkEverySeconds` default 900 |
| Fail closed instead of fail open | CLTS §1.4 | The word "legal" holds only when authority **and** enabled **and** fresh **and** close enough all hold; otherwise `legal: false` with `why` (`ClockJSON`) |
| AEAD | AES-SIV-CMAC-256, AES-128-GCM-SIV | Norn supports both; the negotiated algorithm is shown per server |

### A.2 Differences in the *NTP Client Group Controller* (STS §3.2.2)

| Variable | Paper | Implementation | Assessment |
|---|---|---|---|
| *Instance* | group id, free | group id, lowercase letters, digits and `-`, ≤ 32 characters | fits, but is narrower than OCPP (string[50], case-insensitive) |
| `Clients` | `List<String>` of NTPClientController instances, so **n:m** | servers **embedded** in the group (`servers[]`), so **1:n**; the same host name may appear in several groups. Key exchange, pins, judgements and KnownServers belong to the **host name** and are shared across groups. | model difference, see B.5 |
| `MinServers` | default **1** | default **2**. Without a value it is lowered to the number of enabled servers; an explicitly higher value is refused. | adjust the paper (CLTS says 2 as well) |
| `MaxDeviation` | "time discrepancies" ≥ threshold go to the metrological log, without saying *between what* | **spread between the servers** of one round (`TimeSyncVerdict.DeviationExceeded`). The deviation of the station's own clock is a separate value: `legalTimeToleranceSeconds`, default 1 s. | the paper mixes two quantities |
| `ErrorLogging` | always the "secure metrological log book" | metrological log only for `legal`, otherwise the normal log (as the STS v1.6 table has it for `local`) | align the v2.x table with v1.6 |
| `MaxErrors` | "limited charging session features" | the flag `IsLimited` and an event. Whatever evaluates it (tariff, load management) is not wired yet. | paper: define the signalling |
| Check interval | per **client** in v2.x (`MinInterval`/`MaxInterval`), per **group** in v1.6 | per **group**, a fixed period `checkEverySeconds` (10…86400), not spread randomly | a quorum needs simultaneous measurements, so the interval belongs to the group |
| (none) | missing | `enabled` per group; removal only while switched off | paper: name the OCPP standard variable `Enabled` |
| (none) | missing | `legalTimeAuthority` (≤ 80), `legalTimeToleranceSeconds` (0.001…60, default 1), `legalTimeMaxAgeSeconds` (10…86400, default 3600); allowed **only** in `legal` | these three decide whether the time is "legal", and the paper lacks them entirely |
| (none) | missing | `timeoutSeconds` (0.1…3600) | optionally into the paper |
| (none) | missing | `displayName` (a string, or language → text) | UI only, not needed in the paper |

### A.3 Differences in the *NTP Client Controller* (STS §3.2.1)

| Variable | Paper | Implementation |
|---|---|---|
| *Instance* | "NTP Client Identification" | no id of its own: servers are addressed by their **host name** within the group |
| `ServerURL` (URL) | the NTP server; NTS-KE optionally through `NTSKEServerURL` | `hostname` (DomainName, ≤ 253) **is the NTS-KE server**. There is no separate NTS-KE host. |
| `ServerPort` | default `""` | `ntpPort`, default 123 |
| `Mode` | NTPv4 / NTSv4 / NTPv4TLS | **NTSv4 only**. No plain NTP, no NTP over TLS (SMGW). |
| `DelayAsymmetry`, `DelayVarAsymmetry` | correction values | **not implemented**. *Per server since 2026-11-07*: `delayAsymmetryMs` and `delayVarAsymmetry`, as chrony's `offset` and `asymmetry` (Achim) |
| `Priority` | Byte | ✔ identical; the bands are asked one after another until one meets the quorum |
| `MinInterval`/`MaxInterval` | per client, random | per group, fixed (see A.2) |
| `Preflight` | a packet sent in advance | **not implemented**. *Per group since 2026-11-07*: `preflightSeconds`, chrony's `presend` |
| `ErrorLogging` (per client) | security log | **per group only** |
| `NTSKEServerURL`/`Port` | optional | only `ntsKEPort` per server |
| `RootCAs` | the name of a root CA group | **no groups per server.** Instead there are node-wide TLS roots with usage `nts` and **pins per server**: `certificates[]` and `roots[]` as SHA-256 fingerprints (≤ 16 each), plus `onMismatch` (refuse/record/accept) and `trustOnFirstUse`. As decided on 2026-10-20, the pins cover root CAs per group. |
| `NoCertTimeCheckAfterReboot` | Boolean | **not implemented** |
| `MinRefresh`/`MaxRefresh` | seconds in v2.x, **days** in v1.6 (30/60) | a fixed **30 minutes** (`Norn MonitoringConfig.NTSKERefreshInterval`), plus a cookie pool that is renewed when exhausted. *Per server since 2026-11-07*: `minRefreshSeconds`/`maxRefreshSeconds`, 43200/86400 where unset |
| `AEADAlgorithm` | selectable | Norn offers both (`OfferedAEADAlgorithms`), but the node does **not make it configurable**; only the negotiated algorithm can be read. *Configurable per server since B.4 point 5 (2026-11-05)*: `aead`, and unset offers both |
| `SignedResponses` | Boolean | Norn has the extensions (0xff00–0xff02) and `QueryTime(RequestSignedResponse…)`, but the **node never requests them** |
| (none) | missing | `enabled` per server |
| (none) | missing | pins, `onMismatch`, `trustOnFirstUse` (see above) |

### A.4 Fundamental differences

1. **Checking instead of setting.** STS §2.2 describes slewing and stepping the clock. The node **does not set the clock**: it measures the offset against the group and decides "legal, yes or no" (`"source": "system"`). Setting the clock stays with the operating system (chrony or similar). This is a legitimate and even well-auditable architecture ("the OS disciplines, the application verifies"), but the paper does not know it. Accordingly, the node also lacks `ntp.reboot.allowJumps` and the 5-second threshold for clock corrections from STS §2.2.
2. **Group-centred.** The implementation follows the paper's v1.6 structure (a group with a server list), not the v2.x model (standalone clients that groups refer to).
3. **State per host name.** Cookies and the key exchange (`timeEngine.KeyExchanges`), certificate judgements and KnownServers belong to the host name. Two groups with the same server share all of it; only the error counters belong to the group.
4. **One path for every configuration change.** Every change (web page, API, later OCPP) goes through `WWCPNode.TryUpdateNTSConfiguration(Group, JSON, …)`: parse, check pins and quorum, write the file (one read and one write, validated beforehand against the next start), apply, log the diff. Part B builds on this.

### A.5 Critical Legal Time Services compared with the implementation

CLTS is mostly about the **server and governance side** (PTB as the provider). On the client side, these parts concern the implementation:

| CLTS | Implementation |
|---|---|
| §5.1 signed NTS policy (JSON, RFC 8785) | not implemented |
| §5.1 `quorum.minAgreeingServers` | = `minServers` ✔ |
| §5.1 `quorum.maxPairwiseOffset_ns: 50` | corresponds to `maxDeviationSeconds`, whose lower bound in the node is 1 ms. A client cannot measure 50 ns across networks (see A.6). |
| §5.1 `hosts[].ntske.port`, `ntp.udpPort` | = `ntsKEPort`, `ntpPort` ✔ |
| §5.1 `tlsMinVersion: 1.2` | NTS-KE runs over TLS 1.3 (RFC 8915 requires it) |
| §5.2.1 `_nts-policy` TXT | not implemented |
| §5.2.2 SRV, §5.2.3 SVCB | not implemented; the servers are configured statically |
| §5.2.4 DANE/TLSA | not implemented. The pins are functionally similar: a certificate pin ≈ TLSA `3 0 1`, a root pin ≈ `2 0 1` (both SHA-256 over the whole certificate, not over the SPKI). |
| §5.2.5 DNSSEC | the DO bit is configurable (`dns.dnssecOK`), the status is in the diagnostics; no binding to NTS |
| §5.3 status API, §5.4 history | not used on the client side |
| "What NTS does not solve: wrong but correctly signed time" | partly solved: quorum plus a spread check across several servers |
| "Coordinating several time servers" | bands, quorum and a fallback band |
| "Proof of why the time was trustworthy" | the metrological log with every check, every error, every deviation, every certificate change and every configuration change |

### A.6 Where the papers must be revised

**Technical errors (first).** Points 1–7 were worked into both papers on 2026-10-29 (Whitepapers c9b1877, pushed to origin, github and git2). For point 5, CLTS §5.2.3 now specifies the owner name `_ntske.<service name>`, certificate validation against the service name (not against the target name, which would be safe only with DNSSEC), and weighting through distinct target names for the same server. For point 6, the policy JSON now has `quorum.servers` (ns) and `quorum.clients` (ms, example value 100 ms).

**Points 8–25** were worked into both papers on 2026-10-30, together with B.5–B.9 and the decisions of B.14 (Whitepapers 4e5e43d). STS §3.2 now has the components `ClockCtrlr` (3.2.1), `NTPController` (3.2.2), `NTPClientGroupController` (3.2.3) and `NTPClientController` (3.2.4), with the table layout of the SecureDNS paper (OCPP data types, *Values List / Limits*, *Reference Implementation*), SetVariables semantics (3.2.5) and security events (3.2.6). STS §3.3 maps the same onto OCPP 1.6 keys, and STS §8 relates the paper to CLTS, SecureDNS and Safe & Secure Device Model Updates. Where the papers now say more than, or something other than, Part B:

- **Lists are comma separated strings.** `ClientGroups`, `Clients`, `CertificatePins` and `RootPins` are `string`, not `MemberList` or `SequenceList`: both of those take only values of the `valuesList`, and group and client ids are free. B.7 and B.8 follow.
- **The check interval stays `MinInterval`/`MaxInterval`**, moved to the group, with a random time in between, instead of a new `CheckInterval`. The node has one fixed period, so it reports both as `checkEverySeconds` and accepts only the same value for both, in one ACID request. B.8 follows.
- `LegalTimeReason` uses OCPP-style values (`NotClaimed`, `Disabled`, `NeverChecked`, `Stale`, `OffBy`), mapped from the node's `why`.
- **New defaults, to be confirmed**: `MinRefresh`/`MaxRefresh` 43200/86400 s (once a day the certificate is checked again; the node uses a fixed 1800 s), and in OCPP 1.6 `ntp.legal.maxInterval` 1800 instead of 3600, so that it stays below `ntp.legal.maxAge` 3600.
- **Security events** proposed beside the OCPP ones (`SettingSystemTime`, `ReconfigurationOfSecurityParameters`): `LegalTimeLimited`, `LegalTimeRestored`, and, as for name servers, `InvalidTimeServerCertificate` and `TimeServerCertificateChanged`.
- The global 1.6 key `ntp.enabled` is replaced by `ntp.<group>.enabled`; the local group's keys are listed only where they differ from the legal ones.
- Point 23: `legalTimeCA1` twice in the `rootCAs` example was not a mistake: the list is per server. The description now says so.
- CLTS: the risk subsections are written out, with *Missing Audit Trails* added as §3.6 (the table had it, the subsections did not). The status document (§5.3) defines `id`, `level`, `quorum.ok` and `hosts[].level`, which the decision rules (§5.1.2) now refer to. The TXT record keeps the order `v; id; …` of the ABNF, now also stated in the rules.

The German ToDo list at the end of STS stays as it is.

1. *STS §2.1*: The NTS-KE request contains no nonce. The Unique Identifier belongs in the NTP request (RFC 8915 §5.3).
2. *STS §2.1*: NTS-KE returning the NTP server and port is **standardized** (RFC 8915 §4.1.7 and §4.1.8, records 6/7), not "no-longer-standardized".
3. *STS §3.2.1*: The model is the wrong way round. In NTS one configures the **NTS-KE server**, and it names the NTP server. `ServerURL` should therefore be the NTS-KE host (a host name, not a URL); the NTP host and port are the optional override.
4. *CLTS §5.1*: `tlsMinVersion: 1.2` contradicts RFC 8915, which requires TLS 1.3 for NTS-KE. 1.2 may apply to fetching the policy over HTTPS, so the two must be separated. The ALPN is `ntske/1`, not `ntlske`.
5. *CLTS §5.2.3*: Weighting through **several identical records** does not work. An RRset holds no duplicates, and servers suppress them (RFC 2181 §5). For a new protocol, SVCB also needs a mapping with owner names per RFC 9460 (e.g. port-prefix naming); `nts.ptb.de.` as an SVCB owner is not specified.
6. *CLTS §5.1*: `maxPairwiseOffset_ns: 50` and `maxServerUncertainty_ns: 500` make sense at most for the servers among themselves. A client measures across asymmetric networks in the millisecond range. The paper must name the perspective and provide a separate quantity for clients (seconds or milliseconds).
7. *CLTS §5.2.2*: The intersection rule (SRV ∩ policy ∩ NTS-KE list, empty ⇒ invalid) makes **every static or OCPP-managed configuration invalid**, which is exactly the operating case of CLTS §4.2. The paper must state that a missing SRV set means "no restriction", or that the configured list takes the place of SRV.

**Model and inconsistencies (STS)**

8. `MinServers`: default 2 instead of 1 (like the implementation and CLTS).
9. Define `MaxDeviation` as the **spread between servers**. Separately, add for the legal group the **tolerance** of the station's own clock, the **maximum age** of the last check, and the **authority**. Only these together yield "legal". Relate the 5 s threshold from §2.2 (clock corrections into the log) to these quantities.
10. `MinInterval`/`MaxInterval`, `Preflight` and error counting: v2.x has them per client, v1.6 per group. For quorum measurements they belong **to the group**; per client at most as an override.
11. Settle the log target per group uniformly: legal → metrological log, otherwise the security log. Today the v2.x group table and the v1.6 table for `local` contradict each other.
12. `MaxErrors`: define how the restriction becomes **visible**, e.g. as a read-only variable `Limited` plus NotifyEvent/SecurityEvent.
13. Name `Enabled` per group and per client through the OCPP standard variable `Enabled`; in v1.6 `ntp.<group>.enabled` instead of the global `ntp.enabled`.
14. Adding and removing groups: the Device Model has no way to create instances through SetVariables. The paper must say how instances come into existence: through `NTPController.ClientGroups`, as decided on 2026-10-29 (B.7, B.14 point 3). It should also state that a client belongs to exactly one group (1:n, B.5).
15. `RootCAs`: add pins (`CertificatePins`, `RootPins`, `OnPinMismatch`, `TrustOnFirstUse`), or state clearly that `RootCAs` means the `certificateGroup` of InstallCertificate. Part B maps both this way.
16. `MinRefresh`/`MaxRefresh`: settle on **one** unit (seconds). Today v2.x says "seconds" and v1.6 "days" with 30/60, while the implementation works with 30 minutes. Justify the defaults.
17. `DelayAsymmetry`: the JSON type "Integer" does not fit the example −0.05 ms, so it should be Decimal.
18. `Mode` for the legal group: NTPv4 is not "may be prohibited" but **MUST NOT**; only NTSv4 or NTPv4TLS (SMGW) are allowed.
19. Describe the verifying architecture (A.4 point 1) as an admissible variant.
20. CLTS §5.1.3: The decision rules refer to fields (`reports.overall_level`, `status.quorum_agreement.ok`, `status.health.level`) that are defined nowhere. The policy JSON needs an `id` field that matches `id=` in the TXT record.
21. The **relation to the standard `ClockCtrlr`** is missing. OCPP 2.x already has `ClockCtrlr` with `TimeSource`, `NtpSource`, `NtpServerUri[n]` and `TimeAdjustmentReportingThreshold`. The paper must say how the NTP client groups relate to it: the legal group feeds `ClockCtrlr`, writes to `NtpServerUri[k]` change the legal group (see B.6), and `TimeAdjustmentReportingThreshold` is the 5 s threshold from §2.2.
22. **Paper and reference implementation drift apart.** In WWCP_OCPP, `Mode` defaults to `NTPv4` unless an NTS-KE host is set; the paper says `NTSv4`. The group controller carries ClockCtrlr variables (`TimeOffset`, `TimeZone`, `NextTimeOffsetTransition…`) and has `MaxLogging` instead of `MaxErrors`. The names end in `…Controller` instead of the OCPP-customary `…Ctrlr`; on 2026-10-29 it was decided to keep `…Controller` and to call the new top component `NTPController` (B.14 point 2), so the paper adopts these names. Almost all paper variables are only C# properties there, not Device Model variables. Settle both sides together (B.3).

**Editorial**

23. STS: The `Mode` row lacks a column. The "Values List" column holds examples ("2 (servers)", "60"). Numeric defaults are given as `""`. The headings "NTS-Protected NTS Request/Response" must say "NTP". "3.3.1" appears three times. The local table has `ntske.ports` instead of `ntske.local.ports`. The prefix `nts.legal.signedResponses` differs from `ntp.`/`ntske.`. "CSV" next to "CSL". The default `nts` next to `NTSv4`. `legalTimeCA1` appears twice in the example. (The four links to the C# reference implementation still work.)
24. CLTS: Commas are missing in the JSON example (`quorum`); fixed along with point 6 in c9b1877. The first TXT example lacks the closing `"`. "If both uri nor d" and the "base domain" in rule 5 are unclear; according to the example it is the owner name without `_nts-policy.`. The ABNF enforces the order `v; id; …`, while the rules leave any order open: decide. German passages in §3 and §5. Empty subsections §3.1–3.8. `5.1.2` without `5.1.1`. `****` in §1.3.
25. Cross-references: CLTS §4.2 ("can be communicated through OCPP") should refer to STS §3, and STS should cite CLTS as its rationale.

---

## Part B: Glue Code between WWCP_Node and the OCPP Device Model

### B.1 Principle: one truth, two views

The node's NTS groups (the runtime object `NTSGroup` plus `nts.groups` in the file) remain **the only source**. The Device Model is a **projection** of them and keeps no stored state of its own for NTS variables. WWCP_OCPP is already built for this: `VariableConfig.Value` only calls the `ValueGetter`.

- **Reading**: Every NTS variable gets a `ValueGetter` that reads live from the node. Whatever the web page, the CLI or the API change in the node, GetVariables returns at once; there is nothing to synchronize.
- **Writing**: The `ValueSetter`, or the transaction hook (B.10), translates into the same partial JSON patch that the web page sends with `PUT /api/v1/configuration/nts/groups/{id}`. Then the same call `WWCPNode.TryUpdateNTSConfiguration(Group, JSON, out Error, out NotSaved)` runs.
- **Structure and events**: When groups or servers are added or removed, the instance list changes. Node events make the bridge register or unregister components and trigger monitors.

It follows that validation, the quorum check, pins, writing the file, log entries and applying live happen **exactly once, and the same way everywhere**.

Dependencies: WWCP_Node stays free of OCPP, because it serves every CLI (roaming hub, LC, …), and WWCP_OCPP's libraries stay free of the node. The bridge was built in the station program (`libs/ChargingStation`), next to `ConfigureWebPayments`, and written against `WWCPNode` alone, so that it could move.

*Moved on 2026-11-05*, with Achim's decisions of that day (SecureDNS concept B.13 point 1):
- **Where.** A project of its own in the WWCP_OCPP repository, `WWCP_OCPP_Node`, for both versions and for both bridges, the time servers' and the name servers'. It references `..\..\WWCP_Node`; WWCP_OCPP's other libraries do not.
- **What.** The bridge classes (`NTSDeviceModel*`, `DNSDeviceModel*`, `NTSRootCertificates`, `DNSRootCertificates`, namespace `cloud.charging.open.protocols.OCPP.Node`), and the wiring:
  - `DeviceModelBridge(Node, NamesToKeepResolving, DialsOCPP2_1, DialsOCPP1_6)` is made once per node. It follows the node's events and tells the CSMS, as `ChargingStation.NTSEvents.cs` and `ChargingStation.DNSEvents.cs` did.
  - `Configure(IDeviceModelNode)` and `Configure(IConfigurationKeysNode)` take over `ConfigureNTS` and `ConfigureDNS` of both versions. The node handed last is the one whose components follow the groups and servers (`FollowNTSGroups`, `FollowDNSServers`) and whose queue is told.
  - `IDeviceModelNode` (WWCP_OCPPv2.1) and `IConfigurationKeysNode` (WWCP_OCPPv1.6_ChargePoint) say what the bridge needs of an OCPP node. `AChargingStationNode` and `AChargePointNode` offer them, and a local controller gets the bridges with the same calls once its node does.
- **What stays in the station.** Which names its connections are dialled by and which versions they speak, `BuildOCPPNodes` with `ConfigureWebPayments`, and the queues on disk.
- **The tests** moved into `WWCP_OCPP_Node_TestKit` as abstract fixtures over `IBridgedNode`, as WWCP_Node_TestKit's tests are. `WWCP_OCPP_Node_Tests` runs them against a bare `WWCPNode` with the test nodes of both versions, and the station's suite against a station. The rebuild for changed EVSEs became `IBridgedNode.NewOCPPNodes()`. That the queue is a file beside the configuration is the station's own test (`StationBridgeTests`).

File names in the sections below are those of the day a step was built.

```
 Web page / CLI / API ───┐                         ┌── CSMS: Get/SetVariables, GetReport,
                         ▼                         ▼   InstallCertificate, …
             WWCPNode.TryUpdateNTSConfiguration ◄── DeviceModelBridge (WWCP_OCPP_Node)
             WWCPNode.TryAddNTSGroup / TryRemove…      ▲   ValueGetter/Setter, Tx hook
                         │                            │            │
          file nts.groups + NTSGroup (runtime)        │            ▼
                         │                            │     WWCP_OCPP Device Model
                         └── OnNTSGroupChanged ───────┘     (NTPController, NTPClientGroup…,
                                                             NTPClient…, ClockCtrlr)
```

### B.2 Current state in WWCP_OCPP and in the station program

(WWCP_OCPP `b5d41d54`, ChargingStation `1499963`; paths relative to `libs/`.)

| Area | Today |
|---|---|
| `NTPClientController`, `NTPClientGroupController` (`WWCP_OCPP/WWCP_OCPPv2.1/Components/Logical/`) | Present; component name = class name, instance = id. As Device Model variables, however, there are only `DateTime`/`TimeOffset` (client) and ClockCtrlr-like variables (group). `ServerURL`, the ports, `Mode`, `Priority`, `RootCAs`, `MinServers`, `MaxDeviation`, `ErrorLogging` … are plain get-only properties, and none has a `ValueSetter`. The group has no `Clients` variable. The classes are registered only in tests. |
| Lookup | `AOCPPNetworkingNode.TimeClientController(Instance)` and `NetworkTimeClientController(Instance)` look up under `nameof(TimeClientController)`, which is wrong. `LegalTimeClientController` is therefore always `null`. |
| Storage | `OCPPAdapter.componentConfigs` is a `ConcurrentDictionary<String, List<ComponentConfig>>`. Adding goes through `AddOrUpdateComponentConfig`; **removing is not possible at all**. Duplicates are possible, the inner list is not thread-safe, and the comparison is case-sensitive (OCPP requires case-insensitive). |
| SetVariables (`WWCP_OCPPv2.1_ChargingStation/TestChargingStationNode.cs` from line 2159) | Only `IndependentRequests`; the `BASE`/`ACID` branches (OCPP_CSE `DataConsistencyModel`) are empty. `AttributeType` is ignored. `ValueSetterResponse(NewValue, ErrorMessage)` knows only yes or no, so there is no `RebootRequired`, no `NotSupportedAttributeType` and no `reasonCode`. `OldAttributeValue` (compare-and-set, OCPP_CSE) is passed to the setter as `oldValue`. |
| Dispatch | `OCPPWebSocketAdapterIN.CallProcessor` calls **only the first** subscriber. A second `OnSetVariables` handler of the station would never run. |
| Reports, monitoring | GetBaseReport/GetReport answer `Accepted` but never send `NotifyReport`; there is no conversion from ComponentConfig to `ReportData`. Monitoring requests are accepted and not stored, and `NotifyEvent` never comes. **A CSMS cannot discover the NTS components today.** |
| InstallCertificate | In 2.1, `certificateGroup` is parsed and ignored. There is one certificate per type, in memory, and it is stored **even on Rejected**. GetInstalledCertificateIds reports everything as `CSMSRootCertificate`. In 1.6 there is no handler, and the answer is `Failed`. |
| 1.6 configuration | `AChargePointNode.Configuration` is `protected`; `ConfigurationData` has no callback; ChangeConfiguration creates unknown keys. |
| Security events | The helpers `SendSecurityEventNotification` (2.1 and 1.6) exist, but nothing calls them. |
| Station | `BuildOCPPNodes` builds `TestChargePointNode` (1.6) and `TestChargingStationNode` (2.1), **anew on every EVSE change**. The only bridge is `ConfigureWebPayments`: run once and in one direction only. A CSMS that sets a variable on `WebPaymentsCtrlr` reaches neither the display nor the file. This is the same problem as with NTS, and the pattern here solves it too. |
| WWCP_Node | no OCPP references (and it should stay that way) |

### B.3 What WWCP_OCPP lacks

1. **Build controllers from variables instead of fields.** One `VariableConfig` per paper variable, with `VariableCharacteristics` (DataType, min/max limit, ValuesList, SupportsMonitoring). Getters and setters come from one source, e.g. the interfaces `INTPClientGroupSource` and `INTPClientSource`, read and write per variable, plus a transaction entry point for ACID. WWCP_OCPP thereby keeps the paper's variable list, because the paper links to exactly this reference implementation. The station only supplies the source. Today's data classes stay as an in-memory source for tests.
2. **Extend `ValueSetterResponse`** with `SetVariableStatus` and `StatusInfo` (`reasonCode`, `additionalInfo`), check `AttributeType`, and make the setter asynchronous (`Func<…, Task<ValueSetterResponse>>`), because removing a group is asynchronous in the node. Only then can the bridge say `ReadOnly`, `ValueOutOfRange` or `NotSupportedAttributeType`.
3. **ACID and BASE in the SetVariables handler.** The request already has the field `DataConsistencyModel`. Collect all entries and hand them, per source, to an asynchronous hook such as `Task<IEnumerable<SetVariableResult>> TrySetVariablesAsync(IEnumerable<SetVariableData>)`. The failing entries get `Rejected`, the others `Rolledback`. These are the semantics of the white paper [Safe & Secure Device Model Updates](https://github.com/OpenChargingTechnology/Whitepapers/tree/master/SafeAndSecureDeviceModelUpdates).
4. **Remove components** (`TryRemoveComponentConfig(Name, Instance)`), prevent duplicates, stay thread-safe, compare case-insensitively.
5. **Reports**: ComponentConfig/VariableConfig → `ReportData`, plus `NotifyReport` on GetBaseReport/GetReport (paged, `tbc`). Without this, no CSMS finds the NTS components.
6. **Monitoring**: store monitors. An entry point `VariableChanged(component, variable)` produces `NotifyEvent` with `trigger = Delta`.
7. **Certificates** through an exchangeable store interface instead of `ConcurrentDictionary<InstallCertificateUse, Certificate>`: InstallCertificate, GetInstalledCertificateIds and DeleteCertificate, several certificates per type, `certificateGroup` passed through, nothing stored on `Rejected`.
8. **Fix the lookup bug** in `AOCPPNetworkingNode` (lines 122 and 151).
9. **1.6**: make configuration keys registrable with a getter and setter (like `VariableConfig`). Unknown keys ⇒ `NotSupported`.
10. **Remove the placeholders** `NTSCtrlr` and `NTSServerCtrlr` (`WWCP_OCPPv2.1_ChargingStation/Components/Logical/NTS/`); `NTPController`, `NTPClientGroupController` and `NTPClientController` replace them (B.14 point 2). `NTSCtrlr` is a copy of `AuthCtrlr` without NTS variables. `NTSServerCtrlr` is not: it is a work-in-progress sketch (06d64b5f) with about 40 NTP and NTS-KE settings as plain properties, among them cookie refresh, revocation check and signed responses, but no Device Model variables. Despite its name it describes one time server as a client sees it (host names, `Group`, `Priority`, `Weight`, status). Weighed on 2026-10-30 and deleted in step 2:
    - *In STS 3.2.3/3.2.4 already*: the NTP and NTS-KE host and port (`ServerURL`, `ServerPort`, `NTSKEServerURL`, `NTSKEServerPort`), the delay asymmetries, `Preflight`, the request timeout (`Timeout`), the error logging threshold (`ErrorLogging`), the poll intervals with their randomization (`MinInterval`/`MaxInterval`, random in between), root CA and server certificates with their validation and logging modes (`RootCAs`, `RootPins`, `CertificatePins`, `TrustOnFirstUse`, `OnPinMismatch`), `NoCertTimeCheckAfterReboot`, the cookie refresh (`MinRefresh`/`MaxRefresh`), `AEADAlgorithm`, `SignedResponses`, `Enabled`, `Group` (a client's one group) and `Priority`; of the status, the last synchronization (`LastKeyExchange`, the group's `LastCheck`), the offset (the group's `LastOffset`) and the cookies left (`Cookies`).
    - *Dropped*: `NTP_Version` (always 4); `NTS_KE_MinTLSVersion` (RFC 8915 requires TLS 1.3); `ServerTimeZone`, `TimeOffset` and `CurrentTimestamp` (NTP is UTC, local time is `ClockCtrlr`'s); `Weight` (a round asks every client of a priority, so there is nothing to distribute); the validation modes `skip_validation` and `skip_lifetime_validation` (they would undo NTS); and `NTS_KE_C2S_Key`/`NTS_KE_S2C_Key`, because key material must never be a Device Model variable.
    - *Candidates the paper does not have* (Achim's call): `IPVersionPreference`, `IPHopLimit`, the rate limits for NTP and NTS-KE, `AllowInitialBurst`, `CertificateRevocationCheck` (OCSP/CRL), `DesiredNumberOfCookies`, `ServerCertificate_Extensions`, `ServerLocation`; of the status, `Stratum`, `Delay`, `Jitter`, `SyncStatus`, `LastErrorMessage` and `NextScheduledSync`.

Because only the first subscriber is dispatched, these hooks must sit **inside** the handlers of `TestChargingStationNode`/`TestChargePointNode`. An additional handler in the station is not enough.

### B.4 What the node lacks

1. **Change events**: `OnNTSGroupChanged(NTSGroup, IReadOnlyList<String> Changes, String Origin)`, `OnNTSGroupAdded`, `OnNTSGroupRemoved`. Today there are only `OnNTSGroupLimited` and `EventLog.OnLogged`, and parsing log lines would be wrong. The natural trigger point is the end of `ApplyNTSConfiguration`, where `changed.Count > 0` is logged.
2. **Origin**: an optional parameter `Origin` (`web:<account>`, `cli:<account>`, `ocpp:<CSMS id>`) for `TryUpdateNTSConfiguration`, `TryAddNTSGroup` and `TryRemoveNTSGroupAsync`. The log line "NTS configuration changed: …" then names it. STS §3 requires logging *every* change, and as evidence one also needs *who* made it.
3. **Stable server id** (decided, B.14 point 4): an optional field `"id"` per server entry, unique across the whole node, with the syntax of group ids. It is the instance of `NTPClientController`. The web page must send `id` back with the server list, as it does today with `pinsAsShown`. Entries without `id` get the derived id `<group>-<n>`. The bridge fixes it in the file on the first OCPP write to the group, not at start.
4. **Several groups at once** for ACID: `TryUpdateNTSConfigurations(IEnumerable<(NTSGroup, JObject)>, Origin)`. All patches are applied to **one** document that was read once, checked once (`NTSSection.TryParse`), written once and then applied. `TryWriteNTSSection` already works with "one read, one write"; this only extends it to several groups.

   *Points 1–4 built in step 3 (2026-11-01, WWCP_Node eb2066c), with these refinements:*
   - The events carry `String? Origin`, null for a caller that names none. They are raised after `reconfigureLock` is released, so a listener may change the node in turn, and each listener is called on its own, so one that throws stops neither the others nor the save. A save that changes nothing raises nothing. A pin learned on trust on first use (`LearnTimeServerPin`) raises `OnNTSGroupChanged` with `tofu`.
   - The forms of point 2 live in `ChangeOrigin` (`Web`, `CLI`, `OCPP`, `Start`, `TOFU`, `IsOCPP`), agreed with the SecureDNS session for its own B.4 points 1 and 2. The log lines end in " (by web:root)". No CLI command changes NTS groups yet, so `cli:` is not used here.
   - A server id may be up to 50 characters, the length of an OCPP instance, so the derived id of a server in a group with a 32-character name fits too. A server without an id gets `<group>-<n>` for its place n, or the next free number where a server of the group has that id. The uniqueness check covers derived ids too; a legal group that names no servers counts as `legal-1` to `legal-4`. GET shows each server with its id, the page sends the ids back (any save of the list fixes them), and a change of the ids is part of the change line (`server ids = …`).
   - `TryUpdateNTSConfigurations` reports in `out NTSGroup? Refused` which group caused a refusal (null where only the section as a whole is refused, e.g. an id in two groups), so the bridge can mark that group's entries `Rejected` and the others `Rolledback`. A group named twice is refused.
   - Still open, as in SecureDNS B.4 point 4: reading the servers and writing them whole are two steps, and a server added on the page in between is lost. The bridge can catch that only once the read and the write share the lock. *Closed in step 5*: `TryUpdateNTSConfigurations(Func<IReadOnlyList<NTSGroup>, …>)` asks the change for its parts under `reconfigureLock`, as `TryUpdateDNSConfiguration(Func<JObject, JObject?>)` does for DNS. The bridge compares each group's servers there with the ones the request read, and refuses the change as `ValueChanged` where they differ.
5. **Pass AEAD through** (small): Norn has `NTSServerEndpoint.OfferedAEADAlgorithms`; the node needs the field `aead` per server and has to pass it on in `ToEndpoint()`.

   *Built on 2026-11-05*, with Achim's decisions of the same day:
   - **Unset offers both, as before.** A server without `aead` offers Norn's list, AES-128-GCM-SIV first and then AES-SIV-CMAC-256, and the server chooses. A set `aead` offers that one algorithm alone. This deviates from the paper's default of AES-SIV-CMAC-256 on purpose, so that nothing changes for existing servers.
   - **What OCPP reads.** `AEADAlgorithm` reads the algorithm the entry sets. Unset, it reads the one the last key exchange agreed on, or "" before the first. Writing "" goes back to unset, so WWCP_OCPP marks the variable `MayBeEmpty`.
   - **The 1.6 key is built too.** `ntske.<g>.aead` is one value per server, comma separated (B.13).

   Found on the way: Norn's `MeasurementEngine.MeasureNTSKE` never passed `NTSServerEndpoint.OfferedAEADAlgorithms` to its client. The key exchange of every round therefore offered the client's whole list, whatever the endpoint said. It passes the list on now. A cached key exchange whose agreed algorithm the endpoint no longer offers is not reused (2 tests in `MeasurementEngine_Tests`).

   How it works:
   - **The node.** `aead` takes "AES-SIV-CMAC-256" or "AES-128-GCM-SIV", in any case, and is written back in capitals. Other values are refused: AES-256-GCM-SIV, which Norn can perform but has never run against another implementation, AES_SIV_CMAC_256 in Norn's own spelling, and anything that is not a string.
   - **By host name across all groups**, as with the pins. A server is one server with one key exchange in Norn's cache. The first entry that names an algorithm decides, so a group that lists the same server without one does not undo it (`WWCPNode.TimeServerAEAD.cs`).
   - **On every change.** `OfferAEAD()` puts the offer on every group's endpoints and rebuilds a group's detailed-test client whose offer is wrong. A changed offer lets go of the server's key exchange and is a change line of its own: "a.example offered AES-SIV-CMAC-256 alone", or "… offered AES-128-GCM-SIV and AES-SIV-CMAC-256". A removed group's offers are recomputed as well, and the removal's log line names any that changed. The detailed test offers what its server is offered.
   - **The API and the page.** Each entry of `timeSources[]` shows its own entry's `aead`, which the page sends back, beside the agreed `aeadAlgorithm`. It is not the effective offer from another group, which a save of this group would otherwise write into it. `limits.aeadAlgorithms` lists the two. The server dialog has a select: every one (the server choosing), or one alone. `entryOf` keeps a server's `aead`, so a save of another server does not take it away.
   - **The bridge.** The bridge's `Server`, which writes a group's entries anew on every OCPP change, carries `aead` through, so a change of a server's priority keeps it. A server added by `ntp.<g>.servers` takes the last server's set `aead`, not the algorithm it agreed on.

   Tests:
   - Node: 11 `NTSServerAEADTests` and the kit test `AServerIsOfferedTheAEADAlgorithmItsEntryNames`.
   - Frontend: 4 tests.
   - WWCP_OCPP: `TryCheck_TheAEADAlgorithmIsOneOfTheTwoOrNone_Test`.
   - Station: `AServerIsOfferedOneAEADAlgorithmOrEveryOne` (SetVariables), `TheAEADAlgorithmIsAListWhoseEmptyEntryOffersEveryOne` and `ANewServerOffersWhatTheLastOnesEntryNames` (1.6), and the AEAD algorithm in `TheSameServersLeaveTheSameFileWhicheverWayTheyCame`.
6. **A reporting threshold for offsets** (legal only): `offsetReportingThresholdSeconds`, the target of `ClockCtrlr.TimeAdjustmentReportingThreshold` (B.6).

   *Built on 2026-11-03*, with Achim's decisions of the same day: reported when the threshold is reached, five seconds where nothing says otherwise, and switched off by an empty value.
   - The key takes whole seconds from 1 to 86400, or `false` for never. A page's emptied field (`""`) is never as well. The key left out is the five seconds of STS §2.2, so never is written down as `false`, not by removing the key. Only the legal group may say it, like the other legal keys.
   - The node does not set its clock, so the threshold applies to the measured offset, as STS §2.2 allows. A check that finds the clock at least the threshold off is written into the metrological log as `Critical` (tags `offset`, `security`) and raised as `OnNTSOffsetReached(NTSGroup, TimeSpan Offset)`.
   - That is said once. The next check that finds the clock closer is written down as "within … again" (`Notice`), and only after it is the threshold reached anew. A persisting offset is the same offset, and at a check every 15 minutes it would otherwise be 96 events a day. New servers do not make it new; a threshold switched off forgets that it was reached.
   - A change of the threshold is a change line of its own ("offsets reported from 7 s", "offsets not reported"). The page edits it on the legal card, where an empty field is never.

### B.5 Components and instances

| OCPP component | Instance | In the node | Life cycle |
|---|---|---|---|
| `ClockCtrlr` (standard) | none | the legal group, summarized | fixed |
| `NTPController` *(new, extends the paper)* | none | the NTS section as a whole | fixed |
| `NTPClientGroupController` | group id (`legal`, `local`, …) | `NTSGroup` / `nts.groups.<id>` | added and removed through `NTPController.ClientGroups` |
| `NTPClientController` | stable server id (B.4 point 3) | one entry in `servers[]` | added and removed through `NTPClientGroupController[g].Clients` |

**1:n instead of n:m.** A client belongs to exactly one group, and the bridge refuses an id in two groups. The reasons: `Priority` is only relative within one group, the quorum and the error counters belong to the group, and the node is built this way. If the same server should be in two groups, it gets two entries with different ids; they share cookies and pins through the host name anyway. The paper should switch to 1:n.

**Registration**: The bridge registers its components in `BuildOCPPNodes` after every build and rebuild, like `ConfigureWebPayments`. In addition it reacts to `OnNTSGroupAdded/Removed` and to server ids that are new or missing in `OnNTSGroupChanged`.

### B.6 The standard `ClockCtrlr`

A standard CSMS knows only `ClockCtrlr`. The bridge fills it from the legal group, so even a CSMS without the NTS extension sees where the time comes from, and it takes writes to `NtpServerUri` into the legal group.

| Variable | Mutability | Node | Note |
|---|---|---|---|
| `DateTime` | ro | `TimeProvider.GetUtcNow()` | |
| `TimeSource` | rw (standard) | `NTP,RealTimeClock` while `legal` is enabled, otherwise `RealTimeClock` | Only the current value is accepted; anything else ⇒ `Rejected`. The NTS configuration determines the source. |
| `NtpSource` | ro | `manual` | |
| `NtpServerUri[1…9]` | rw | the enabled servers of the legal group, in query order | by position, see below (B.14 point 5) |
| `TimeAdjustmentReportingThreshold` | rw | `offsetReportingThresholdSeconds` (B.4 point 6) | When a check finds an offset ≥ the threshold ⇒ `SecurityEventNotification` `SettingSystemTime` with the offset as `techInfo`. This is the 5 s value from STS §2.2. |
| `TimeOffset`, `TimeZone`, `NextTimeOffsetTransition…` | n/a | n/a | not an NTS matter; stays as it is |

**Writing `NtpServerUri`** (decided on 2026-10-29, B.14 point 5). OCPP makes the variable's instance a single digit, the NTP priority (1 = highest). The node has priorities 0…255 and asks servers of equal priority together, as one band, and Norn requires *each band on its own* to reach the quorum (`TimeSourceGroup.Measure`). An index of its own per server would make bands of one server that never reach a quorum of 2. The index therefore stands for the position in query order:

- **Reading**: `[k]` is the k-th enabled server of the legal group in query order (priority, then file order). Up to nine are shown. When fewer than nine are enabled, the report also lists the next free index `[n+1]` with an empty value, so a standard CSMS sees where a server can be added.
  *As built in step 4 (2026-11-02)*:
  - All nine indexes are in the report, and those beyond the servers asked read empty, as the DNS slots do. The first empty one shows where a server can be added.
  - A server reads as `nts://host`, with `:port` where its NTS-KE port is not 4460.
  - A node without a legal group reads nine empty ones.
  - `TimeOffset` (with and without the instance `NextTransition`), `TimeZone` and `NextTimeOffsetTransitionDateTime` are left out until the node has them, so a CSMS reads them as unknown.
  - `TimeAdjustmentReportingThreshold` came with B.4 point 6 (2026-11-03). It reads the legal group's threshold in whole seconds, and "" where it is never or there is no legal group. A write goes into the legal group's patch, with its `NtpServerUri` entries under ACID. "" is never (`false` in the file), and below 1 or above 86400 is `ValueOutOfRange` (`MinLimit`/`MaxLimit` of the variable in WWCP_OCPP).
- **`[k] = host` at an existing position** replaces that server's host name, and its NTS-KE port when the value is `nts://host:port`. The entry keeps its id, priority, root pins, `onMismatch` and `trustOnFirstUse`, and loses its certificate pins, which name the old server's certificate. The diff line in the metrological log shows both.
- **`[n+1] = host`** adds a server with that host name, enabled, with the default ports, in the band of the last server (with its priority), so it counts towards the same quorum. An index beyond `n+1` ⇒ `Rejected`, with the next free index in `additionalInfo`.
- **`[k] = ""`** removes the k-th server, and the later ones move up one index. If the node refuses, e.g. because fewer servers would be enabled than an explicit `minServers`, its sentence goes into `additionalInfo`.
- **Values** are host names or `nts://host[:port]`, as for `NTPClientController.ServerURL`. `ntp://…` or any other scheme ⇒ `Rejected`, `InvalidValue`, because the node speaks NTS only.
- **Consistency**: Each write is one patch through `TryUpdateNTSConfiguration`, so a standard CSMS can use `IndependentRequests`. Under ACID, `NtpServerUri` entries go into the legal group's patch together with its `NTPClientController` entries; two entries of one request that change the same server ⇒ both `Rejected`, all others `Rolledback`.
- **What stays elsewhere**: priorities and further bands, the NTP port, pins, the quorum and any server beyond the ninth are changed through `NTPClientController` and `NTPClientGroupController`.

### B.7 Variables: `NTPController`

| Variable | Type | Mutability | Node | Note |
|---|---|---|---|---|
| `ClientGroups` | string, comma separated | rw | the group ids | A new id ⇒ `TryAddNTSGroup` (switched off, without servers). A missing id ⇒ `TryRemoveNTSGroupAsync`; if the group is still enabled ⇒ `Rejected` with "switch it off first". Not a `MemberList`, which takes only values of the `valuesList`. |
| `SuggestedGroups` | string, comma separated | ro | `NTSSection.SuggestedIds` minus the existing ones | like `suggested` in the API |
| `LegalTime` | boolean | ro | `ClockJSON().legal` | a monitoring target |
| `LegalTimeReason` | OptionList | ro | `ClockJSON().why` | `NotClaimed`, `Disabled`, `NeverChecked`, `Stale`, `OffBy`, from the node's `notClaimed`, `ntsOff`, `neverChecked`, `stale`, `offBy` |

### B.8 Variables: `NTPClientGroupController[<id>]`

| Variable | Type (characteristics) | Mutability | Node key | Note |
|---|---|---|---|---|
| `Enabled` | boolean | rw | `enabled` | OCPP standard variable |
| `Clients` | string, comma separated | rw | `servers[].id` | A new id ⇒ a new entry, whose `ServerURL` must come in the same ACID request (B.10). A missing id ⇒ the entry is removed. The order does not matter; `Priority` decides. |
| `MinServers` | integer, min 1, max = enabled servers | rw | `minServers` | Actual = the value set (`Group.Quorum`) |
| `EffectiveMinServers` | integer | ro | `Sources.MinServers` | lower when fewer servers are enabled |
| `MaxDeviation` | decimal s, 0.001…3600 | rw | `maxDeviationSeconds` | the spread between servers |
| `ErrorLogging` | integer 1…65535 | rw | `errorLogging` | |
| `MaxErrors` | integer 1…65535 | rw | `maxErrors` | |
| `MinInterval`, `MaxInterval` | integer s, 10…86400 | rw | `checkEverySeconds` | Moved from the client to the group (STS 3.2.3). As long as the node does not spread randomly, both read as `checkEverySeconds`, and only the same value for both is accepted, in one ACID request; unequal values ⇒ `Rejected` |
| `Preflight` | integer s, 1…86400 | rw (since 2026-11-07) | `preflightSeconds` (false = never) | moved from the client to the group. "" where there is none, and "" takes it away. A round begun this long after the last one sends each server its NTS query a first time and ignores the answer. Not the pre-flight check of name resolution (B.13 point 6) |
| `Timeout` *(new)* | decimal s, 0.1…3600 | rw | `timeoutSeconds` | |
| `LegalTimeAuthority` *(new, legal only)* | string[80] | rw | `legalTimeAuthority` | An empty string ⇒ removed (`RemovesLegalTimeAuthority`) |
| `LegalTimeTolerance` *(new, legal only)* | decimal s, 0.001…60 | rw | `legalTimeToleranceSeconds` | |
| `LegalTimeMaxAge` *(new, legal only)* | integer s, 10…86400 | rw | `legalTimeMaxAgeSeconds` | |
| `DisplayName` *(optional)* | string[80] | rw | `displayName["*"]` | language-neutral only; the language variants stay with the web page |
| `Limited` | boolean | ro | `IsLimited` | a monitoring target (B.12) |
| `ErrorsInARow` | integer | ro | `ErrorsInARow` | |
| `LastCheck` | dateTime | ro | `LastCheck` | |
| `LastOffset` | decimal s | ro | `LastCheckOffset` | |
| `Answered` / `Asked` | integer | ro | `LastCheckAnswered` / `LastCheckAsked` | |

The "legal only" variables exist **only** in the instance `legal`. In any other instance ⇒ `UnknownVariable`, just as the node refuses them in other groups.

### B.9 Variables: `NTPClientController[<id>]`

| Variable | Type | Mutability | Node key | Note |
|---|---|---|---|---|
| `Enabled` | boolean | rw | `servers[i].enabled` | |
| `ServerURL` | string[253] | rw | `servers[i].hostname` | A host name is the **NTS-KE server**. `nts://host[:port]` is accepted as well (the port ⇒ `NTSKEServerPort`). |
| `ServerPort` | integer 1…65535 | rw | `ntpPort` | default 123 |
| `NTSKEServerURL` | string | ro | = `ServerURL` | a separate KE host ⇒ `Rejected` until the node supports it |
| `NTSKEServerPort` | integer | rw | `ntsKEPort` | default 4460 |
| `Mode` | OptionList `NTSv4` | ro | n/a | other values ⇒ `Rejected` |
| `Priority` | integer 0…255 | rw | `priority` | |
| `RootCAs` | string[50] | rw | `rootGroup` (new) ⇒ `roots[]` | B.11 |
| `CertificatePins` *(new)* | string, comma separated SHA-256 | rw | `certificates[]` | ≤ 16 |
| `RootPins` *(new)* | string, comma separated SHA-256 | rw | `roots[]` | ≤ 16 |
| `OnPinMismatch` *(new)* | OptionList `Refuse,Record,Accept` | rw | `onMismatch` | |
| `TrustOnFirstUse` *(new)* | OptionList `None,Root,Certificate` | rw | `trustOnFirstUse` | |
| `AEADAlgorithm` | OptionList `AES-SIV-CMAC-256,AES-128-GCM-SIV` | rw (since B.4 point 5) | `aead` / negotiated | "" offers both, the server choosing, and reads the negotiated one |
| `Cookies`, `LastKeyExchange`, `Certificate`, `Judgement` | various | ro | state per host name | like `timeSources[]` in the API |
| `MinRefresh`/`MaxRefresh` | integer s, 60…5184000 | rw (since 2026-11-07) | `servers[i].minRefreshSeconds`/`maxRefreshSeconds` | 43200/86400 where the entry sets none (Achim, 2026-11-07); by host name across every group, as `AEADAlgorithm`; a `MaxRefresh` before the `MinRefresh` ⇒ `Rejected` |
| `DelayAsymmetry` | decimal ms, −1000…1000 | rw (since 2026-11-07) | `servers[i].delayAsymmetryMs` | added to every offset measured with the server, chrony's `offset`; "0" where unset, and "" or 0 unsets |
| `DelayVarAsymmetry` | decimal, −0.5…0.5 | rw (since 2026-11-07) | `servers[i].delayVarAsymmetry` | chrony's `asymmetry` (Achim, 2026-11-07): the offset is corrected by it times the delay beyond the shortest of the server's last 16; "0" where unset. Both by host name across every group, the first entry setting either deciding |
| `NoCertTimeCheckAfterReboot`, `SignedResponses` | n/a | n/a | not implemented | not in the report ⇒ `UnknownVariable`; report them only once the node supports them. `Preflight` and `ErrorLogging` are variables of the group now (B.8). |

### B.10 Writing: OCPP → node

**Pre-check per entry** (in all modes):

| Case | Status | `reasonCode` |
|---|---|---|
| unknown component or instance | `UnknownComponent` | none |
| unknown variable | `UnknownVariable` | none |
| `attributeType` ≠ Actual | `NotSupportedAttributeType` | none |
| ReadOnly | `Rejected` | `ReadOnly` |
| outside min/max | `Rejected` | `ValueOutOfRange` |
| type or format error | `Rejected` | `InvalidValue` |
| `OldAttributeValue` ≠ the getter's current value | `Rejected` | a code of its own, e.g. `ValueChanged`, to be settled in the Safe & Secure paper |

The last case (compare-and-set) is what **really makes it irrelevant whether the web page or OCPP is used**: if someone changes a value on the web page while the CSMS sets the same value, the CSMS overwrites nothing it has not seen. Symmetrically, the web API could get an ETag on `GET /nts/groups/{id}` and `If-Match` on `PUT`, as the page already does for pins with `pinsAsShown`.

**Depending on `DataConsistencyModel`:**

- **`IndependentRequests`** (standard OCPP, without the field): every entry on its own, in request order, each with one patch, one `TryUpdateNTSConfiguration` and its own result, as the standard intends. Rules spanning several fields therefore apply per entry, and the CSMS must order the entries accordingly (enable clients first, then raise `MinServers`; the other way round when lowering). **New client ids** in `Clients` are not possible here, because a server entry needs a host name ⇒ `Rejected`, `additionalInfo`: "use DataConsistencyModel ACID". Whoever knows the NTS extension also knows ACID.
- **`ACID`** (OCPP_CSE): All NTS entries of the request become one patch per group and then one call to `TryUpdateNTSConfigurations` (B.4 point 4). Groups added through `NTPController.ClientGroups` are created first, and groups removed through it are removed last. The result: everything `Accepted`, or the causing entries `Rejected` and all others `Rolledback`.
- **`BASE`**: like ACID, but each group is atomic on its own.

*As built in step 5 (2026-11-02)*:
- **New instances.** An entry of `NTPClientController[<new id>]` or `NTPClientGroupController[<new id>]` would be `UnknownComponent` before the bridge sees it. So WWCP_OCPP asks an `INewInstancesSource` first (Achim's decision of 2026-11-02, a hook rather than placeholder instances). The bridge makes a stand-in for a server only where the request's `Clients` of a group names it, and for a group only where the request's `ClientGroups` names it. The stand-in is not added to the node, and GetVariables does not see it.
- **IndependentRequests.** Each entry finds what the entries before it left. After `NtpServerUri[2] = ""`, the server that was `[3]` is `[2]`. With ACID and BASE all positions are those the request found.
- **ACID and groups.** Groups are made and taken away around the one save of the groups' patches. If the save is refused, the groups made are taken away again. A group that cannot be taken away after the save is the one case where a change is left half done: its `ClientGroups` entry is `Rejected`, and the rest is `Accepted`, because it is in effect. A group is taken away only when it is switched off.
- **BASE units.**
  - A group is a unit with its servers.
  - `ClockCtrlr` is in the unit of the legal group.
  - `NTPController` and groups made by the request form one unit "groups".
- **Hosts.**
  - A server given another host keeps its id, priority, root pins, `onMismatch` and `trustOnFirstUse`, and loses its certificate pins, unless the same change sets them. This holds through `ServerURL` as well as through `NtpServerUri`.
  - `ntp://` and every other scheme but `nts://` are refused.
- **Interval and timeout.**
  - `MinInterval` and `MaxInterval` are written as the one `checkEverySeconds`, and only when both are the same value. Setting one alone to another value is refused.
  - `Timeout` cannot be emptied, because the node reads an empty one as "the file does not say".
- **`TimeSource`** is accepted only as it reads.

`servers` is always sent **whole**, because the node replaces lists whole: the current list with all changes from the request. One patch yields one log line per group, like saving on the page. If the node refuses, its sentence goes into `additionalInfo` (≤ 1024). `NotSaved` ⇒ `Rejected` with `InternalError`, the counterpart of the web API's 500/NotChanged. `RebootRequired` never occurs, because the node applies everything live. The admin role check in the SetVariables handler stays in front of all this.

### B.11 Certificates: `NetworkTimeRootCertificate` and `certificateGroup`

The station answers InstallCertificate, GetInstalledCertificateIds and DeleteCertificate for `NetworkTimeRootCertificate` through the store interface from B.3 point 7, backed by the node's `CertificateStore`.

- **InstallCertificate**(`NetworkTimeRootCertificate`, `certificateGroup = G`) ⇒ a TLS root in the node's store with usage `nts` and a group mark `ntsca-<g>`. Usages are `[a-z][a-z0-9_-]*` with ≤ 32 characters, so a `certificateGroup` that cannot be mapped ⇒ `Rejected`. The event goes to the metrological log (STS §3).
- **`NTPClientController.RootCAs = G`**: *Achim's decision of 2026-11-03*: the group lives in the node, and is not resolved into pins. The server entry gets `"rootGroup": G`, and the node anchors that server at the roots marked `ntsca-<g>` alone. Root pins stay pins and narrow that further, as STS 3.2.4 says. The plan was that the bridge would write the group's fingerprints as `roots[]` and recompute them whenever a root of G came or went. That would have put RootCAs and RootPins into one field, so that a CSMS writing RootPins overwrote the group.
- **A new or deleted certificate in G** (through OCPP or the web page) counts from the server's next NTS-KE handshake, because the node asks the store at every handshake. Nothing needs recomputing.
- **`RootCAs` empty** ⇒ no group. The node's rule applies (the machine's roots plus its own `nts` roots), which corresponds to the paper's fallback chain.
- **GetInstalledCertificateIds**(`NetworkTimeRootCertificate`) lists the roots with usage `nts`, whether they were imported through OCPP or through the web page.

*As built in step 6 (2026-11-03)*:
- **The node**:
  - `ServerPins.RootGroup` is read and written with the pins, under `"rootGroup"`, and shown in `heldTo`.
  - The mark is `<service>ca-<group>` (`ServerPins.MarkOf`). A group has up to 24 letters, digits, '-' or '_', so that the mark of every service, "meterca-" included, fits the 32 characters of a usage.
  - For a server held to a group, `JudgeServer` takes neither the machine's chain nor a root without a usage nor a pinned root outside the group. A group and pins narrow each other and do not add up. These are the three leaks the SecureDNS session pointed out.
  - A page save keeps a group the page did not change. What a mismatch comes to goes where no pin is left beside a group, because the file refuses it there.
  - The page's pin fields gain the group. Its `pins.ts` carries it, so the DNS and meter pages keep it too.
  - The node honours a group for every service. Only NTS has an OCPP side to set it.
- **WWCP_OCPP**:
  - `ICertificateSource` (`WWCP_OCPP_Common`) keeps the roots of some types for both versions, by the names of the types.
  - `CertificateSources` is a node's set of them, with the certificates in memory for every other type.
  - A deletion asks every source that has the certificate first (`CheckDeletionAsync`, asynchronous since WWCP_OCPP 451e785b, because the DNS source's check is a network pre-flight), and deletes it from all of them only where none fails. A root held by the NTS and the DNS source alike is thus deleted for both; a usage no OCPP source manages, such as `meter`, stays.
  - Hashes are those of OCSP. The node used to list the issuer's name, its own key in hex and .NET's serial.
  - The 1.6 charge point, which had no certificate handlers, answers through the same sources.
  - `NameServerRootCertificate` is added in both versions, for the DNS bridge.
- **Station**:
  - `NTSRootCertificates` is the source of `NetworkTimeRootCertificate` for both nodes.
  - Listed are the roots that vouch for time servers: those for `nts`, and those for every use.
  - A root for every use is not deleted for the time servers alone (`Failed`, `ForEveryUse`). One kept for another service loses `nts` and its marks, and stays.
  - Installations and deletions go into the metrological log, with the CSMS that asked.

### B.12 Node → OCPP: changes from elsewhere

1. **Values**: Nothing to do here, because the getters read live.
2. **Structure**: `OnNTSGroupAdded/Removed` and new or missing server ids ⇒ register or remove components (B.3 point 4).
3. **Monitoring**: `OnNTSGroupChanged` ⇒ the bridge compares the projected values with the last reported state and calls `VariableChanged` for every changed variable, which yields `NotifyEvent` with `Delta` (B.3 point 6). This state is only a note for monitoring, not a second source.
4. **`OnNTSGroupLimited`** ⇒ `Limited` changes, and for `legal` also `NTPController.LegalTime`. For `legal`, a `SecurityEventNotification` is sent as well (vendor-specific, e.g. `LegalTimeLimited`/`LegalTimeRestored`).
5. **Local changes**: Changes to the legal group that did **not** come through OCPP (`Origin` web or cli) ⇒ `SecurityEventNotification` `ReconfigurationOfSecurityParameters` with the node's diff line as `techInfo` (cut to the field length, complete in the log). This way the operator learns of local changes (NIS2, CLTS §4.2).
6. **Offset ≥ `TimeAdjustmentReportingThreshold`** ⇒ `SettingSystemTime` (B.6).
7. **No echo**: The bridge's own changes carry `Origin = ocpp:…`, so point 5 does not apply to them. Monitors fire nevertheless, because the node normalizes (host names, a lowered quorum, a removed authority).
8. **Manual edits** of the file take effect only at the next start, as before. The projection is built from the node at start, so the two cannot drift apart.

*As built in step 7 (2026-11-03)*:
- **Monitors** (WWCP_OCPP `VariableMonitors`):
  - `Delta`, `UpperThreshold` and `LowerThreshold` can be set on a variable that supports monitoring. The NTP controllers' variables do, and so do `ClockCtrlr`'s, all but `DateTime`.
  - The monitors watch nothing themselves. `CheckMonitorsAsync` reads each monitored value and compares it with what its monitor saw last. The bridge asks after every node event, and the node asks itself every 10 s, because `LegalTime`, `LastCheck` and `Limited` change without a node event.
  - No copy of a value is kept, only each monitor's last value. That is point 3's "last reported state", per monitor.
  - Monitors are kept in memory. The CSMS sets them again after a restart.
- **Security events** (`ChargingStation.NTSEvents.cs`):
  - Point 5 covers every group and a group added or removed, as STS 3.2.6 says. A pin learned on first use counts too, through `OnNTSGroupChanged` by `tofu`.
  - Point 4 is `LegalTimeLimited`/`LegalTimeRestored`.
  - `OnServerNews` of a time server: `Refused` ⇒ `InvalidTimeServerCertificate`, `CertificateChanged` ⇒ `TimeServerCertificateChanged`.
  - Point 6 is `SettingSystemTime` since B.4 point 6 (2026-11-03), on `OnNTSOffsetReached`, with "nts.groups.legal: the clock is +7.008 s off, the threshold is 5 s" as `techInfo`. It is told once per reaching, not at every check.
  - An event goes to each OCPP node that a configured connection uses, never to a node nothing dials.
- **The queue** (Achim's decision of 2026-11-03: on disk):
  - `OfflineQueue` keeps security events and monitor events in `ocpp-queue/queue-2.1.jsonl` and `queue-1.6.jsonl` beside the configuration, until the CSMS takes them, across restarts.
  - It holds at most 1000 messages for at most 30 days. When full, monitor events go before security events.
  - Queues and monitors belong to the station and are handed to every node built for it.
  - techInfo is cut at 255 characters, and the log keeps it whole.

### B.13 OCPP 1.6

The same pattern applies to the keys from STS §3.3, once WWCP_OCPP can register keys with a getter and setter (B.3 point 9). `GetConfiguration` computes the CSL values from the groups. `ChangeConfiguration` becomes a patch for `TryUpdateNTSConfiguration`, and the bridge takes over the paper's CSL rules (a list that is too short ⇒ the last value keeps applying, one that is too long ⇒ an error). Limits: ChangeConfiguration carries one key per request, so bundling is impossible. `ntp.legal.servers` and `ntp.legal.minServers` must therefore come in an admissible order, and `Rejected` carries no reason. In 1.6, pins and new groups stay reserved to the web page. InstallCertificate with `certificateGroup` exists in 1.6 as well (STS extends the 1.6 Security Extensions with it) and goes through the same store.

*As built in step 8 (2026-11-03)*:
- **One way to change.**
  - A 1.6 key is turned into the SetVariables entries of the NTP controllers it stands for, and handed to the same Device Model, with ACID where it is several. 1.6 thus has the checks, the refusals and the log line (`ocpp:<id>`) of 2.1.
  - A change is accepted where every entry is, and rejected otherwise.
  - The keys are computed when asked: a pair of pin keys per server, and the legal keys for the legal group alone.
- **Pins.** Unlike the plan above, pins *are* writable through 1.6, because STS §3.3.2 defines `ntske.<g>.certificatePins.n` and `rootPins.n`. Groups are still not made or taken away through 1.6.
- **Lists.**
  - A list is per server, in file order, switched-off servers included.
  - `ntp.<g>.servers` renames servers at their places, keeping their ids and pins.
  - A new server at the end gets the last server's ports, priority, root group and pin rules, but none of its pins.
  - Servers beyond the end are taken away.
- **One interval.** `minInterval` and `maxInterval` each set the node's one interval, because 1.6 cannot set both at once.
- **Mode.** `mode` takes `NTSv4` alone.
- **Left out.** Keys the node does not have are not listed, and a change of one is `NotSupported`. These are `ntske.<g>.servers`, `signedResponses` and `ntp.reboot.*`.
- **The preflight.** `ntp.<g>.preflight` came on 2026-11-07: the group's `Preflight`, "" for none.
- **The asymmetries.** `ntp.<g>.delayAsym` and `delayVarAsym` came on 2026-11-07, lists per server like the others, "0" where nothing is corrected.
- **The refresh.** `ntske.<g>.minRefresh` and `maxRefresh` came on 2026-11-07. They are lists per server in seconds, reading 43200 and 86400 where nothing sets another, the last value holding for the rest. A `maxRefresh` before a server's `minRefresh` is `Rejected`, as in 2.1.
- **AEAD.** `ntske.<g>.aead` came with B.4 point 5 (2026-11-05). It is a list like the others except for an empty entry, which is a server that offers every algorithm, not the one before it. A server whose entry names none reads as `AEADAlgorithm` does: the algorithm agreed on, or "".
- **The threshold.** `ntp.timeAdjustmentReportingThreshold` came with B.4 point 6 (2026-11-03). It is `ClockCtrlr.TimeAdjustmentReportingThreshold` and so the legal group's, listed where there is a legal group. It reads "5" by default and "" for never, and an empty value switches the report off.

### B.14 Decisions

Settled on 2026-10-29:

1. **The CSMS may change the legal group**, always, with no switch to lock it; it is written like every other group. What protects it is the record: every change goes to the metrological log with `Origin = ocpp:<CSMS id>` (B.4 point 2), and changes from the web page or the CLI reach the operator as a `SecurityEventNotification` (B.12 point 5). The paper should say so: the legal group is writable through OCPP, and every change to it is an entry in the metrological log.
2. **The names end in `…Controller`**: `NTPController` (the new top component, B.7), `NTPClientGroupController` and `NTPClientController`, as in WWCP_OCPP today and in the paper's text. `ClockCtrlr` keeps its standard name. The placeholders `NTSCtrlr`/`NTSServerCtrlr` are removed (B.3 point 10). The SecureDNS concept (`SecureDNS-DeviceModel-Concept.md`) should follow with `DNSClientController`/`DNSServerController`, so both extensions are named alike; that is still open there and in the SecureDNS paper.
3. **Groups come and go through `NTPController.ClientGroups`** (B.7), with free ids as in the node, and without implicit instances. The paper adds `NTPController` with `ClientGroups`.
4. **Server ids are a field `id` in the node file** (B.4 point 3), passed through by the web page; entries without one get `<group>-<n>`, fixed on the first OCPP write to the group. Ids derived from host names were ruled out: an instance holds at most 50 characters and a host name up to 253, and a new host name would rename the instance.
5. **`ClockCtrlr.NtpServerUri` is writable**, by position, as B.6 describes: a standard CSMS can replace, add and remove legal servers. A new server joins the band of the last one instead of getting a priority of its own, because every band must reach the quorum on its own.

The alternatives that were weighed:

- *Legal group*: a switch `nts.groups.legal.ocpp: "readOnly"` for setups under German calibration law (Eichrecht), read-only or writable by default; or a legal group that is always read-only through OCPP.
- *Names*: the OCPP-customary `NTPCtrlr`, `NTPClientGroupCtrlr` and `NTPClientCtrlr`, like the standard components and like the SecureDNS concept today.
- *Groups*: `legal`, `local` and `grid` always reported as instances ("virtual", switched off) that come into existence on the first write. That is friendlier to a standard CSMS but covers neither free ids nor removal. Or both ways at once.
- *Server ids*: derived from host names in the bridge alone, or positions `<group>-1…n` as for the DNS servers in the SecureDNS concept. Neither needs a node change, but the first renames the instance with its host name, and the second shifts the instances when a server is removed.
- *`NtpServerUri`*: read-only, with changes only through `NTPClientController`; or replacing host names only, without adding or removing servers.

### B.15 Implementation steps

1. **WWCP_OCPP**: reports and NotifyReport, removing components, the lookup bug, the extended `ValueSetterResponse`, the placeholders removed (B.3 points 2, 4, 5, 8, 10). This is the prerequisite for everything, and it helps every component.
2. **WWCP_OCPP**: build the NTP controllers from variables with a source interface (B.3 point 1), with characteristics as in tables B.8 and B.9. *Built on 2026-10-30, read-only first, like the DNS controllers; WWCP_OCPP c811e28c (2026-11-01, on top of 57694cf3; pushed and pinned on 2026-11-01)*: `NTPController` (new), `NTPClientGroupController` and `NTPClientController` read every value through `INTPControllerSource` at every read and keep none. Their variables are the tables of STS 3.2.2–3.2.4 as data, in the `ControllerVariable` record the DNS controllers use as well. `ClientGroups` and `Clients` come from the source's groups and their clients, so they cannot disagree with the instances. A variable the source does not list is `UnknownVariable`, the variables of legal time exist in the instance `legal` only, and every variable is `ReadOnly` until the setters exist. `NTPController.AllOf(Source)` makes all components; a client named by two groups belongs to the first. The ClockCtrlr variables, `MaxLogging`, `AllowJumpsAfterReboot` and the n:m client list are gone, and so is `NTSServerCtrlr` (B.3 point 10). Tests: 9 in `NTPControllerTests`; the 5 that ask only over the wire are red on 752a9ce5 (the old classes know none of the paper's variables and answer the clock variables), and on top of WWCP_OCPP 57694cf3 (the DNS session's `ControllerVariable` and `ISetVariablesSource`) the 2.1 suite passes 1964/1964. Still open: the `valuesList` of `Mode` lists all three modes, while the paper wants only the supported ones (the node has NTSv4 only). The place for it is an optional `Supported` list in `ControllerVariable.ToVariableConfig` that narrows the `valuesList` and that `TryCheck` honours; DNS does not need it, since the node takes every `Transport`. *Closed on 2026-11-05 in WWCP_OCPP f1de4e03*: `INTPControllerSource.SupportedClientValues(Variable)` says what a source supports, null by default for every value. `NTPClientController` hands it to `ToVariableConfig(…, Supported)`, whose `valuesList` lists only those values, in the paper's order and spelling (`ControllerVariable.ValuesOf`). `NTPClientController.TryCheck(Source, …)` refuses a value of the paper the source does not support as `InvalidValue` and says what it supports. `NTSDeviceModel` supports `NTSv4` only, so a CSMS reads the `valuesList` `NTSv4` for every server; `Mode` stays `ReadOnly`, and 1.6 already accepted only `NTSv4`. The DNS controllers pass nothing and are unchanged. And the standard `ClockController` is still registered as "ClockController" instead of "ClockCtrlr" (step 4).
3. **Node**: events, `Origin`, the server `id` (with the frontend), the multi-group update (B.4 points 1–4), kit tests. *Built on 2026-11-01: WWCP_Node eb2066c, on top of 339e2c7 (pushed and pinned on 2026-11-02)*, with the refinements noted under B.4. Contents:
   - The node: `ChangeOrigin`; the events `OnNTSGroupChanged`, `OnNTSGroupAdded` and `OnNTSGroupRemoved`; `Origin` on `TryUpdateNTSConfiguration`, `TryAddNTSGroup` and `TryRemoveNTSGroupAsync`, passed by the JSON API as `web:<account>`; `TryUpdateNTSConfigurations`, which the single-group update now calls as well; `NTSServerConfiguration.Id` with `IdsOf` and `IsServerId`, and `NTSConfiguration.ServerIdsIn`; the uniqueness check across groups in `NTSSection.TryParse`; `NTSGroup.ServerIds`; and `id` in `timeSources`.
   - The page sends each server's id back and shows it in the server dialog.

   Tests:
   - 10 `NTSChangeTests` and 11 `NTSServerIdTests`.
   - 3 kit tests over the JSON API. They are red on 339e2c7; in the station's conformance fixture (station a9ea3a3, in a scratch composite) they pass, and the fixture passes 171/171.
   - 3 frontend tests, red on the old page code.
   - The tests that need the new API were checked by mutations instead:
     - Telling listeners inside the lock makes `AListenerMayChangeTheNodeInTurn` red.
     - Writing the groups one by one makes the four all-or-none tests red.
     - Without the check across groups, the three id tests go red.
   - The node suite passes 1184/1184 (SourceRules rerun with the artifacts inside the repo), and the frontend 660/660.
4. **Bridge, reading**, in ChargingStation: sources against the node, `ClockCtrlr`, registration in `BuildOCPPNodes`. *Built on 2026-11-02: WWCP_OCPP 1b452d02, on top of c811e28c, and station b8ef0f8, on top of a9ea3a3, with WWCP_Node eb2066c (pushed and pinned on 2026-11-02).* Contents:
   - **WWCP_OCPP**:
     - `ClockController` is read from an `IClockControllerSource` at every read, like the NTP controllers. It is registered under the standard's name `ClockCtrlr`; it used to take nine values and register as `ClockController`, and nothing registered it.
     - Its variables are the standard's. `NtpServerUri` has the instances 1 to 9, and `TimeOffset` has none and `NextTransition`.
     - `ControllerVariable.ToVariableConfig` takes an optional instance.
   - **Station**:
     - `OCPP/NTSDeviceModel` is the source of `ClockCtrlr` and of the three NTP controllers. It reads the node (`NTSGroups`, `NTSConfigurationJSON(group)`, `ClockJSON`) at every read, through the JSON the page reads.
     - `ConfigureNTS` registers all of them in `BuildOCPPNodes`.
     - `FollowNTSGroups` keeps one component per group and server on the node built last. It follows `OnNTSGroupAdded`, `OnNTSGroupRemoved` and `OnNTSGroupChanged` (new or missing server ids, a server moved to another group).
   - **Left out of `NTPClientController`**, so they read as unknown:
     - `NTSKEServerURL`: the node has no separate key exchange server.
     - `RootCAs`: step 6.
     - `MinRefresh`/`MaxRefresh`: the node does not say Norn's `NTSKERefreshInterval`, and a fixed 1800 would be a second truth.
     - `DelayAsymmetry`, `DelayVarAsymmetry`, `NoCertTimeCheckAfterReboot` and `SignedResponses`.
     - `Preflight` is left out of the group.
   - **How some values read**:
     - `ServerURL` reads without the final dot the node writes.
     - The pins and `OnPinMismatch`/`TrustOnFirstUse` are what the node holds the host name to (the page's `heldTo`). A host named in two groups shows the entry that says something about it first.
     - `AEADAlgorithm` is the one the last key exchange agreed on, "" before the first. Since B.4 point 5 it is the one the server's entry names, where it names one.
     - `Problem` is a refused certificate at the last key exchange.
     - `LegalTimeReason` maps the node's `ntsOff` to `Disabled`.

   Tests:
   - 6 `ClockControllerTests` in WWCP_OCPP. They do not compare `DateTime` over the wire, because of the parsing finding in B.16.
   - 10 `NTSDeviceModelTests` in the station, including one through the OCPP node's GetVariables: groups and servers made, changed and taken away, and a node rebuilt for changed EVSEs. That one is red on station a9ea3a3, where the node knows neither ClockCtrlr nor the NTP controllers.
   - The tests that need the new API were checked by mutations:
     - Not following the groups, or following only the first node built, makes the wire test red.
     - `NtpServerUri` in file order, switched-off servers included, makes the clock test red.
     - A group's or a server's JSON kept from the first read makes the change test red.
     - `ClockCtrlr` under the old name makes 5 of the 6 clock tests red.
   - The 2.1 suite passes 1970/1970. The station suite passes 578/578, SourceRules rerun with the artifacts inside the repo.
5. **Bridge, writing**: IndependentRequests, then ACID/BASE (B.3 point 3), `NTPController.ClientGroups` and `ClockCtrlr.NtpServerUri` (B.6). *Built on 2026-11-02: WWCP_Node e05d41d, on top of 95b7e84, WWCP_OCPP a4e4182a, on top of d7e16490, and station 3e1de56, on top of d40fb26 (pushed and pinned on 2026-11-03)*, with the refinements noted under B.10. Contents:
   - **WWCP_Node**:
     - `TryUpdateNTSConfigurations` takes a change it asks for its parts under the lock (B.4 point 4).
     - The overload with fixed parts goes through it.
   - **WWCP_OCPP**:
     - `INTPControllerSource` and `IClockControllerSource` say which variables they write.
     - The NTP controllers and `ClockController` are ReadWrite there and hand every write to their source, as the DNS controllers do.
     - `TryCheck` helpers say why a value does not fit the paper.
     - `ControllerVariable.TryCheck` takes SequenceList and MemberList values.
     - `INewInstancesSource`, with `NewInstanceLookupDelegate`, `DeviceModel.SetVariablesAsync(…, NewInstances)`, `AChargingStationNode.AddNewInstancesSource`/`NewInstanceOf`, and `TestChargingStationNode` passing it. Stand-ins come from `NTPClientGroupController.NewInstance` and `NTPClientController.NewInstance`.
   - **Station**:
     - `OCPP/NTSDeviceModel.Writing.cs` is the `ISetVariablesSource` and `INewInstancesSource` of the NTP controllers and of `ClockCtrlr`.
     - It writes, through the node:
       - `ClientGroups`.
       - Of a group: `Enabled`, `Clients`, `MinServers`, `MaxDeviation`, `MinInterval`/`MaxInterval`, `Timeout`, `ErrorLogging`, `MaxErrors` and the three of legal time.
       - Of a server: `Enabled`, `ServerURL`, `ServerPort`, `NTSKEServerPort`, `Priority`, the pins, `OnPinMismatch` and `TrustOnFirstUse`.
       - Of `ClockCtrlr`: `NtpServerUri` and `TimeSource`.
     - Each server is sent back with its id and `pinsAsShown`, as the page sends it.
     - Compare-and-set: an `OldAttributeValue` that is no longer the value is `ValueChanged`.
     - A variable set twice in one request is refused.
     - Changes are written down as `ocpp:<sender>`.
     - `ConfigureNTS` registers the model as the node's source of new instances.

   Tests:
   - WWCP_Node: 2 `NTSChangeTests` for the change under the lock.
   - WWCP_OCPP: 3 `NewInstancesTests` over the wire, and 2 `NTPControllerTests` (a writing source's controllers and stand-ins; `TryCheck`).
   - Station: 20 `NTSDeviceModelWritingTests` through the station's OCPP 2.1 node. The three written against the old `SetVariablesAsync` are red on station d40fb26, where every write is `ReadOnly`.
   - The rest were checked by nine mutations, each making its tests red:
     - In the node, the change asked for before the lock is taken.
     - In WWCP_OCPP, the new-instance lookup not asked, and the group controller never ReadWrite.
     - In the station:
       - no check of the list under the lock;
       - groups made not taken away again;
       - a new `NtpServerUri` server in band 0;
       - certificate pins kept across a rename;
       - no compare-and-set;
       - no check of a variable set twice.
   - Full suites: node 1201/1201, 2.1 1976/1976, station 602/602, each with SourceRules rerun with the artifacts inside the repo.
6. **Certificates** (B.3 point 7, B.11). *Built on 2026-11-03: WWCP_Node 4e86012, on top of e05d41d, WWCP_OCPP 17777861, on top of 92578632, and station a2cf884, on top of 3e1de56 (pushed and pinned on 2026-11-03)*, with the decision and the refinements noted under B.11. Contents:
   - **WWCP_Node**: `ServerPins.RootGroup` and `MarkOf`, the group in `JudgeServer` and `TryAnchor`, and the group on the page (`pins.ts`, `pinViews.ts`).
   - **WWCP_OCPP**: `ICertificateSource`, `CertificateSources`, `InMemoryCertificates` and `CertificateHashes` in `WWCP_OCPP_Common`. The certificate handlers of `TestChargingStationNode` (2.1) and `TestChargePointNode` (1.6) go through them. `NameServerRootCertificate` is added, and `NetworkTimeRootCertificate` in `GetCertificateIdUse`.
   - **Station**: `OCPP/NTSRootCertificates.cs`, registered in `ConfigureNTS` for both nodes. `NTPClientController.RootCAs` is read and written as `rootGroup`.

   Tests:
   - WWCP_Node: 6 `TimeServerPinningTests` and 7 frontend tests.
   - WWCP_OCPP: 5 `CertificateSourcesTests` (2.1, over the wire) and 2 `CertificateTests` (1.6). The hashes are checked against BouncyCastle's reading of the certificate. Two older tests now expect OCPP's form of a serial, and `NotFound` where nothing is left.
   - Station: 7 `NTSRootCertificatesTests` through the station's nodes, and RootCAs moved to the variables the node answers.
   - 14 mutations, each red in its tests:
     - in the node: the machine's chain, roots without the mark, pinned roots outside the group, a page's save taking the group, what a mismatch comes to left beside a group alone, and the page's `keysOf` without the group;
     - in WWCP_OCPP: deleting without asking every source first, a serial with its leading zeros, the same root of two groups listed twice;
     - in the station: no mark, a shared root deleted outright, roots for every use not listed, the group not written, and the station's check of a group's name.
   - Full suites: node 1207/1207, frontend 667/667, 2.1 1981/1981, 1.6 639/639, station 609/609, SourceRules with the artifacts inside the repos.
7. **Monitoring and security events** (B.3 point 6, B.12). *Built on 2026-11-03: WWCP_OCPP 6acdfa63, on top of 17777861, and station 000073a, on top of a2cf884 (pushed and pinned on 2026-11-03)*, as noted under B.12. Contents:
   - **WWCP_OCPP**:
     - `VariableMonitors` and the five monitoring handlers of `TestChargingStationNode`, which used to accept everything and keep nothing. A NotifyMonitoringReport follows GetMonitoringReport.
     - `CheckMonitorsAsync` and `VariableChanged` on `AChargingStationNode`, and `ToVariableConfig(…, Monitored)`.
     - `OfflineQueue` in `WWCP_OCPP_Common`. `QueueSecurityEventAsync` and `QueueEventsAsync` on the 2.1 node, `QueueSecurityEventAsync` on the 1.6 charge point, retried at the nodes' maintenance.
     - The seven security events of STS 3.2.6 and SDNS 3.1.4, in both versions.
   - **Station**: `ChargingStation.NTSEvents.cs`, and the queues and monitors handed to the nodes.

   Tests:
   - WWCP_OCPP: 7 `VariableMonitoringTests` over the wire (one of them an event kept while the CSMS was away and sent after a restart, from the file), 3 `OfflineQueueTests`, and 2 `SecurityEventTests` for 1.6. `GetMonitoringReport_Test` expects `EmptyResultSet` for a component without monitors.
   - Station: 6 `NTSEventsTests`.
   - 17 mutations, each red in its tests:
     - in WWCP_OCPP: a delta told at every check, a number told at any move, a threshold never cleared, monitoring a variable that does not support it, no duplicates found, the level ignored, a security event let go before a monitor event, no age, a message taken that nobody took (2.1 and 1.6), and a techInfo not cut;
     - in the station: a change through OCPP told back, a version told that nothing dials, another group's limit told, refused certificates not told, monitors not asked after a change, and a station dialling nothing queueing all the same.
   - Full suites: 2.1 1991/1991, 1.6 641/641, station 615/615. The node is as in step 6.
8. **1.6** (B.3 point 9, B.13). *Built on 2026-11-03: WWCP_OCPP bce7a870, on top of 6acdfa63, and station dd5ebd2, on top of 000073a (pushed and pinned on 2026-11-03)*, as noted under B.13. Contents:
   - **WWCP_OCPP**:
     - `IConfigurationKeySource` for the 1.6 charge point (`AddConfigurationKeySource`). It computes its keys when asked, resolves a named key live, and answers a change, or says the key is not its own.
     - Sources are asked before the charge point's own keys.
     - A write-only key is listed without its value.
     - ChangeConfiguration is asynchronous.
   - **Station**: `OCPP/NTSDeviceModel.Keys16.cs`, registered at the charge point in `ConfigureNTS`. The certificates of 1.6 came with step 6, and its security events with step 7.

   Tests:
   - WWCP_OCPP: 2 `ConfigurationKeySourceTests` over the wire.
   - Station: 5 `NTSKeys16Tests`.
   - 13 mutations, each red in its tests:
     - in WWCP_OCPP: a named key not asked of the sources, the list without them, a write-only value shown, a change not handed on;
     - in the station: a short list not repeating its last value, a longer one taken, an empty entry not taking the one before it, a new server not taking the last one's settings, servers beyond the end kept, one interval set alone, every mode taken, a pin key too many, the legal keys for every group.
   - Full suites: 1.6 643/643, station 620/620. 2.1 and the node are as in step 7.
9. **Equivalence tests**: The same change through `PUT /nts/groups/{id}` and through SetVariables must yield **the same file** and **the same state** (and the same log, except for `Origin`). Plus round trips from the web page to GetVariables and from SetVariables to `GET /nts/groups/{id}`, and a compare-and-set conflict between the two paths. *Built on 2026-11-03: station 9647436, on top of dd5ebd2 (pushed and pinned on 2026-11-03)*. It holds tests only: 4 `NTSEquivalenceTests`.
   - The page's PUT goes to a started station that is signed in. SetVariables and the 1.6 keys go to twins built from the same configuration.
   - For a group's settings, all three leave the same `nts` section in the file and the same group in the node. The page and SetVariables leave the same log line, apart from the origin.
   - Servers sent whole by the page, with `pinsAsShown`, and the same changes made through SetVariables leave the same servers.
   - Each path reads at once what the other one wrote.
   - A CSMS's stale write after a page change is refused as `ValueChanged`.
   - What is not equal, by design:
     - A page rename keeps the certificate pins the dialog showed. An OCPP rename drops them unless the same change sets them (B.10).
     - The page has no compare-and-set for a list: what it sends is what it showed, and the node keeps a pin learned in between (`pinsAsShown`).
   - 4 mutations, each red in its test: a 1.6 key standing for another variable, a CSMS's change named as the page's, a root group written in another case, no compare-and-set.
   - Full suite: station 624/624.
10. **Papers**: revise them per A.6, B.5–B.9 and the decisions of B.14 (done on 2026-10-30, see A.6); then apply the pattern to `webPayments` (B.2).

### B.16 Side findings (outside NTS, but in the running station program)

Fixed in WWCP_OCPP 5afa19e0 (2026-10-29), each with a test, for the battery swap station as well:

- `AChargingStationNode` registered `SecurityCtrlr` with the hard-coded `Identity "CS001"` and `BasicAuthPassword "s3cur3!"`. The identity is now the node's id, and there is no password until one is set.
- GetVariables (`TestChargingStationNode`) did not check `WriteOnly` and returned `BasicAuthPassword` in plain text. A WriteOnly variable is now `Rejected`, with no value.
- `TestChargePointNode` (1.6) created test keys, among them `password = "12345678"` (WriteOnly). GetConfiguration returned WriteOnly values, and ChangeConfiguration created any unknown key as ReadWrite. The node now has only the keys its caller passes (`ConfigurationKeys`), GetConfiguration lists a WriteOnly key without its value, and an unknown key is `NotSupported`.
- InstallCertificate stored certificates even on `Rejected`, so a rejected one replaced the one installed before. Only an accepted certificate is stored now.

Still open: SetNetworkProfile is accepted and discarded.

Found in step 4 (2026-11-02), fixed on 2026-11-03 in WWCP_Core cf75d625 (with WWCP_OCPP 5dad3658; pushed and pinned on 2026-11-03):
- WWCP_Core's `WWCPWebSocketServer` and `WWCPWebSocketClient` (and the overlay ones) read every message with `JArray.Parse` and Newtonsoft's default `DateParseHandling.DateTime`. A string that looks like a time becomes a time.
- So a CSMS reads the variable value "2026-11-02T12:00:00.000Z" as "11/02/2026 12:00:00". This happens in GetVariables and NotifyReport, for `ClockCtrlr.DateTime`, `LastCheck`, `LastKeyExchange` and DNS `KnownSince`.
- Likewise a station receives a SetVariables value that looks like a time in that form. Any other free-text field that holds such a string is mangled the same way.
- The fix is `DateParseHandling.None` where the messages are parsed. `JSONMessages.ParseArray` does that and is otherwise `JArray.Parse`; all four places use it.
- The parsers of dates in the messages read strings as before. Styx's `ParseMandatory` casts the token to `DateTimeOffset`, which Newtonsoft does from a string with the invariant culture. A time with an offset keeps it now.
- Tests:
  - 3 `DateLikeStringsTests` in WWCP_Core, one of them over a WebSocket.
  - `ClockControllerTests` compares `ClockCtrlr.DateTime` over the wire again. That test is red with the old way of reading.
  - Full suites: core 90/90, overlay 28/28, 2.1 1991/1991, 1.6 643/643, station 624/624.
