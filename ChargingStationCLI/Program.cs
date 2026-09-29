/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of ChargingStationCLI <https://github.com/OpenChargingCloud/ChargingStationCLI>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using System.Net;
using System.Security.Cryptography.X509Certificates;

using org.GraphDefined.Vanaheimr.Hermod;

using cloud.charging.open.ChargingStation.CommandLine;
using cloud.charging.open.ChargingStation.ISO15118;

using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.CommandLine;
using cloud.charging.open.protocols.WWCP.Node.Configuration;

#endregion

namespace cloud.charging.open.ChargingStation
{

    /// <summary>
    /// One charging station, with its web interface and a prompt, until 'quit',
    /// Ctrl+C or SIGTERM.
    /// </summary>
    /// <remarks>
    /// What every kind of node's program does is the node's: the switches and
    /// the words -h explains them with, why it could not be set up or could not
    /// start, what goes into the certificate store, the banner and the prompt.
    /// What is left here is the station's: its display and its local app
    /// server, the wire below the charging cable, what its configuration holds,
    /// and what its banner says of all of them and of its EVSEs.
    /// </remarks>
    public class Program
    {

        /// <summary>
        /// Where the password of the V2G certificate is read from, so that it
        /// does not stand in the process list for everybody to see.
        /// </summary>
        public const String V2GCertificatePasswordVariable = "CHARGINGSTATION_V2G_CERT_PASSWORD";


        #region (private static) Usage

        /// <summary>
        /// What -h shows: every node's switches, in a charging station's words,
        /// and the station's own.
        /// </summary>
        private static readonly NodeUsage Usage = new (

            Program:               "ChargingStationCLI",
            Kind:                  ChargingStation.ChargingStationKind,
            DefaultPort:           ChargingStation.DefaultHTTPPort,
            FrontendSources:       "libs/ChargingStation/ChargingStation/Frontend",

            ConfigurationSays:     "where the name servers, the time servers, the EVSEs, the power limits and the calibration " +
                                  $"certificates of this station live (default: {WWCPConfigFile.DefaultFileName} below the " +
                                   "repository root). Without the file the station has one 22 kW type 2 socket and the system " +
                                   "defaults; the Configuration pages of the web interface write it, and every change there " +
                                   "takes effect at once.",

            CertificateKinds:      ChargingStation.StoredCertificateKinds,

            Synopsis:              [ "[--kiosk-port <n>]", "[--no-kiosk]",
                                     "[--local-app-port <n>]", "[--local-app-any]", "[--no-local-app]",
                                     "[--v2g]", "[--v2g-interface <name>]", "[--v2g-port <n>]", "[--v2g-cert <file>]",
                                     "[--v2g-loopback]", "[--slac-udp <ip:port>]", "[--evse-id <id>]" ],

            AfterTheWebInterface: [

                "Display:",

                .. NodeUsage.Switch("--kiosk-port <n>",     "the TCP port of the display, the page for the screen on the front of the " +
                                                           $"station (default: {ChargingStation.DefaultKioskPort}). Its own server on its own " +
                                                            "port, so that it and the web interface can be bound to different addresses. " +
                                                            "There is no sign-in on it."),

                .. NodeUsage.Switch("--no-kiosk",           "do not listen for the display at all."),

                "",

                "Local app:",

                .. NodeUsage.Switch("--local-app-port <n>", "the TCP port of the local app server, where an app on a phone in the " +
                                                            "station's own network starts and stops a charge: POST /localStart, " +
                                                            "POST /localStop/<session>, and the WebSocket /localApp (default: " +
                                                           $"{ChargingStation.DefaultLocalAppPort}). Its own server on its own port. " +
                                                            "There is no sign-in on it."),

                .. NodeUsage.Switch("--local-app-any",      "listen for the app on all addresses instead of 127.0.0.1. On its own: " +
                                                            "--any does not reach it, and it does not move the web interface."),

                .. NodeUsage.Switch("--no-local-app",       "do not listen for an app at all."),

                ""

            ],

            BeforeTheLog: [

                "The wire below the charging cable (ISO 15118), off unless asked for:",

                .. NodeUsage.Wrap("Everything here except the certificate and the simulated SLAC medium can also be written in " +
                                  "the \"v2g\" section of the configuration file, and changed on the V2G page of the web interface " +
                                  "while the station runs. The file has the last word on whatever it mentions.",
                                  "  ",
                                  "  "),

                "",

                .. NodeUsage.Switch("--v2g",                "bring up the V2G endpoint, SDP and SLAC"),

                .. NodeUsage.Switch("--v2g-interface <name>",
                                                            "the interface the vehicle is on, i.e. the powerline modem; without one the " +
                                                            "candidate that carries no IPv4 address is taken. Nothing in ISO 15118 is IPv4, " +
                                                            "while the interface a machine is administered over practically always has " +
                                                            "one, so that is very probably the port with the vehicle behind it. The console " +
                                                            "says which it took and why"),

                .. NodeUsage.Switch("--v2g-port <n>",      $"the TCP port of the V2G endpoint (default: {V2GOptions.DefaultV2GPort}, which IANA " +
                                                            "registers for v2g-secc). ISO 15118 does not require it - the port travels in " +
                                                            "the SDP response, so a vehicle finds the endpoint wherever it is. Pass 0 to let " +
                                                            "the operating system pick a free one, which is what a machine running two " +
                                                            "stations wants. Whichever it is, that is what SDP advertises"),

                .. NodeUsage.Switch("--v2g-cert <file>",    "a PKCS#12 certificate for the V2G endpoint, so that it speaks TLS 1.3 as " +
                                                            "ISO 15118-20 requires; the password is read from the environment variable " +
                                                           $"{V2GCertificatePasswordVariable}. Without a certificate the endpoint speaks " +
                                                            "plain TCP and SDP says so, rather than sending vehicles into a handshake " +
                                                            "that cannot finish"),

                .. NodeUsage.Switch("--v2g-loopback",       "also answer SDP requests coming from this same machine, for a bench where " +
                                                            "the vehicle runs here too. Off in the field: a station has no business " +
                                                            "answering a simulator somebody left running on its own controller. Set it on " +
                                                            "the vehicle as well: which of the two sockets decides depends on the platform"),

                .. NodeUsage.Switch("--slac-udp <ip:port>", "run SLAC over a simulated medium instead of a powerline modem, e.g. " +
                                                            "127.0.0.1:0 for a bench. Without this, SLAC needs AF_PACKET and therefore " +
                                                            "Linux, and says so where it cannot"),

                .. NodeUsage.Switch("--evse-id <id>",      $"what SLAC hands a vehicle (default: {V2GOptions.DefaultEVSEId})"),

                ""

            ]

        );

        #endregion

        #region (private static) WhatToDoAbout(Problem)

        /// <summary>
        /// The way past the port of the display or of the local app server,
        /// in the words of whoever started this station; null for the web
        /// interface's, which is every node's.
        /// </summary>
        /// <remarks>
        /// Here and not in the station, because the switches are this
        /// program's vocabulary: the station knows which port it wanted and
        /// what the socket layer said, and nothing about how it was started.
        /// </remarks>
        private static String? WhatToDoAbout(PortUnavailableException Problem)

            => Problem.Whose == ChargingStation.DisplayPort

                   ? "Stop whatever has it, or give the display another port with --kiosk-port <number> - " +
                     "or leave the display off altogether with --no-kiosk."

                   : Problem.Whose == ChargingStation.AppPort

                         ? "Stop whatever has it, or give the local app server another port with --local-app-port <number> - " +
                           "or leave it off altogether with --no-local-app."

                         : null;

        #endregion


        #region (private static) BesideTheWebInterface(Station)

        /// <summary>
        /// The two other things this station listens for, beside the web
        /// interface: the display and the local app server, each said to be
        /// off where it is.
        /// </summary>
        private static IEnumerable<(String Label, String Value)> BesideTheWebInterface(ChargingStation Station)
        {

            yield return ("display",    Station.KioskURL is { } display
                                            ? $"{display}  (no sign-in)"
                                            : "switched off (--no-kiosk)");

            yield return ("local app",  Station.LocalAppURL is { } app
                                            ? $"{app}  (no sign-in; POST localStart, POST localStop/<session>, WebSocket localApp)"
                                            : "switched off (--no-local-app)");

        }

        #endregion

        #region (private static) OfTheStation(Station)

        /// <summary>
        /// What this station is: its EVSEs, what the grid allows it, and the
        /// calibration certificates it runs under, where it has any.
        /// </summary>
        private static IEnumerable<(String Label, String Value)> OfTheStation(ChargingStation Station)
        {

            yield return ("EVSEs",  $"{Station.EVSEs.Count}: {String.Join(", ", Station.EVSEs.Select(evse => evse.ToString()))}");

            yield return ("grid",   Station.UplinkPowerLimit_kW.HasValue
                                        ? $"up to {Station.UplinkPowerLimit_kW.Value} kW"
                                        : "no limit configured");

            if (Station.CalibrationCertificates.Count > 0)
                yield return ("calibration", String.Join(", ", Station.CalibrationCertificates.Select(certificate => certificate.Id)));

        }

        #endregion

        #region (private static) BelowTheCable(Station)

        /// <summary>
        /// What is on the wire below the charging cable.
        /// </summary>
        private static IEnumerable<(String Label, String Value)> BelowTheCable(ChargingStation Station)
        {

            // Said even when there is nothing to say, because "the V2G lines
            // are missing" and "V2G is off" look identical on a console and
            // only one of them is a thing somebody configured.
            if (Station.V2G is not { } link)
            {
                yield return ("V2G", Station.V2GOptions.Enabled
                                         ? "switched on, but nothing came up - see the log"
                                         : "switched off");
                yield break;
            }

            // Which interface, and what made it that one, on a line of its own
            // rather than in a parenthesis behind SDP: on a machine with two of
            // them this is the first thing somebody checks, and the reason is
            // the half that saves the afternoon.
            yield return ("V2G interface",  link.InterfaceChoice ?? "none");

            yield return ("V2G endpoint",   (link.V2GEndpoint?.ToString() ?? "not listening") +
                                            (link.V2GEndpoint is not null ? link.UsesTLS ? ", TLS 1.3" : ", plain TCP" : ""));

            yield return ("SDP",            link.SDPRunning  ? "answering" : "not running");
            yield return ("SLAC",           link.SLACRunning ? "listening" : "not running");

        }

        #endregion


        public static async Task<Int32> Main(String[] Arguments)
        {

            Console.OutputEncoding = System.Text.Encoding.UTF8;

            #region Arguments

            // Every node's switches first. What is left is the station's own,
            // as it was typed.
            var arguments = NodeArguments.Parse(Arguments);

            if (arguments.Refused(Usage) is Int32 refused)
                return refused;

            IPPort?  kioskPort      = null;
            var      noKiosk        = false;

            IPPort?  localAppPort   = null;
            var      localAppAny    = false;
            var      noLocalApp     = false;

            var      v2g            = false;
            var      v2gLoopback    = false;
            String?  v2gInterface   = null;
            // The station's default rather than zero, because this is passed
            // through unconditionally: leaving it at zero here would override
            // V2GOptions.DefaultV2GPort and no station started from this
            // command line would ever see it. "--v2g-port 0" still asks for
            // any free one, which is now a thing somebody says rather than
            // what they get by saying nothing.
            UInt16   v2gPort        = V2GOptions.DefaultV2GPort;
            String?  v2gCertFile    = null;
            String?  slacUDP        = null;
            var      evseId         = V2GOptions.DefaultEVSEId;

            var      rest           = arguments.Rest;

            for (var i = 0; i < rest.Count; i++)
            {
                switch (rest[i])
                {

                    case "--kiosk-port":
                        if (i + 1 < rest.Count && UInt16.TryParse(rest[i + 1], out var parsedKioskPort))
                        {
                            kioskPort = IPPort.Parse(parsedKioskPort);
                            i++;
                        }
                        else
                        {
                            Console.Error.WriteLine("Missing or invalid port number after --kiosk-port!");
                            return 2;
                        }
                        break;

                    case "--no-kiosk":
                        noKiosk = true;
                        break;

                    case "--local-app-port":
                        if (i + 1 < rest.Count && UInt16.TryParse(rest[i + 1], out var parsedLocalAppPort))
                        {
                            localAppPort = IPPort.Parse(parsedLocalAppPort);
                            i++;
                        }
                        else
                        {
                            Console.Error.WriteLine("Missing or invalid port number after --local-app-port!");
                            return 2;
                        }
                        break;

                    case "--local-app-any":
                        localAppAny = true;
                        break;

                    case "--no-local-app":
                        noLocalApp = true;
                        break;

                    case "--v2g":
                        v2g = true;
                        break;

                    case "--v2g-interface":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out v2gInterface))
                        {
                            Console.Error.WriteLine("Missing interface name after --v2g-interface!");
                            return 2;
                        }
                        v2g = true;
                        break;

                    case "--v2g-port":
                        if (i + 1 < rest.Count && UInt16.TryParse(rest[i + 1], out v2gPort))
                        {
                            i++;
                            v2g = true;
                        }
                        else
                        {
                            Console.Error.WriteLine("Missing or invalid port number after --v2g-port!");
                            return 2;
                        }
                        break;

                    case "--v2g-cert":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out v2gCertFile))
                        {
                            Console.Error.WriteLine("Missing PKCS#12 file after --v2g-cert!");
                            return 2;
                        }
                        v2g = true;
                        break;

                    case "--v2g-loopback":
                        v2gLoopback = true;
                        v2g         = true;
                        break;

                    case "--slac-udp":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out slacUDP))
                        {
                            Console.Error.WriteLine("Missing endpoint after --slac-udp!");
                            return 2;
                        }
                        v2g = true;
                        break;

                    case "--evse-id":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out var parsedEVSEId))
                        {
                            Console.Error.WriteLine("Missing identification after --evse-id!");
                            return 2;
                        }
                        evseId = parsedEVSEId;
                        v2g    = true;
                        break;

                    default:
                        return NodeArguments.Unknown(rest[i], Usage);

                }
            }

            var root = NodeProgram.RepositoryRoot("ChargingStationCLI.slnx");

            #endregion

            #region What the vehicle finds on the wire below the cable

            V2GOptions? v2gOptions = null;

            if (v2g)
            {

                X509Certificate2? v2gCertificate = null;

                if (v2gCertFile is not null)
                {
                    try
                    {
                        v2gCertificate = X509CertificateLoader.LoadPkcs12FromFile(
                                             v2gCertFile,
                                             Environment.GetEnvironmentVariable(V2GCertificatePasswordVariable)
                                         );
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine($"The V2G certificate '{v2gCertFile}' could not be read: {e.Message}");
                        Console.Error.WriteLine($"A password is taken from the environment variable {V2GCertificatePasswordVariable}.");
                        return 2;
                    }
                }

                IPEndPoint? slacEndpoint = null;

                if (slacUDP is not null && !IPEndPoint.TryParse(slacUDP, out slacEndpoint))
                {
                    Console.Error.WriteLine($"'{slacUDP}' is not an address and port, e.g. 127.0.0.1:9000 or 127.0.0.1:0!");
                    return 2;
                }

                v2gOptions = new V2GOptions {
                                 Enabled            = true,
                                 InterfaceName      = v2gInterface,
                                 V2GPort            = v2gPort,
                                 ServerCertificate  = v2gCertificate,
                                 MulticastLoopback  = v2gLoopback,
                                 EVSEId             = evseId,
                                 SlacTransport      = slacEndpoint is not null
                                                          ? SlacTransportKind.UDP
                                                          : SlacTransportKind.Auto,
                                 SlacUDPEndpoint    = slacEndpoint
                             };

            }

            #endregion

            #region The station

            ChargingStation station;

            try
            {
                station = new ChargingStation(

                              HTTPHostname:      arguments.HTTPHostname,
                              HTTPPort:          arguments.Port,

                              // The display is its own server on its own port,
                              // so that it and the administration can be bound
                              // to different addresses and firewalled apart -
                              // see KioskHTTPAPI. It follows --any, because a
                              // display on a screen is normally the one of the
                              // two that has to be reachable from elsewhere.
                              KioskPort:         kioskPort,
                              NoKiosk:           noKiosk,

                              // On here, and on the loopback address unless it
                              // is told otherwise: the station itself has none
                              // unless it is given a port. It does not follow
                              // --any, because it is the one server meant for a
                              // network anybody may join - see LocalAppHTTPAPI -
                              // and it should get there only when somebody says
                              // so, and without taking the administration along.
                              LocalAppPort:      noLocalApp
                                                     ? null
                                                     : localAppPort ?? ChargingStation.DefaultLocalAppPort,

                              LocalAppHostname:  localAppAny
                                                     ? IPvXAddress.Any
                                                     : IPv4Address.Localhost,

                              AccountsPath:      arguments.AccountsPathBelow(root),
                              ConfigFile:        new WWCPConfigFile(arguments.ConfigFilePathBelow(root)),
                              Frontend:          arguments.Frontend,
                              CertificatesPath:  arguments.CertificatesPath,
                              V2G:               v2gOptions,
                              ConsoleLogLevel:   arguments.ConsoleLogLevel,
                              LogPath:           arguments.LogPathBelow(root),
                              BridgeDebugLog:    !arguments.NoTrace

                          );
            }
            catch (Exception e)
            {
                return NodeProgram.CouldNotBeSetUp(ChargingStation.ChargingStationKind, e, arguments.Verbose);
            }

            await using (station)
            {

                if (station.ImportCertificates(arguments, out _) is Int32 notImported)
                    return notImported;

                if (arguments.ListCertificates)
                    station.ListCertificates();

                if (await station.Started(arguments.Verbose, WhatToDoAbout) is Int32 notStarted)
                    return notStarted;

                #region What somebody who just started this needs to know

                foreach (var line in station.Banner(BesideTheInterfaces:  BesideTheWebInterface(station),
                                                    OfTheKind:            OfTheStation(station),
                                                    AfterTheTimeServers:  BelowTheCable(station)))
                    Console.WriteLine(line);

                #endregion

                #region The command line, until 'quit', Ctrl+C or SIGTERM

                // The node's: a prompt where somebody can type, and waiting
                // where nobody can, with the log sharing the screen.
                await new StationCLI(station).RunUntilStopped();

                #endregion

            }

            #endregion

            return 0;

        }

    }

}
