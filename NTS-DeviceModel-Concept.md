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
| `DelayAsymmetry`, `DelayVarAsymmetry` | correction values | **not implemented** |
| `Priority` | Byte | ✔ identical; the bands are asked one after another until one meets the quorum |
| `MinInterval`/`MaxInterval` | per client, random | per group, fixed (see A.2) |
| `Preflight` | a packet sent in advance | **not implemented** |
| `ErrorLogging` (per client) | security log | **per group only** |
| `NTSKEServerURL`/`Port` | optional | only `ntsKEPort` per server |
| `RootCAs` | the name of a root CA group | **no groups per server.** Instead there are node-wide TLS roots with usage `nts` and **pins per server**: `certificates[]` and `roots[]` as SHA-256 fingerprints (≤ 16 each), plus `onMismatch` (refuse/record/accept) and `trustOnFirstUse`. As decided on 2026-10-20, the pins cover root CAs per group. |
| `NoCertTimeCheckAfterReboot` | Boolean | **not implemented** |
| `MinRefresh`/`MaxRefresh` | seconds in v2.x, **days** in v1.6 (30/60) | a fixed **30 minutes** (`Norn MonitoringConfig.NTSKERefreshInterval`), plus a cookie pool that is renewed when exhausted |
| `AEADAlgorithm` | selectable | Norn offers both (`OfferedAEADAlgorithms`), but the node does **not make it configurable**; only the negotiated algorithm can be read |
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

Dependencies: WWCP_Node stays free of OCPP, because it serves every CLI (roaming hub, LC, …). WWCP_OCPP stays free of the node. The bridge lives **in the station program** (`libs/ChargingStation`), next to `ConfigureWebPayments`. If the Local Controller needs it later as well, it moves into a small adapter assembly.

```
 Web page / CLI / API ───┐                         ┌── CSMS: Get/SetVariables, GetReport,
                         ▼                         ▼   InstallCertificate, …
             WWCPNode.TryUpdateNTSConfiguration ◄── NTSDeviceModelBridge (ChargingStation)
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
10. **Remove the placeholders** `NTSCtrlr` and `NTSServerCtrlr` (`WWCP_OCPPv2.1_ChargingStation/Components/Logical/NTS/`). They are copies of `AuthCtrlr` without NTS variables; `NTPController`, `NTPClientGroupController` and `NTPClientController` replace them (B.14 point 2).

Because only the first subscriber is dispatched, these hooks must sit **inside** the handlers of `TestChargingStationNode`/`TestChargePointNode`. An additional handler in the station is not enough.

### B.4 What the node lacks

1. **Change events**: `OnNTSGroupChanged(NTSGroup, IReadOnlyList<String> Changes, String Origin)`, `OnNTSGroupAdded`, `OnNTSGroupRemoved`. Today there are only `OnNTSGroupLimited` and `EventLog.OnLogged`, and parsing log lines would be wrong. The natural trigger point is the end of `ApplyNTSConfiguration`, where `changed.Count > 0` is logged.
2. **Origin**: an optional parameter `Origin` (`web:<account>`, `cli:<account>`, `ocpp:<CSMS id>`) for `TryUpdateNTSConfiguration`, `TryAddNTSGroup` and `TryRemoveNTSGroupAsync`. The log line "NTS configuration changed: …" then names it. STS §3 requires logging *every* change, and as evidence one also needs *who* made it.
3. **Stable server id** (decided, B.14 point 4): an optional field `"id"` per server entry, unique across the whole node, with the syntax of group ids. It is the instance of `NTPClientController`. The web page must send `id` back with the server list, as it does today with `pinsAsShown`. Entries without `id` get the derived id `<group>-<n>`. The bridge fixes it in the file on the first OCPP write to the group, not at start.
4. **Several groups at once** for ACID: `TryUpdateNTSConfigurations(IEnumerable<(NTSGroup, JObject)>, Origin)`. All patches are applied to **one** document that was read once, checked once (`NTSSection.TryParse`), written once and then applied. `TryWriteNTSSection` already works with "one read, one write"; this only extends it to several groups.
5. **Pass AEAD through** (small): Norn has `NTSServerEndpoint.OfferedAEADAlgorithms`; the node needs the field `aead` per server and has to pass it on in `ToEndpoint()`.
6. **A reporting threshold for offsets** (legal only): `offsetReportingThresholdSeconds`, the target of `ClockCtrlr.TimeAdjustmentReportingThreshold` (B.6).

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
- **`[k] = host` at an existing position** replaces that server's host name, and its NTS-KE port when the value is `nts://host:port`. The entry keeps its id, priority, root pins, `onMismatch` and `trustOnFirstUse`, and loses its certificate pins, which name the old server's certificate. The diff line in the metrological log shows both.
- **`[n+1] = host`** adds a server with that host name, enabled, with the default ports, in the band of the last server (with its priority), so it counts towards the same quorum. An index beyond `n+1` ⇒ `Rejected`, with the next free index in `additionalInfo`.
- **`[k] = ""`** removes the k-th server, and the later ones move up one index. If the node refuses, e.g. because fewer servers would be enabled than an explicit `minServers`, its sentence goes into `additionalInfo`.
- **Values** are host names or `nts://host[:port]`, as for `NTPClientController.ServerURL`. `ntp://…` or any other scheme ⇒ `Rejected`, `InvalidValue`, because the node speaks NTS only.
- **Consistency**: Each write is one patch through `TryUpdateNTSConfiguration`, so a standard CSMS can use `IndependentRequests`. Under ACID, `NtpServerUri` entries go into the legal group's patch together with its `NTPClientController` entries; two entries of one request that change the same server ⇒ both `Rejected`, all others `Rolledback`.
- **What stays elsewhere**: priorities and further bands, the NTP port, pins, the quorum and any server beyond the ninth are changed through `NTPClientController` and `NTPClientGroupController`.

### B.7 Variables: `NTPController`

| Variable | Type | Mutability | Node | Note |
|---|---|---|---|---|
| `ClientGroups` | MemberList | rw | the group ids | A new id ⇒ `TryAddNTSGroup` (switched off, without servers). A missing id ⇒ `TryRemoveNTSGroupAsync`; if the group is still enabled ⇒ `Rejected` with "switch it off first". |
| `SuggestedGroups` | MemberList | ro | `NTSSection.SuggestedIds` minus the existing ones | like `suggested` in the API |
| `LegalTime` | boolean | ro | `ClockJSON().legal` | a monitoring target |
| `LegalTimeReason` | OptionList | ro | `ClockJSON().why` | `notClaimed`, `ntsOff`, `neverChecked`, `stale`, `offBy` |

### B.8 Variables: `NTPClientGroupController[<id>]`

| Variable | Type (characteristics) | Mutability | Node key | Note |
|---|---|---|---|---|
| `Enabled` | boolean | rw | `enabled` | OCPP standard variable |
| `Clients` | SequenceList | rw | `servers[].id` | A new id ⇒ a new entry, whose `ServerURL` must come in the same ACID request (B.10). A missing id ⇒ the entry is removed. The order does not matter; `Priority` decides. |
| `MinServers` | integer, min 1, max = enabled servers | rw | `minServers` | Actual = the value set (`Group.Quorum`) |
| `EffectiveMinServers` | integer | ro | `Sources.MinServers` | lower when fewer servers are enabled |
| `MaxDeviation` | decimal s, 0.001…3600 | rw | `maxDeviationSeconds` | the spread between servers |
| `ErrorLogging` | integer 1…65535 | rw | `errorLogging` | |
| `MaxErrors` | integer 1…65535 | rw | `maxErrors` | |
| `CheckInterval` *(new)* | integer s, 10…86400 | rw | `checkEverySeconds` | As long as the node does not spread randomly: `MinInterval` = `MaxInterval` = `CheckInterval`; unequal values ⇒ `Rejected` |
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
| `CertificatePins` *(new)* | SequenceList SHA-256 | rw | `certificates[]` | ≤ 16 |
| `RootPins` *(new)* | SequenceList SHA-256 | rw | `roots[]` | ≤ 16 |
| `OnPinMismatch` *(new)* | OptionList `Refuse,Record,Accept` | rw | `onMismatch` | |
| `TrustOnFirstUse` *(new)* | OptionList `None,Root,Certificate` | rw | `trustOnFirstUse` | |
| `AEADAlgorithm` | OptionList `AES-SIV-CMAC-256,AES-128-GCM-SIV` | ro, rw after B.4 point 5 | `aead` / negotiated | |
| `Cookies`, `LastKeyExchange`, `Certificate`, `Judgement` | various | ro | state per host name | like `timeSources[]` in the API |
| `MinRefresh`/`MaxRefresh` | integer s | ro (1800) | Norn `NTSKERefreshInterval` | rw only once the node makes it configurable |
| `DelayAsymmetry`, `DelayVarAsymmetry`, `Preflight`, `ErrorLogging`, `NoCertTimeCheckAfterReboot`, `SignedResponses` | n/a | n/a | not implemented | not in the report ⇒ `UnknownVariable`; report them only once the node supports them |

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

`servers` is always sent **whole**, because the node replaces lists whole: the current list with all changes from the request. One patch yields one log line per group, like saving on the page. If the node refuses, its sentence goes into `additionalInfo` (≤ 1024). `NotSaved` ⇒ `Rejected` with `InternalError`, the counterpart of the web API's 500/NotChanged. `RebootRequired` never occurs, because the node applies everything live. The admin role check in the SetVariables handler stays in front of all this.

### B.11 Certificates: `NetworkTimeRootCertificate` and `certificateGroup`

The station answers InstallCertificate, GetInstalledCertificateIds and DeleteCertificate for `NetworkTimeRootCertificate` through the store interface from B.3 point 7, backed by the node's `CertificateStore`.

- **InstallCertificate**(`NetworkTimeRootCertificate`, `certificateGroup = G`) ⇒ a TLS root in the node's store with usage `nts` and a group mark `ntsca-<g>`. Usages are `[a-z][a-z0-9_-]*` with ≤ 32 characters, so a `certificateGroup` that cannot be mapped ⇒ `Rejected`. The event goes to the metrological log (STS §3).
- **`NTPClientController.RootCAs = G`** ⇒ the bridge resolves `G` into the fingerprints of all roots with the mark `ntsca-<g>`. It writes them as `roots[]` pins, and `G` as `rootGroup`, into the server entry. This keeps the node's rule: pins **narrow** and never widen.
- **A new or deleted certificate in G** (through OCPP or the web page) ⇒ the bridge recomputes `roots[]` of all servers with `rootGroup = G`. This goes through `TryUpdateNTSConfiguration`, so it comes with a log line and with dropping the key exchange, which the node does on changed pins anyway.
- **`RootCAs` empty** ⇒ no root pins. The node's rule applies (the machine's roots plus its own `nts` roots), which corresponds to the paper's fallback chain.
- **GetInstalledCertificateIds**(`NetworkTimeRootCertificate`) lists the roots with usage `nts`, whether they were imported through OCPP or through the web page.

### B.12 Node → OCPP: changes from elsewhere

1. **Values**: Nothing to do here, because the getters read live.
2. **Structure**: `OnNTSGroupAdded/Removed` and new or missing server ids ⇒ register or remove components (B.3 point 4).
3. **Monitoring**: `OnNTSGroupChanged` ⇒ the bridge compares the projected values with the last reported state and calls `VariableChanged` for every changed variable, which yields `NotifyEvent` with `Delta` (B.3 point 6). This state is only a note for monitoring, not a second source.
4. **`OnNTSGroupLimited`** ⇒ `Limited` changes, and for `legal` also `NTPController.LegalTime`. For `legal`, a `SecurityEventNotification` is sent as well (vendor-specific, e.g. `LegalTimeLimited`/`LegalTimeRestored`).
5. **Local changes**: Changes to the legal group that did **not** come through OCPP (`Origin` web or cli) ⇒ `SecurityEventNotification` `ReconfigurationOfSecurityParameters` with the node's diff line as `techInfo` (cut to the field length, complete in the log). This way the operator learns of local changes (NIS2, CLTS §4.2).
6. **Offset ≥ `TimeAdjustmentReportingThreshold`** ⇒ `SettingSystemTime` (B.6).
7. **No echo**: The bridge's own changes carry `Origin = ocpp:…`, so point 5 does not apply to them. Monitors fire nevertheless, because the node normalizes (host names, a lowered quorum, a removed authority).
8. **Manual edits** of the file take effect only at the next start, as before. The projection is built from the node at start, so the two cannot drift apart.

### B.13 OCPP 1.6

The same pattern applies to the keys from STS §3.3, once WWCP_OCPP can register keys with a getter and setter (B.3 point 9). `GetConfiguration` computes the CSL values from the groups. `ChangeConfiguration` becomes a patch for `TryUpdateNTSConfiguration`, and the bridge takes over the paper's CSL rules (a list that is too short ⇒ the last value keeps applying, one that is too long ⇒ an error). Limits: ChangeConfiguration carries one key per request, so bundling is impossible. `ntp.legal.servers` and `ntp.legal.minServers` must therefore come in an admissible order, and `Rejected` carries no reason. In 1.6, pins and new groups stay reserved to the web page. InstallCertificate with `certificateGroup` exists in 1.6 as well (STS extends the 1.6 Security Extensions with it) and goes through the same store.

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
2. **WWCP_OCPP**: build the two NTP controllers from variables with a source interface (B.3 point 1), with characteristics as in tables B.8 and B.9.
3. **Node**: events, `Origin`, the server `id` (with the frontend), the multi-group update (B.4 points 1–4), kit tests.
4. **Bridge, reading**, in ChargingStation: sources against the node, `ClockCtrlr`, registration in `BuildOCPPNodes`.
5. **Bridge, writing**: IndependentRequests, then ACID/BASE (B.3 point 3), `NTPController.ClientGroups` and `ClockCtrlr.NtpServerUri` (B.6).
6. **Certificates** (B.3 point 7, B.11).
7. **Monitoring and security events** (B.3 point 6, B.12).
8. **1.6** (B.3 point 9, B.13).
9. **Equivalence tests**: The same change through `PUT /nts/groups/{id}` and through SetVariables must yield **the same file** and **the same state** (and the same log, except for `Origin`). Plus round trips from the web page to GetVariables and from SetVariables to `GET /nts/groups/{id}`, and a compare-and-set conflict between the two paths.
10. **Papers**: revise them per A.6 (points 8–25 are left), B.5–B.9 and the decisions of B.14; then apply the pattern to `webPayments` (B.2).

### B.16 Side findings (outside NTS, but in the running station program)

- `AChargingStationNode` registers `SecurityCtrlr` with the hard-coded `Identity "CS001"` and `BasicAuthPassword "s3cur3!"`.
- GetVariables (`TestChargingStationNode`) does not check `WriteOnly` and returns `BasicAuthPassword` in plain text.
- `TestChargePointNode` (1.6) creates test keys, among them `password = "12345678"` (WriteOnly). GetConfiguration also returns WriteOnly values, and ChangeConfiguration creates any unknown key as ReadWrite.
- InstallCertificate stores certificates even on `Rejected`. SetNetworkProfile is accepted and discarded.
