# OCPP over CBOR - Concept and Work Plan

Status: 2026-11-12. The decisions of chapter 5 are taken. Built are steps 1 to 4 (WWCP_OCPP
bd3b9077), step 5 (e3cc32b2), step 6 (03ed10e9), step 7 (WWCP_Core 011c651d, WWCP_OCPP
f88c44d8), step 8 (WWCP_OCPP c64f7b8d) and step 9 (ChargingStation 9f8c4d2). Step 10 is
open.

## 1. Goal

OCPP messages are sent as CBOR in binary WebSocket frames instead of JSON in text frames.
Whether a connection speaks CBOR is agreed on by the WebSocket subprotocol: `ocpp2.1cbor`
next to `ocpp2.1`. A client connection is configured as `json`, `cbor` or `auto`.

## 2. What is there today

Counted by grep on WWCP_OCPP as pinned in the superproject; the totals include the base
classes `ARequest` and `AResponse`.

| Area                                              | State                                              |
|---------------------------------------------------|----------------------------------------------------|
| v2.1 requests                                     | 118 of 139 have `ToCBOR` / `TryParseCBOR`          |
| v2.1 responses                                    | 121 of 135                                         |
| v2.1 data structures                              | 113 of 117                                         |
| v1.6 requests / responses                         | 51 of 63 / 53 of 64                                |
| v2.1 adapter (Outgoing / Incoming / Forwarding)   | 0 of 116 / 116 / 107 files know CBOR               |
| Envelope (CALL, CALLRESULT, CALLERROR, SEND)      | JSON array and the binary format only, no CBOR     |
| Transport `IOCPPMessageTransport`                 | `SendJSON...` and `SendBinary...` only             |

### 2.1 Messages without CBOR (v2.1)

- SSH: `SignSSHKey`, `InstallSSHKey`, `DeleteSSHKey`, `GetInstalledSSHKeys`, `SSHKeySigned`
  (request and response each)
- Certificates and device model: `GetCRL`, `FlushPeriodicEventStream` (request and response each)
- Extensions: `NotifyNetworkTopology` (both), `NotifyWebPaymentStarted` (both),
  `NotifyWebPaymentFailed` (request), `NTSKE` (request), `AddSignaturePolicy`,
  `DeleteSignaturePolicy`, `AddUserRole` (requests)
- Data structures: `ComponentConfig`, `VariableConfig`, `InstalledSSHKey`, `SecurityEventType`

In v1.6 the signature policy and user role messages, `NotifyNetworkTopology` and the web
payment notifications are missing.

### 2.2 Three findings that shape the plan

1. **Binary frames are taken.** On `ocpp2.1` a binary frame is the format of the
   BinaryDataStreams extension (`OCPP_BinaryRequestMessage` and its siblings; the first byte
   is the message type).
2. **CBOR is not transcoded JSON.** A measurement is a metrological value there (tag 44252).
   Between the two forms only the typed class translates, never a blind conversion.
3. **Signatures are computed over canonical JSON** (JCS, `SignaturePolicy.cs`). A CBOR
   message has no defined signature yet.

### 2.3 What already fits

- Hermod keeps the negotiated subprotocol per connection (`Subprotocol` on the client and
  the server connection), and the server has a `SubprotocolSelector`.
- Styx shows CBOR as JSON for logs (`CBORJSON`), with documented losses: byte strings,
  non-text keys, binary floats, tags 0, 1 and 37.
- The OCPP nodes take their list of subprotocols as a parameter; the SSH transport carries
  a subprotocol in its hello.

## 3. Work plan

1. **Close the serializer gaps** (WWCP_OCPP). The messages and data structures of 2.1, v1.6
   alike. A test that, like `MessagesFromTheSchemaTests`, sends every message once through
   CBOR and back, so that no new message arrives without CBOR.
2. **CBOR envelope** (WWCP_OCPP_Common). `OCPP_CBORRequestMessage`, `...ResponseMessage`,
   `...RequestErrorMessage`, `...ResponseErrorMessage`, `...SendMessage`: a CBOR array with
   the fields of the JSON array, including the overlay variant with destination and network
   path.
3. **Transport.** `IOCPPMessageTransport` gets `SendCBOR...`, implemented by the WebSocket
   client, the WebSocket server and both SSH transports. On reception the subprotocol of the
   connection decides how a binary frame is read.
4. **Negotiation.** `ocpp2.1cbor` as a constant next to `ocpp2.1`. A server offers both; a
   client what its configuration says. With `auto` it offers `ocpp2.1cbor, ocpp2.1` and so
   falls back to JSON by itself.
5. **Adapter, made generic** (the largest part). Not 340 files one by one: the sending and
   the receiving path are changed centrally, so that the connection decides the format and
   a message only delivers `ToJSON` or `ToCBOR`. The `Receive_XXX` methods found by
   reflection take a `JObject` today; they need a second table or a signature that does not
   name the format.
6. **Forwarding** (local controller, gateway). Between a CBOR and a JSON connection a
   message is translated through its class. For unknown messages and free `DataTransfer`
   content only `CBORJSON` remains, with its losses.
7. **Signatures.** Depends on decision 3; touches the SignedMessages paper.
8. **Logs, events, HTTP API, offline queue.** A CBOR message is shown and logged as JSON.
   The queue stores a message without its format, since the connection that replays it may
   be another one.
9. **Station.** A field `encoding` (`json`, `cbor`, `auto`) in `ConnectionEntry` and
   `peers.json`, `SubProtocolsOf` in `ChargingStation.Connections.cs` follows it, the
   negotiated format on the connections page, a CLI switch, the README.
10. **End-to-end tests.** Station against CSMS over `ocpp2.1cbor`, the fallback to JSON,
    mixed forwarding, SSH, signed messages; then the station's suite.

Step 5 was planned as the largest part and turned out small: the adapter was not made
generic, it was left alone and given a translating edge (3.2).

### 3.1 What steps 1 to 4 built (WWCP_OCPP, OCPP 2.1)

- **Step 1.** The SSH messages, the signature policy and user role requests, the web payment
  notifications, `ComponentConfig`, `VariableConfig`, `InstalledSSHKey` and
  `SecurityEventType` have their CBOR; `SignaturePolicy` and `UserRole` of WWCP_Core get
  theirs as extensions in `DataStructures/CBORExtensions.cs`. `EveryMessageHasCBORTests`
  asks every request and response with a `ToJSON` for its `ToCBOR` and `TryParseCBOR`.
  `GetCRL`, `FlushPeriodicEventStream` and `NotifyNetworkTopology` are commented out as a
  whole and were left alone.
- **Step 2.** `WebSockets/CBORMessages`: `OCPP_CBORRequestMessage`, `...ResponseMessage`,
  `...RequestErrorMessage`, `...ResponseErrorMessage`, `...SendMessage`, and
  `OCPP_CBORFrame`. What stands in front of the payload is read and written by the JSON
  message of the same kind, so both frames have one definition. Error details stay the JSON
  object they are everywhere else and become a CBOR map on the wire.
- **Step 3.** `IOCPPMessageTransport.SendCBOR...`, in the WebSocket client and server and
  the SSH client and server. A CBOR message only goes out over a connection that agreed on
  a CBOR subprotocol. The adapter reads a binary frame of such a connection as CBOR
  (`ProcessCBORMessage`); until step 5 it answers a request with a `NotImplemented` request
  error in CBOR and drops everything else.
- **Step 4.** `Version.WebSocketSubProtocolIdCBOR`, `MessageEncodings` (`JSON`, `CBOR`,
  `Auto`) and `OCPPSubprotocols` (`IsCBOR`, `SpeaksCBOR`, `CBOROf`, `JSONOf`, `Offer`).
  Until step 5 a server took `ocpp2.1cbor` only where it was handed that subprotocol.

### 3.2 What step 5 built (WWCP_OCPP, OCPP 2.1)

The adapter still works on JSON, in all its 340 files. It translates at its edge:

- **Sending.** The five JSON sending methods of `OCPPAdapter` ask the transport whether it
  speaks CBOR to the next node (`IOCPPMessageTransport.SpeaksCBORTo`). Where it does, the
  payload is read by the class of the message and written as its CBOR; the envelope becomes
  the `OCPP_CBOR...Message` of the same kind.
- **Receiving.** `ProcessCBORMessage` reads the payload by the class of the message, writes
  its JSON, puts it into the JSON array the envelope stands for and hands that to
  `ProcessJSONMessage`: the same processors, events, refusals and forwarding as for JSON.
- **`OCPPMessagesAsCBOR`** is that translation: by reflection, the action of a message to
  the class that reads and writes both forms. A response is read and written together with
  its request, so the adapter keeps the requests that crossed a CBOR connection until they
  are answered or timed out - those it sent, by request id, and those it received, by
  connection and request id.
- **What cannot be translated is said.** A request that cannot be read is answered with a
  request error in CBOR (`FormationViolation`, or `NotImplemented` for an action without a
  CBOR form); a response that cannot be read ends the waiting request at once and is
  answered with a response error.
- **Signatures over JSON hold.** A signature of the JWS form crosses CBOR as its protected
  header, its header and its value, and is verified against the JSON the receiver writes
  from what it read. This is the JSON signature of decision 3; the signature over CBOR is
  step 7.
- **Never the wrong format.** A server sends neither JSON nor the binary format over a
  connection that agreed on CBOR, and no CBOR over one that did not.
- **A server takes `ocpp2.1cbor` by itself** now, over HTTP WebSockets and over SSH. A
  client still offers what it is handed, and JSON alone where it is handed nothing.
- **Tests.** `EveryActionCrossesCBORTests`: every action the adapter has a processor for
  is translated, and every message with a schema sample comes back from CBOR bytes as it
  went. `CS_Messages_Tests`, `CSMS_Messages_Tests` and both `..._SignedMessages_Tests` run
  a second time with charging stations that offer `ocpp2.1cbor` alone
  (`OverCBOR/MessagesOverCBORTests.cs`); the frame logs of the test bases show a CBOR frame
  as the JSON it stands for.

Not part of step 5:

- **The binary format** was refused over a CBOR connection; step 6 carries it (3.3).
- **Messages without an answer are carried blind.** `MessageTransfer` and
  `NotifyPeriodicEventStream` have no CBOR serializers; over CBOR they are what their JSON
  says, value by value (`CBORJSON`), and are read back the same way.
- **Custom serializers and parsers** see the JSON on both sides, but not the translation:
  a property only a custom serializer writes does not cross a CBOR connection.
- **OCPP over SSH with CBOR** goes through the same edge, but has no test yet (step 10).

### 3.3 What step 6 built (WWCP_OCPP, OCPP 2.1)

A node that forwards needed nothing of its own: what arrives over a CBOR connection is JSON
before the forwarding processors see it, and what they send on leaves the way its connection
speaks. Running the forwarding tests with a local controller between a CBOR and a JSON
connection found what was missing:

- **The binary format crosses CBOR as a byte string** (decision 1). `BinaryDataTransfer`,
  `SendFile`, `GetFile` and `SecureDataTransfer` are CBOR messages as every other, whose
  payload is a byte string: the bytes their class writes for the binary format, read by the
  same class. `OCPP_CBORFrame.TrySplit` takes a map or a byte string as the last item. An
  answer in JSON to a request that went this way is carried blind, an error is the CBOR
  error. Between a CBOR and a binary connection nothing is translated at all.
- **What nobody here has a class for is carried blind.** A request of an action without a
  class - a vendor's, or of a version a forwarding node does not know - is what its JSON
  says, value by value (`CBORJSON`), and so is its response. At a node the request is for,
  it is refused as every unknown action is (`ProtocolError`), no longer at the edge
  (`NotImplemented`). The losses are those of `CBORJSON`: a point in time is a text, a
  metrological value a number.
- **An answer finds its request.** A forwarded answer is sent without its connection, and two
  charging stations behind a local controller number their requests alike; the request an
  answer is written with is the one of that identification that came from the node the
  answer goes to.
- **The networking mode a message left in** is told to the JSON message the adapter was
  handed, as the JSON transports do: the events of a forwarding node showed the mode the
  message had arrived in.
- **Tests.** The overlay network of the tests (`ADefaultOverlayNetwork`) and the local
  controller in standard mode (`ForwardingUpInStandardModeTests`) take an encoding for each
  side of the local controller. `OverCBOR/ForwardingOverCBORTests.cs` runs their fixtures
  again with CBOR below the local controller, above it, and on both sides, and adds a
  request nobody knows on its way through the controller.

Not part of step 6:

- A message known at both ends and unknown in between cannot be built in one process; that
  its blind form is read by the class at the far end is not tested, and for a class whose
  CBOR differs from its JSON (points in time, metrological values) it will not be.
- A gateway between OCPP versions translates more than the format; nothing of that is here.
- The refusal a CSMS answers an unknown action with (`ProtocolError`) does not come back to
  a charging station behind the local controller of the overlay network: the request
  reaches the CSMS as it was, the charging station waits out its timeout. That is so over
  JSON on both sides as well, so it is no matter of CBOR, and the test of the request nobody
  knows asks for its arrival alone.

### 3.4 What step 7 built (WWCP_Core, WWCP_OCPP)

Decision 3 and the white paper "Signed OCPP Messages and Signer Roles" (section 4.2, "Three
encodings, one header") say what the signature over CBOR is: a `COSE_Sign1` with the
protected header of section 4.1. Styx brings COSE; what was built is its use.

- **The signature.** A `COSE_Sign1` (RFC 9052, tag 18) with a detached payload. Its
  protected header is the header a JWS signature has, field by field: `alg` is the COSE
  algorithm (label 1; ES256, ES384, ES512), `kid` the key identifier (label 4), `crit` the
  critical parameters (label 2), and every other field - `OCPPVersion`, `OCPPMessageTypeId`,
  `OCPPAction`, `OCPPMessageId`, `OCPPSource`, `OCPPDestination`, `iat`, `OCPPNonce`,
  `OCPPRequestHash`, `OCPPSignerName`, `OCPPSignerDescription` - keeps its name, as a
  text label. `kid` is the COSE Key Thumbprint of the signer's public key (RFC 9679,
  SHA-256). The mathematics are those of the JWS: ECDSA, `r‖s`, deterministic (RFC 6979).
- **What it signs.** The CBOR of the message without `signatures`, in the deterministic
  encoding of RFC 8949, section 4.2.1 - of the message *as its class reads it from its
  JSON*, not of the object a node holds. A point in time is whole milliseconds on the wire;
  a sender that signed the ticks it had in memory signed what no receiver ever saw
  (`OCPPMessagesAsCBOR.SignedPayloadOf`).
- **Where it stands.** Next to the other signatures of the message, in `signatures`: in
  CBOR as the `COSE_Sign1` it is, in JSON as `{ "cose": "<base64url of its bytes>" }`.
  It signs the same bytes on every node that has the class of the message, so it is
  verified behind a JSON connection as well as behind a CBOR one, and the adapter - which
  works on JSON - verifies it where it verifies every signature.
- **One set of rules.** `SignaturePolicy.CheckCOSE` checks the key and the mathematics;
  the header against the frame, the time and the replay cache are the code the JWS runs
  through (`CheckFrameAndTime`). A response is bound to its request by `OCPPRequestHash`
  over all signature values of the request, whatever their encoding.
- **Configuration.** `SignaturePolicy.Formats`: `JWS` (default), `COSE` or `Both` - what
  a node writes. It verifies every signature it can read. A policy learns what the CBOR of
  a message is from its node's adapter (`CBORPayloadOf`); without that it writes no COSE
  signature and refuses one it is shown.
- **Tests.** `COSESignaturesTests` (WWCP_Core): written and read, the thumbprint, another
  payload, key and header. `COSESignatureTests` (WWCP_OCPP): valid behind JSON and behind
  CBOR, both signatures next to each other, a changed message, another frame, a replay, the
  key of a rule, a node without the CBOR - and the sample of every request, signed and
  verified behind both. `OverCBOR/SignedWithCOSETests.cs` runs the fixtures of signed
  messages and of signature policies with COSE, over JSON and over CBOR connections.

Chosen here, and Achim's to confirm or change (they belong into the white paper):

1. **The signer's key travels as a COSE_Key** in the unprotected header, under the text
   label `OCPPSignerKey` - what `jwk` is to the JWS. COSE has no parameter of its own for a
   bare public key (only `x5chain` for certificates).
2. **A COSE signature crosses JSON** as its bytes. Decision 3 says that it ends where a
   gateway translates the message to JSON; here it ends only where a node has no class
   for the message - that node carries it along and cannot verify it.
3. **Points in time are whole milliseconds** in what is signed, since the JSON form is. A
   sender whose CBOR carries more must sign what the JSON form of its message says.
4. **The default is the JWS alone.** A node writes COSE only where it is set to.
5. **ES256, ES384, ES512**, as the JWS has them, are written; their fully-specified
   successors of RFC 9864 (ESP256, ESP384, ESP512) are read as well.

Not part of step 7:

- `Formats` is a property of the policy, not of a connection (the white paper has it per
  connection, section 6.3.1); the station's configuration is step 9.
- `COSE_Sign` with several signers in one structure, and countersignatures (RFC 9338) of
  a forwarding node: several signers are several `COSE_Sign1` next to each other.
- Binary messages keep format 0 (finding F17, Q19 of the white paper).
- OCPP 1.6: its messages have no CBOR, so a 1.6 node writes and verifies no COSE signature.

### 3.5 What step 8 built (WWCP_OCPP, OCPP 2.1)

Step 8 was planned before step 5 left the adapter on JSON. With the translation at the
adapter's edge, nearly all of it was there already; step 8 looked, added the one thing
missing and wrote the tests that hold it.

- **Events.** A message that crossed CBOR is told of as a JSON message: the adapter raises
  `OnJSON...MessageSent` and `OnJSON...MessageReceived` with the JSON it works on, and
  none of the events of the binary format. A message of the binary format that crossed
  CBOR as a byte string is told of as the binary message it is.
- **HTTP API.** The event log of the HTTP API (`OCPP_HTTPAPI`, server-sent events and the
  control WebSocket) is fed by the events of the messages, with the request and its JSON;
  it never sees a frame.
- **Offline queue.** `OfflineQueue` keeps the kind of a message and its payload as JSON,
  in memory and in its file; `EnqueuedRequest` keeps the request and its JSON. Neither
  says how the message will be sent: it goes through the adapter, over the connection that
  is there when it goes.
- **Frame logs** are what was missing. Who logs the frames of a connection sees the bytes
  of a CBOR message. `OCPP_CBORFrame.TryAsJSON` shows such a frame as the JSON array it
  stands for - its payload value by value (`CBORJSON`), the bytes of a message of the
  binary format as base64url. It is for showing; the helper of the test bases is this
  method now.
- **Tests** (`OverCBOR/LogsEventsAndQueuesOverCBORTests.cs`): a frame as JSON (a request,
  a message of the overlay network, a binary payload, no message at all); a request and its
  response told of as JSON at both ends and by no binary event; the queues per destination
  once more with both connections on CBOR; a queue written to its file under a held
  connection, read again and sent over CBOR. The encoding of a fixture moved up into
  `ACSMSTests`.

Not part of step 8:

- The WebSocket and SSH transports write no log of their own, and the station logs frames
  in its connection test alone ("Received n byte(s) of binary"); that it shows a CBOR
  frame through `TryAsJSON` is step 9.
- A frame shown this way is not the JSON the adapter works on where the two differ: a
  point in time is what CBOR has for it, not the text of the JSON form.

### 3.6 What step 9 built (ChargingStation)

- **`encoding`.** A connection says how its OCPP messages are written: `"json"`, which it
  is where it says nothing (decision 4), `"cbor"` or `"auto"`. It is a field of
  `ConnectionEntry`, written where it is not `"json"`, and so of an endpoint in `peers.json`;
  `ConnectionStore` takes it when a connection is added and keeps it where a change does not
  say. OCPP 1.6 has no CBOR: a 1.6 connection that names `"cbor"` or `"auto"` is refused, and
  one that becomes 1.6 says `"json"` again.
- **What is offered.** `SubProtocolsOf` follows the connection: `ocpp2.1, ocpp2.0.1` for
  JSON, `ocpp2.1cbor` alone for CBOR, `ocpp2.1cbor, ocpp2.1, ocpp2.0.1` for auto - in the
  WebSocket handshake and over `ssh://` (decision 6). The dialling passed nothing so far
  and left it to the node; it says it now.
- **CBOR and nothing else.** A back end may take a handshake without agreeing on any of the
  subprotocols it was offered (Hermod's WebSocket server does). A connection that says
  `"cbor"` and came to no CBOR subprotocol is let go of before anything is said to it, is
  `Refused` and not tried again - over a WebSocket and over SSH.
- **What it came to.** The log says which subprotocol a connection that is not `"json"`
  agreed on. `GET /api/v1/configuration/connections` says it under `subprotocolInUse`, and
  the connections page shows a chip "CBOR" and has the field ("Messages as").
- **Connection test.** It offers what the connection would, asks its Heartbeat as a CBOR
  frame where the handshake came to CBOR, reads the answer from the binary frame and shows
  every frame it receives as the JSON it stands for (`OCPP_CBORFrame.TryAsJSON`). Where a
  connection says `"cbor"` and the back end did not agree, the test fails and says so. At the
  command line `testconnection` tests a stored connection, with its encoding.
- **README**: the paragraph "JSON or CBOR".
- **Tests.** `DiallingOverCBORTests`: what is offered, agreed on and spoken for nothing,
  `json`, `cbor` and `auto`, and the fallback of `auto` to JSON; a back end without CBOR
  not kept; what is no encoding, and OCPP 1.6; a change that keeps what it does not say; the
  connection test in JSON and in CBOR. `connections.test.ts`: the field and the chip.

Not part of step 9:

- `subprotocolInUse` is said of WebSocket connections alone; an `ssh://` connection says
  what it agreed on in the log.
- No test dials over `ssh://` with CBOR, and none against a real CSMS node (step 10).
- The SSH server of the station (commissioning) offers `ocpp2.1, ocpp2.0.1` as before.

### 3.7 Found on the way, not changed

- `OCPP_JSONResponseErrorMessage` writes the overlay form (7 items) and reads the standard
  form (5 items) alone, so a response error in overlay mode is not read back - in JSON, and
  therefore in CBOR.
- `UserRole.ToJSON` writes `customData` only while its parser asks for `id`; an
  `AddUserRoleRequest` is not read back, and carries its role under the key
  `signaturePolicy`. CBOR mirrors both.
- `SignaturePolicy` rules and `UserRole` key pairs are read from JSON but never written;
  they are not in CBOR.
- `NotifyWebPaymentStartedResponse.ToJSON` leaves out the `statusInfo` its parser reads;
  its `ToCBOR` writes it.
- A point in time with fractions of a second is written by the shared timestamp helper as
  a float under tag 1.
- `Cost.TryParse` takes any `amountMultiplier` from JSON; the CBOR reader holds it to the
  -3 to 3 of OCPP. Four tests sent 23, which JSON carried and CBOR refused; they send 3 now.
- Two classes are called `NotifyWebPaymentStartedRequest`: the message, and the same
  message wrapped into a `DataTransfer`. The translation takes the message.

## 4. Repositories touched

| Repository       | Steps          |
|------------------|----------------|
| WWCP_OCPP        | 1 - 8, 10      |
| WWCP_Core        | 3, 7 (possibly)|
| ChargingStation  | 9, 10          |
| Whitepapers      | 7              |
| Hermod           | none expected  |

## 5. Decisions (Achim, 2026-11-12)

1. **Binary frames on `ocpp2.1cbor`: CBOR only.** `BinaryDataTransfer` becomes a CBOR
   message with a byte string; the binary format of the BinaryDataStreams extension is not
   spoken on this subprotocol.
2. **Scope: OCPP 2.1 first.** No `ocpp2.0.1cbor`; `ocpp1.6cbor` and the missing v1.6
   serializers come later.
3. **Signatures: a matter of configuration.** A message may carry both - a signature over
   its deterministic CBOR and one over the JCS form of its JSON representation. Which of
   them a node writes, and which it asks for, is configured. The CBOR signature ends when a
   gateway translates the message to JSON; the JSON signature survives it.
4. **Default of the station: `json`.**
5. **Keys: postponed.** Text keys as in JSON stay; integer keys are not part of this plan.
6. **OCPP over SSH: the same subprotocol** in the hello.
