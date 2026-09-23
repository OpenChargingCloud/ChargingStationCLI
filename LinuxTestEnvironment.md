# Linux Test Environment

This documentation describes how you can set up a *Linux Test Environment* for testing this virtual charging station against e.g. a virtual electric vehicle.

It is the counterpart of the same document in
[EVCLI](https://github.com/OpenChargingCloud/EVCLI/blob/master/LinuxTestEnvironment.md),
and the two are meant to run **on one host**: the same two Linux bridges, one
virtual machine each, and the simulated ISO 15118 charging cable between them.
Everything here that is about the host - the bridges and the two helper
scripts - is the same in both documents; what differs is the virtual machine
and what runs inside it. Set up whichever of the two you like first.

Why a Linux virtual machine at all: SLAC needs `AF_PACKET`, which is Linux
only. A station started on Windows or macOS answers SDP and serves its web
interface perfectly well, says so in the log, and cannot listen for a
powerline modem.

## Creating Linux virtual block devices

```
mkdir -p /home/KVMGuests/cs
qemu-img create -f qcow2 /home/KVMGuests/cs/cs.qcow2 32G
qemu-img create -f raw   /home/KVMGuests/cs/cs.swap   8G
```


## Linux KVM script

The following linux script will set up a Linux KVM virtual machine with two network interfaces.    
The install image will be **Debian GNU/Linux** booted from a minimal CD image. When you install Debian it is needed to set the `bootindex` correctly (1 vs. 2 qcow2 image vs. 2 vs. 1 for the virtual CD drive). It is also
recommended to disable the second network interface within the script while installing.

You can access the VM via `telnet 127.0.0.1 4120`, or via `vncviewer 127.0.0.1::6020`.    
During Debian GNU/Linux installation is is recommended to use *vnc*.

The VNC display, the serial port and both MAC addresses differ from the ones
in the vehicle's document, because the two machines run on the same host and
would otherwise collide. Everything else is the same script.

```
#!/bin/bash
set -euo pipefail

NAME=cs
BASE=/home/KVMGuests/${NAME}

# pro VM eindeutig halten
VNC_DISPLAY=120          # 127.0.0.1:5900+DISPLAY
SERIAL_PORT=4120
MAC0=00:23:05:42:01:20
MAC1=00:23:05:42:01:21

qemu-system-x86_64 \
  -enable-kvm \
  -machine q35,accel=kvm \
  -cpu host \
  -name "${NAME}",process="${NAME}" \
  -pidfile "${BASE}/pid" \
  -m 4096 \
  -smp 4 \
  -k de \
  \
  -display none \
  -vnc "127.0.0.1:${VNC_DISPLAY}" \
  \
  -serial "telnet:127.0.0.1:${SERIAL_PORT},server,nowait,nodelay" \
  \
  -drive if=none,id=disk0,file="${BASE}/${NAME}.qcow2",format=qcow2 \
  -device virtio-blk-pci,drive=disk0,bootindex=1 \
  \
  -drive if=none,id=swap0,file="${BASE}/${NAME}.swap",format=raw \
  -device virtio-blk-pci,drive=swap0 \
  \
  -drive if=none,id=cd0,media=cdrom,readonly=on,file=/home/KVMGuests/debian-13.7.0-amd64-netinst.iso \
  -device ide-cd,bus=ide.0,drive=cd0,bootindex=2 \
  \
  -netdev tap,id=net0,ifname="tap1${NAME}",vhost=on,script=/home/KVMGuests/addInterfaceToBridge.sh,downscript=/home/KVMGuests/removeInterfaceFromBridge.sh \
  -device virtio-net-pci,netdev=net0,mac="${MAC0}" \
  \
  -netdev tap,id=net1,ifname="tap2${NAME}",vhost=on,script=/home/KVMGuests/addInterfaceToBridge.sh,downscript=/home/KVMGuests/removeInterfaceFromBridge.sh \
  -device virtio-net-pci,netdev=net1,mac="${MAC1}" \
  \
  -boot menu=on \
  -daemonize
```


## Installing Debian GNU/Linux

1. Installing Debian GNU/Linux 13.7.0: https://www.debian.org/CD/netinst/ for AMD64.
2. Boot the virtual machine
3. Deselect everything except `Standard Tools`, select `SSH server`
4. `vncviewer 127.0.0.1::6020`
5. `apt install joe mc sudo net-tools git tcpdump screen`
6. `joe /etc/sudoers` add: `ahzf    ALL=(ALL:ALL) NOPASSWD: ALL`, or which user you prefer :)
7. https://learn.microsoft.com/en-us/dotnet/core/install/linux-debian?tabs=dotnet10
8. `apt install -y curl ca-certificates unzip gnupg`
9. `curl -fsSL https://deb.nodesource.com/setup_24.x | sudo -E bash -`
10. `sudo apt install -y nodejs`

Node.js is needed for more than the station's own web interface. Seven
projects in the OCPP and WWCP_Core repositories compile TypeScript into their
HTTPRoot as part of the build, and the first build fetches the compiler for
them with `npm ci` - so the first build needs the network, and every one
after it does not.

Nothing has to be installed globally for that. It used to: those projects ran
a bare `tsc`, and a machine that had followed every step above still stopped
with

```
error MSB3073: The command "tsc" exited with code 127.
```

which is the shell saying "no such command" rather than anything about the
code. The compiler is pinned in each repository's package.json now, so every
machine compiles with the same version - which also settles a quieter fault,
where whichever version a machine happened to have would rewrite .js files
that are checked in and leave the tree dirty after a build that changed
nothing.


## Linux Virtual Bridges

We use two Linux virtual Ethernet bridges, one for management traffic and another one for the simulated ISO 15118 charging cable. As ISO 15118 is **IPv6-only** we do not configure any IPv4 for it. We also do not add the host machine to this Ethernet network. So for ISO 15118 CCS (Combined Charging System) this network will always just have two hosts - the EV and the charging station (EVSE). In contrast to this for ISO 15118 MCS (MegaWatt Charging) there might be additional hosts within this network.

This is the host's configuration and it is shared with the vehicle's virtual
machine: set it up once, whichever of the two you build first.

```
# The primary management network interface
auto br1
iface br1 inet static
        address         10.3.0.1
        netmask         255.255.0.0
        broadcast       10.3.255.255

        pre-up          /sbin/brctl addbr br1
        pre-up          /sbin/brctl setfd br1 0
        post-down       /sbin/brctl delbr br1

        up              /bin/echo "1" > /proc/sys/net/ipv4/ip_forward
        up              /bin/echo "1" > /proc/sys/net/ipv6/conf/all/forwarding
        up              /sbin/iptables -t nat -A POSTROUTING -s 10.3.0.0/16  -j MASQUERADE


# The ISO 15118 network cable
auto br2
iface br2 inet6 manual

        pre-up          /sbin/brctl addbr br2
        pre-up          /sbin/brctl setfd br2 0
        post-down       /sbin/brctl delbr br2

        up              /bin/echo "0" > /sys/class/net/br2/bridge/multicast_snooping
        up              /bin/echo "1" > /proc/sys/net/ipv4/ip_forward
        up              /bin/echo "1" > /proc/sys/net/ipv6/conf/all/forwarding
        up              /bin/echo "1" > /proc/sys/net/ipv6/conf/br2/disable_ipv6
```

`multicast_snooping` is off on `br2` on purpose. SDP is a multicast to
`ff02::1` and a snooping bridge with no querier on the segment drops it, which
looks exactly like a station that is not answering.


Helper script: `/home/KVMGuests/addInterfaceToBridge.sh`
```
#!/bin/sh

bridge=br`echo $1 | awk '{split($1,a,"[A-Za-z_\\\-]+"); print a[2]}'`

echo ""
echo "add $1 to $bridge using $0"

/sbin/ifconfig $1 0.0.0.0 up
/sbin/brctl addif $bridge $1

exit 0
```

Helper script: `/home/KVMGuests/removeInterfaceFromBridge.sh`
```
#!/bin/sh

bridge=br`echo $1 | awk '{split($1,a,"[A-Za-z_\\\-]+"); print a[2]}'`

echo ""
echo "remove $1 from $bridge using $0"

/sbin/brctl delif $bridge $1
/sbin/ifconfig $1 0.0.0.0 down

exit 0
```


## Linux VM Network Settings

The networking settings of the virtual machine can be set via `/etc/network/interfaces`:

```
# The primary management network interface
allow-hotplug enp0s4
iface enp0s4 inet dhcp

# The ISO 15118 network cable
allow-hotplug enp0s5
iface enp0s5 inet manual
```

The second one carries no IPv4 address, and that is what lets the station find
it without being told: `--v2g` without `--v2g-interface` takes the candidate
that has no IPv4 address, and the console says which it took and why.

```
  V2G interface  enp0s5 (the only one of enp0s4, enp0s5 without an IPv4 address)
```

Name it yourself with `--v2g-interface enp0s5`, or in the `"v2g"` section of
the configuration file, and that line says `enp0s5` with nothing behind it.


## Building and starting the station

The ISO 15118 schemas are not in any of these repositories and are not fetched
by the build either - that is a licence you accept yourself, once:

```
bash libs/WWCP_ISO15118/tools/download-schemas.sh
```

Then:

```
./updateAndBuild.sh
./run.sh --any --v2g
```

`--any` is what makes the web interface reachable from the host rather than
only from inside the virtual machine; without it the station listens on
`127.0.0.1` and the VM is the only thing that can talk to it. The display
listens on its own port, so the two can be bound to different addresses.

The banner names the commit every assembly was built from, which is worth
pasting into a bug report:

```
  built from     ChargingStation     d160aed53da479ddf83468bca5228b565492d598
                 ChargingStationCLI  082ed822924e400406ad1c6b997b4698ca04ef07
                 ...
```

A hash with `-dirty` behind it is a build from a tree with uncommitted
changes, so it is not the commit it names.
