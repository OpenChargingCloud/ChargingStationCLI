# To Do

Work that was decided to come later. Each entry says where its details are.

## NTS: the variables `NTPClientController` still leaves out

The OCPP 2.x component `NTPClientController[<id>]` (one per time server) does
not report these variables yet, so a CSMS reads them as unknown. The 1.6 keys
that stand for them are missing as well. Details are in
[NTS-DeviceModel-Concept.md](NTS-DeviceModel-Concept.md), tables B.9 and B.13,
and in the Secure Time Sync paper, §3.2.3, §3.2.4 and §3.3.

- **`NTSKEServerURL`**: a key exchange server separate from the NTP server.
  Today the node does the key exchange with the host named in `ServerURL`.
- **`DelayAsymmetry` / `DelayVarAsymmetry`**: a known asymmetry of the path
  to the server, and its variance.
- **`SignedResponses`**: asking for digitally signed NTS responses.
- **`Preflight`**: checking a server before it is used. It belongs to the
  group (`NTPClientGroupController`, concept B.8, paper §3.2.3).
