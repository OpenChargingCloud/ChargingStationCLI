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

The deepest file in the submodules is 143 characters below the clone root, so
under the classic 260-character limit the root has about 115 characters to
live in. `D:\src\ChargingStation` is fine; a checkout somewhere below
`C:\Users\<you>\AppData\Local\Temp\...` is not, and the clone fails halfway
through a submodule with `Filename too long` rather than at the start.
Per clone instead of globally: `git clone -c core.longpaths=true ...`.

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
