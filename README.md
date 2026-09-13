# Charging Station

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

Then `dotnet build ChargingStationCLI.slnx` and `dotnet run --project
ChargingStationCLI`. The build needs the .NET 10 SDK and Node.js: the web
interface is built by npm and embedded into the assembly, so the station is
one thing to deploy.


### Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so. For this please read the Contributor License
Agreement carefully and send us a signed copy or use a similar free and
open license.
