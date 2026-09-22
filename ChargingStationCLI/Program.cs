/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of ChargingStation <https://github.com/OpenChargingCloud/ChargingStation>
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
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.ChargingStation;
using cloud.charging.open.ChargingStation.Configuration;
using cloud.charging.open.ChargingStation.ISO15118;
using cloud.charging.open.ChargingStation.Logging;
using cloud.charging.open.ChargingStation.Web;

#endregion

namespace cloud.charging.open.ChargingStation
{

    /// <summary>
    /// One charging station, with its web interface, until Ctrl+C.
    /// </summary>
    public class Program
    {


        #region (private static) TryTakeValue(Arguments, ref Index, out Value)

        private static Boolean TryTakeValue(String[]     Arguments,
                                            ref Int32    Index,
                                            out String?  Value)
        {

            if (Index + 1 < Arguments.Length && !Arguments[Index + 1].StartsWith("--"))
            {
                Value = Arguments[++Index];
                return true;
            }

            Value = null;
            return false;

        }

        #endregion

        #region (private static) RepositoryRoot()

        /// <summary>
        /// The directory holding ChargingStationCLI.slnx, looked up from the
        /// binary and from the current directory; the current directory when
        /// neither leads to it.
        /// </summary>
        /// <remarks>
        /// The web login file defaults to a place below it, so that it does not
        /// end up in bin/ - where the next "dotnet clean" would take the
        /// station's password with it.
        /// </remarks>
        private static String RepositoryRoot()
        {

            foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {

                var directory = new DirectoryInfo(start);

                while (directory is not null)
                {

                    if (File.Exists(Path.Combine(directory.FullName, "ChargingStationCLI.slnx")))
                        return directory.FullName;

                    directory = directory.Parent;

                }

            }

            return Environment.CurrentDirectory;

        }

        #endregion

        #region (private static) PrintUsage()

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: ChargingStationCLI [--port <number>] [--any] [--frontend <dist directory>]");
            Console.WriteLine("                          [--accounts <dir>] [--config <file>] [--verbose | --quiet]");
            Console.WriteLine("                          [--no-trace]");
            Console.WriteLine("                          [--v2g [--v2g-interface <name>] [--v2g-port <n>] [--v2g-cert <file>]");
            Console.WriteLine("                                 [--v2g-loopback] [--slac-udp <ip:port>] [--evse-id <id>]]");
            Console.WriteLine();
            Console.WriteLine("Web interface:");
            Console.WriteLine($"  --port <number>   TCP port to listen on (default: {ChargingStation.DefaultHTTPPort})");
            Console.WriteLine("  --any             listen on all addresses instead of 127.0.0.1");
            Console.WriteLine("  --frontend <dir>  serve the web interface from a directory on disk instead of the");
            Console.WriteLine("                    bundle embedded in the assembly - use it together with");
            Console.WriteLine("                    'npm run watch' in ChargingStation/Frontend");
            Console.WriteLine();
            Console.WriteLine("Display:");
            Console.WriteLine($"  --kiosk-port <n>  the TCP port of the display, the page for the screen on the front");
            Console.WriteLine($"                    of the station (default: {ChargingStation.DefaultKioskPort}). Its own server on its own");
            Console.WriteLine("                    port, so that it and the web interface can be bound to");
            Console.WriteLine("                    different addresses. There is no sign-in on it.");
            Console.WriteLine("  --no-kiosk        do not listen for the display at all.");
            Console.WriteLine();
            Console.WriteLine("Accounts:");
            Console.WriteLine($"  --accounts <dir>    where the accounts live (default: {ChargingStation.DefaultAccountsPath}/ below the");
            Console.WriteLine("                      repository root). Without it a password is made up at the");
            Console.WriteLine($"                      first start for the user '{ChargingStation.DefaultAdminUser}' and shown once.");
            Console.WriteLine();
            Console.WriteLine("Configuration:");
            Console.WriteLine($"  --config <file>   where the name servers, the time server, the EVSEs, the power");
            Console.WriteLine($"                    limits and the calibration certificates of this station live");
            Console.WriteLine($"                    (default: {StationConfigFile.DefaultFileName} below the repository");
            Console.WriteLine("                    root). Without the file the station has one 22 kW type 2 socket");
            Console.WriteLine("                    and the system defaults; the Configuration pages of the web");
            Console.WriteLine("                    interface write it, and every change there takes effect at once.");
            Console.WriteLine();
            Console.WriteLine("The wire below the charging cable (ISO 15118), off unless asked for:");
            Console.WriteLine("  Everything here except the certificate and the simulated SLAC medium can");
            Console.WriteLine("  also be written in the \"v2g\" section of the configuration file, and changed");
            Console.WriteLine("  on the V2G page of the web interface while the station runs. The file has");
            Console.WriteLine("  the last word on whatever it mentions.");
            Console.WriteLine();
            Console.WriteLine("  --v2g             bring up the V2G endpoint, SDP and SLAC");
            Console.WriteLine("  --v2g-interface <name>");
            Console.WriteLine("                    the interface the vehicle is on, i.e. the powerline modem;");
            Console.WriteLine("                    without one the first candidate with an IPv6 link-local");
            Console.WriteLine("                    address is taken, and the console says which");
            Console.WriteLine("  --v2g-port <n>    the TCP port of the V2G endpoint; without one the operating");
            Console.WriteLine("                    system picks a free one, which is what SDP then advertises");
            Console.WriteLine("  --v2g-cert <file> a PKCS#12 certificate for the V2G endpoint, so that it speaks");
            Console.WriteLine("                    TLS 1.3 as ISO 15118-20 requires; the password is read from");
            Console.WriteLine($"                    the environment variable {V2GCertificatePasswordVariable}.");
            Console.WriteLine("                    Without a certificate the endpoint speaks plain TCP and SDP");
            Console.WriteLine("                    says so, rather than sending vehicles into a handshake that");
            Console.WriteLine("                    cannot finish");
            Console.WriteLine("  --v2g-loopback    also answer SDP requests coming from this same machine, for a");
            Console.WriteLine("                    bench where the vehicle runs here too. Off in the field: a");
            Console.WriteLine("                    station has no business answering a simulator somebody left");
            Console.WriteLine("                    running on its own controller. Set it on the vehicle as well -");
            Console.WriteLine("                    which of the two sockets decides depends on the platform");
            Console.WriteLine("  --slac-udp <ip:port>");
            Console.WriteLine("                    run SLAC over a simulated medium instead of a powerline modem,");
            Console.WriteLine("                    e.g. 127.0.0.1:0 for a bench. Without this, SLAC needs");
            Console.WriteLine("                    AF_PACKET and therefore Linux, and says so where it cannot");
            Console.WriteLine($"  --evse-id <id>    what SLAC hands a vehicle (default: {V2GOptions.DefaultEVSEId})");
            Console.WriteLine();
            Console.WriteLine("Log:");
            Console.WriteLine("  -v, --verbose     write every entry to the console, down to the debug ones");
            Console.WriteLine("  -q, --quiet       write only warnings and worse");
            Console.WriteLine("      --no-trace    do not pick up what the libraries below write with DebugX");
            Console.WriteLine();
            Console.WriteLine("Whatever the console shows, the web interface shows the whole log under 'Logs'.");
        }

        #endregion


        /// <summary>
        /// Where the password of the V2G certificate is read from, so that it
        /// does not stand in the process list for everybody to see.
        /// </summary>
        public const String V2GCertificatePasswordVariable = "CHARGINGSTATION_V2G_CERT_PASSWORD";


        #region (private static) WhatToDoAbout(Problem)

        /// <summary>
        /// The way past a port that cannot be had, in the words of whoever
        /// started this station.
        /// </summary>
        /// <remarks>
        /// Here and not in the station, because the switches are this
        /// program's vocabulary: the station knows which port it wanted and
        /// what the socket layer said, and nothing about how it was started.
        /// </remarks>
        private static String WhatToDoAbout(PortUnavailableException Problem)

            => Problem.Whose == StationPort.Display

                   ? "Stop whatever has it, or give the display another port with --kiosk-port <number> - " +
                     "or leave the display off altogether with --no-kiosk."

                   : "Another copy of this station already running is the usual answer. Stop it, " +
                     "or give this one another port with --port <number>.";

        #endregion


        public static async Task<Int32> Main(String[] Arguments)
        {

            Console.OutputEncoding = System.Text.Encoding.UTF8;

            #region Arguments

            IPPort?  port           = null;
            var      anyAddress     = false;
            String?  frontendDir    = null;
            String?  accountsPath   = null;
            String?  configFilePath = null;
            var      verbose        = false;
            var      quiet          = false;
            var      noTrace        = false;

            IPPort?  kioskPort      = null;
            var      noKiosk        = false;

            var      v2g            = false;
            var      v2gLoopback    = false;
            String?  v2gInterface   = null;
            UInt16   v2gPort        = 0;
            String?  v2gCertFile    = null;
            String?  slacUDP        = null;
            var      evseId         = V2GOptions.DefaultEVSEId;

            for (var i = 0; i < Arguments.Length; i++)
            {
                switch (Arguments[i])
                {

                    case "--port":
                        if (i + 1 < Arguments.Length && UInt16.TryParse(Arguments[i + 1], out var parsedPort))
                        {
                            port = IPPort.Parse(parsedPort);
                            i++;
                        }
                        else
                        {
                            Console.Error.WriteLine("Missing or invalid port number after --port!");
                            return 2;
                        }
                        break;

                    case "--any":
                        anyAddress = true;
                        break;

                    case "--kiosk-port":
                        if (i + 1 < Arguments.Length && UInt16.TryParse(Arguments[i + 1], out var parsedKioskPort))
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

                    case "--frontend":
                        if (!TryTakeValue(Arguments, ref i, out frontendDir))
                        {
                            Console.Error.WriteLine("Missing directory after --frontend!");
                            return 2;
                        }
                        break;

                    case "--accounts":
                        if (!TryTakeValue(Arguments, ref i, out accountsPath))
                        {
                            Console.Error.WriteLine("Missing directory after --accounts!");
                            return 2;
                        }
                        break;

                    case "--config":
                        if (!TryTakeValue(Arguments, ref i, out configFilePath))
                        {
                            Console.Error.WriteLine("Missing file after --config!");
                            return 2;
                        }
                        break;

                    case "-v":
                    case "--verbose":
                        verbose = true;
                        break;

                    case "-q":
                    case "--quiet":
                        quiet = true;
                        break;

                    case "--no-trace":
                        noTrace = true;
                        break;

                    case "--v2g":
                        v2g = true;
                        break;

                    case "--v2g-interface":
                        if (!TryTakeValue(Arguments, ref i, out v2gInterface))
                        {
                            Console.Error.WriteLine("Missing interface name after --v2g-interface!");
                            return 2;
                        }
                        v2g = true;
                        break;

                    case "--v2g-port":
                        if (i + 1 < Arguments.Length && UInt16.TryParse(Arguments[i + 1], out v2gPort))
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
                        if (!TryTakeValue(Arguments, ref i, out v2gCertFile))
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
                        if (!TryTakeValue(Arguments, ref i, out slacUDP))
                        {
                            Console.Error.WriteLine("Missing endpoint after --slac-udp!");
                            return 2;
                        }
                        v2g = true;
                        break;

                    case "--evse-id":
                        if (!TryTakeValue(Arguments, ref i, out var parsedEVSEId))
                        {
                            Console.Error.WriteLine("Missing identification after --evse-id!");
                            return 2;
                        }
                        evseId = parsedEVSEId;
                        v2g    = true;
                        break;

                    case "-h":
                    case "--help":
                        PrintUsage();
                        return 0;

                    default:
                        Console.Error.WriteLine($"Unknown argument '{Arguments[i]}'!");
                        PrintUsage();
                        return 2;

                }
            }

            if (verbose && quiet)
            {
                Console.Error.WriteLine("--verbose and --quiet ask for opposite things!");
                return 2;
            }

            #endregion

            #region Where the web interface comes from

            // A directory given on the command line wins, so that
            // "npm run watch" beside a running station shows up in the browser
            // on a reload, without rebuilding the C# side.
            IStaticContentSource? frontend = null;

            if (frontendDir is not null)
            {

                if (!Directory.Exists(frontendDir))
                {
                    Console.Error.WriteLine($"The frontend directory '{frontendDir}' does not exist!");
                    return 2;
                }

                frontend = new FileSystemContentSource(frontendDir);

            }

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

                              HTTPHostname:     anyAddress
                                                    ? IPvXAddress.Any
                                                    : IPv4Address.Localhost,

                              HTTPPort:         port,

                              // The display is its own server on its own port,
                              // so that it and the administration can be bound
                              // to different addresses and firewalled apart -
                              // see KioskHTTPAPI. It follows --any, because a
                              // display on a screen is normally the one of the
                              // two that has to be reachable from elsewhere.
                              KioskPort:        kioskPort,
                              NoKiosk:          noKiosk,

                              AccountsPath:     accountsPath ?? Path.Combine(RepositoryRoot(), ChargingStation.DefaultAccountsPath),

                              ConfigFile:       new StationConfigFile(
                                                    configFilePath ?? Path.Combine(RepositoryRoot(), StationConfigFile.DefaultFileName)
                                                ),

                              Frontend:         frontend,

                              V2G:              v2gOptions,

                              ConsoleLogLevel:  verbose ? LogLevel.Debug
                                                    : quiet ? LogLevel.Warning
                                                    : LogLevel.Info,

                              BridgeDebugLog:   !noTrace

                          );
            }
            catch (Exception e)
            {

                Console.Error.WriteLine($"The charging station could not be set up: {e.Message}");

                // A station that does not come up at all is the one moment the
                // stack trace is worth more than a tidy console.
                if (verbose)
                    Console.Error.WriteLine(e);

                return 1;

            }

            await using (station)
            {

                try
                {
                    await station.Start();
                }
                catch (PortUnavailableException problem)
                {

                    // What somebody starting a second copy of this station used
                    // to get was thirteen frames of stack trace under the
                    // operating system's own words for a port in use - in
                    // German on a German Windows, under eleven lines of English
                    // log, with the port named nowhere.
                    Console.Error.WriteLine($"The charging station could not start: {problem.Message}.");
                    Console.Error.WriteLine(WhatToDoAbout(problem));

                    if (verbose)
                        Console.Error.WriteLine(problem);

                    return 1;

                }

                #region What somebody who just started this needs to know

                Console.WriteLine();
                Console.WriteLine($"  web interface  {station.WebInterfaceURL}");
                Console.WriteLine($"  display        {station.KioskURL?.ToString() ?? "switched off (--no-kiosk)"}{(station.KioskURL.HasValue ? "  (no sign-in)" : "")}");
                Console.WriteLine($"  JSON API       {station.WebInterfaceURL}api/v1/status");
                Console.WriteLine($"  event stream   {station.WebInterfaceURL}api/v1/events");
                Console.WriteLine($"  frontend from  {station.Frontend.Description}");

                var builtFrom = BuiltFrom.Repositories.ToArray();

                if (builtFrom.Length > 0)
                {

                    // One line each, and the whole hash. This is meant to be read
                    // out of a bug report and pasted into a checkout, and an
                    // abbreviation is a thing somebody then has to guess the rest
                    // of. The column is as wide as the longest name rather than a
                    // number picked today, so a repository joining later still
                    // lines up.
                    var width = builtFrom.Max(repository => repository.Repository!.Length);

                    for (var i = 0; i < builtFrom.Length; i++)
                        Console.WriteLine((i == 0 ? "  built from     " : "                 ") +
                                          builtFrom[i].Repository!.PadRight(width) +
                                          "  " +
                                          builtFrom[i].Commit);

                }

                Console.WriteLine($"  accounts       {station.ExtAPI.Users.Count()} user(s) in {station.AccountsPath}");
                Console.WriteLine($"  sign in at     {station.WebInterfaceURL}{ChargingStation.ExtAPIPath.ToString().Trim('/')}/login");
                Console.WriteLine($"  configuration  {station.ConfigFile.Path}");
                Console.WriteLine($"  EVSEs          {station.EVSEs.Count}: {String.Join(", ", station.EVSEs.Select(evse => evse.ToString()))}");
                Console.WriteLine($"  grid           {(station.UplinkPowerLimit_kW.HasValue ? $"up to {station.UplinkPowerLimit_kW.Value} kW" : "no limit configured")}");

                if (station.CalibrationCertificates.Count > 0)
                    Console.WriteLine($"  calibration    {String.Join(", ", station.CalibrationCertificates.Select(certificate => certificate.Id))}");
                Console.WriteLine($"  name servers   {(station.DNSEnabled ? String.Join(", ", station.DNSClient.DNSServers) : "switched off")}");
                Console.WriteLine($"  time server    {station.NTSClient.Hostname}{(station.NTSEnabled ? "" : " (switched off)")}");

                // Said even when there is nothing to say, because "the V2G
                // lines are missing" and "V2G is off" look identical on a
                // console and only one of them is a thing somebody configured.
                if (station.V2G is null)
                    Console.WriteLine($"  V2G            {(station.V2GOptions.Enabled ? "switched on, but nothing came up - see the log" : "switched off")}");

                if (station.V2G is { } link)
                {
                    Console.WriteLine($"  V2G endpoint   {link.V2GEndpoint?.ToString() ?? "not listening"}" +
                                      (link.V2GEndpoint is not null ? link.UsesTLS ? ", TLS 1.3" : ", plain TCP" : ""));
                    Console.WriteLine($"  SDP            {(link.SDPRunning  ? $"answering on '{link.Interface?.Name}'" : "not running")}");
                    Console.WriteLine($"  SLAC           {(link.SLACRunning ? "listening" : "not running")}");
                }

                if (station.GeneratedPassword is not null)
                {
                    Console.WriteLine();
                    Console.WriteLine("  ┌─ First start: there were no accounts, so one was made up for you ─────────");
                    Console.WriteLine($"  │  user      {ChargingStation.DefaultAdminUser}");
                    Console.WriteLine($"  │  password  {station.GeneratedPassword}");
                    Console.WriteLine("  │  It is shown here once and kept only as a hash. Write it down.");
                    Console.WriteLine("  └───────────────────────────────────────────────────────────────────────────");
                }

                Console.WriteLine();
                Console.WriteLine("Press Ctrl+C to stop.");
                Console.WriteLine();

                #endregion

                #region Wait for Ctrl+C

                var stopped = new TaskCompletionSource();

                Console.CancelKeyPress += (_, e) => {
                    e.Cancel = true;
                    stopped.TrySetResult();
                };

                await stopped.Task;

                #endregion

            }

            #endregion

            return 0;

        }

    }

}
