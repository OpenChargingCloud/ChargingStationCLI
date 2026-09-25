# Charging Station

[![CI](https://github.com/OpenChargingCloud/ChargingStationCLI/actions/workflows/ci.yml/badge.svg)](https://github.com/OpenChargingCloud/ChargingStationCLI/actions/workflows/ci.yml)
[![Nightly](https://github.com/OpenChargingCloud/ChargingStationCLI/actions/workflows/nightly.yml/badge.svg)](https://github.com/OpenChargingCloud/ChargingStationCLI/actions/workflows/nightly.yml)

This software implementes a EV Charging Station.


### Getting it

The libraries it is built from are submodules, so they have to come along:

```
git clone --recurse-submodules <this repository>
```

If you already cloned it without them:

```
git submodule update --init --recursive
```

They are fetched from GitHub over https, so nothing but git is needed - no
account, no key.

**On Windows**, turn long paths on first:

```
git config --global core.longpaths true
```

The deepest file in the submodules is 154 characters below the clone root, and
under the classic 260-character limit git creates no file whose whole path is
longer than 259, so the root itself may be at most 104 characters long.
`D:\src\ChargingStation` is fine; a checkout nested below
`C:\Users\<you>\AppData\Local\Temp\...` can run out of room, and then the
clone fails halfway through a submodule with `Filename too long` rather than
at the start. Per clone instead of globally: `git clone -c core.longpaths=true ...`.

Then `dotnet build ChargingStationCLI.slnx` and `dotnet run --project
ChargingStationCLI`. The build needs the .NET 10 SDK and Node.js: the web
interface is built by npm and embedded into the assembly, so the station is
one thing to deploy.


### Typing at it

Once it is up, the console is a prompt rather than a place that only scrolls:

```
ChargingStation> syncNTS
succeeded after 44 ms: 4 of 4 server(s) answered (2 required), offset +703.0 ms, spread 2.5 ms
  ptbtime1.ptb.de  +702.9 ms, round trip 24.5 ms, key exchange reused
  ptbtime2.ptb.de  +703.0 ms, round trip 23.6 ms, key exchange reused
  ptbtime3.ptb.de  +701.0 ms, round trip 22.8 ms, key exchange reused
  ptbtime4.ptb.de  +703.5 ms, round trip 29.7 ms, key exchange reused
```

`help` lists what can be typed, `quit` leaves, **Tab** completes and **↑**
walks back through what was typed before. `syncNTS` is the first command, and
it is **Sync now** on the **NTS** page, typed: the same group of time servers
is asked, the same entries go into the log, and the same result is left behind
for the page to show. The one entry that differs says who asked — the page
names the account that pressed the button, the prompt says it was somebody at
the command line. Neither of them steps the clock.

With one of the station's time servers after it, it is that server's **Test**
button instead: one server, on the ports it is configured with, and every step
of the key exchange and the time request with when it happened. Only a server
of this station is tested; anything else is answered with the ones there are,
and nothing is asked. `syncNTS` then takes a time server, and **Tab** offers
the station's own as soon as the command is typed, completing as far as their
names agree.

The key exchange is TLS, and the test says what its certificate claims and
whether that held up: the session, then every certificate of the chain as this
machine built it - the server's, the intermediates', the root's - each with
both ends of its validity and the days it has left, the root's SHA-256
fingerprint, and the verdict with its reasons. The root is there as much as the
server's certificate because a root can be pinned, and a pinned root that runs
out stops everything relying on it; which root the chain ends at depends on the
machine's trust store. A certificate that is refused is described just the
same, before the exchange is said to have failed.

```
ChargingStation> syncNTS ptbtime2.ptb.de
ptbtime2.ptb.de answered, 625 ms altogether:
    +2 ms  Asking ptbtime2.ptb.de: key exchange on port 4460, time on port 123, 10 second(s) allowed.
   +56 ms  'ptbtime2.ptb.de' resolves to 192.53.103.104, 2001:0638:0610:be01:0000:0000:0000:0104.
   +56 ms  Key exchange over TLS ...
  +551 ms  Connected to 2001:0638:0610:be01:0000:0000:0000:0104, of 2 address(es) that were offered.
  +552 ms  Where the time went: name 19 ms, TCP 25 ms, TLS 377 ms, key exchange 41 ms.
  +554 ms  TLS 1.3, TLS_AES_128_GCM_SHA256, ALPN ntske/1.
  +559 ms  Server certificate: CN=ptbtime2.ptb.de, for ptbtime2.ptb.de; RSA 3072-bit, sha256RSA; valid 2026-08-09 03:05:52 to 2026-11-07 03:05:51 UTC, 44 day(s) left.
  +559 ms  Intermediate CA: CN=YR1, O=Let's Encrypt, C=US; RSA 2048-bit, sha256RSA; valid 2025-09-03 00:00:00 to 2028-09-02 23:59:59 UTC, 709 day(s) left.
  +559 ms  Intermediate CA: CN=Root YR, O=ISRG, C=US; RSA 4096-bit, sha256RSA; valid 2026-05-13 00:00:00 to 2032-09-02 23:59:59 UTC, 2170 day(s) left.
  +560 ms  Root CA: CN=ISRG Root X1, O=Internet Security Research Group, C=US; RSA 4096-bit, sha256RSA; valid 2015-06-04 11:04:38 to 2035-06-04 11:04:38 UTC, 3175 day(s) left.
  +560 ms  The root's SHA-256 fingerprint: 96bcec06264976f37460779acf28c5a7cfe8a3c0aae11a8ffcee05c0bddf08c6.
  +560 ms  Validated: the chain ends at a root this machine trusts, nothing in it is revoked (asked online), and 'ptbtime2.ptb.de' is one of the server certificate's names.
  +561 ms  The key exchange succeeded: AES_SIV_CMAC_256, 8 cookie(s).
  +561 ms  It named no NTP server of its own, so the time is asked of this host.
  +561 ms  Authenticated NTP request ...
  +623 ms  Answered by [2001:638:610:be01::104]:123; 8 cookie(s) left, and a fresh one came back.
  +623 ms  Round trip 25.0 ms.
  +624 ms  This station's clock is +897.8 ms off what ptbtime2.ptb.de says.
  +624 ms  The clock was not stepped: that is a different thing, with meter readings and certificates hanging off it, and not something a test does by surprise.
```

The log keeps writing while you type, from whichever thread did the thing it is
reporting, and your half-typed line survives it: the line is taken off the
screen, the entry is written whole, and the line comes back with the cursor
where it was.

Where there is no terminal — from a script, under a service manager, in CI, or
with the output going into a file or through `| tee` — there is no prompt, and
the station runs until it is stopped, exactly as it did before.


### The log

Everything that happens is written three times over, because the three answer
different questions. The **console** shows what is going on to whoever is
watching, at the level `--verbose` and `--quiet` choose. The **Logs** page
keeps the last two thousand entries for whoever asks, and loses them when the
process ends. And `logs/` beside the solution keeps one file per day, every
entry down to the debug ones, for the afternoon somebody asks what happened
last night — `--log-file <dir>` puts it elsewhere, `--no-log-file` leaves it
out, and nothing in it is ever deleted.

The days are UTC days, as the timestamps in the files are. A file that cannot
be written is said once on the console rather than once per entry, every entry
after that is tried again, and the first one that makes it is preceded by a
line saying how many are missing.


### The wire below the charging cable

`--v2g` brings up the V2G endpoint, SDP and SLAC. On one machine that is
everything for the message exchange and nothing at all for the wire: SDP
discovery, link-local addressing and the interface the powerline modem sits on
only mean something between two machines sharing one Ethernet segment, and
SLAC needs `AF_PACKET` and therefore Linux.

[LinuxTestEnvironment.md](LinuxTestEnvironment.md) sets that up - KVM guests on
a Linux host, a bridge for management and a second, IPv6-only bridge standing
in for the charging cable. The vehicle on the other end of it is
[EVCLI](https://github.com/OpenChargingCloud/EVCLI), which carries the mirror
image of that document.


### Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so. For this please read the Contributor License
Agreement carefully and send us a signed copy or use a similar free and
open license.
