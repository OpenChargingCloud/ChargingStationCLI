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
