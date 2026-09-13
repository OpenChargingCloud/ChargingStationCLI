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

using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.ChargingStation;
using cloud.charging.open.ChargingStation.Logging;
using cloud.charging.open.ChargingStation.Web;

#endregion

namespace OCPP_ChargingStation
{

    /// <summary>
    /// One charging station, with its web interface, until Ctrl+C.
    /// </summary>
    public class Program
    {

        public static async Task<Int32> Main(String[] Arguments)
        {

            Console.OutputEncoding = System.Text.Encoding.UTF8;

            #region Arguments

            IPPort?  port           = null;
            var      anyAddress     = false;
            String?  frontendDir    = null;
            String?  loginFilePath  = null;
            var      verbose        = false;
            var      quiet          = false;
            var      noTrace        = false;

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

                    case "--frontend":
                        if (!TryTakeValue(Arguments, ref i, out frontendDir))
                        {
                            Console.Error.WriteLine("Missing directory after --frontend!");
                            return 2;
                        }
                        break;

                    case "--web-login":
                        if (!TryTakeValue(Arguments, ref i, out loginFilePath))
                        {
                            Console.Error.WriteLine("Missing file after --web-login!");
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

            #region The station

            ChargingStation station;

            try
            {
                station = new ChargingStation(

                              HTTPHostname:     anyAddress
                                                    ? IPvXAddress.Any
                                                    : IPv4Address.Localhost,

                              HTTPPort:         port,

                              LoginFile:        new WebLoginFile(
                                                    loginFilePath ?? Path.Combine(RepositoryRoot(), WebLoginFile.DefaultFileName)
                                                ),

                              Frontend:         frontend,

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

                await station.Start();

                #region What somebody who just started this needs to know

                Console.WriteLine();
                Console.WriteLine($"  web interface  {station.WebInterfaceURL}");
                Console.WriteLine($"  JSON API       {station.WebInterfaceURL}api/v1/status");
                Console.WriteLine($"  event stream   {station.WebInterfaceURL}api/v1/events");
                Console.WriteLine($"  frontend from  {station.Frontend.Description}");
                Console.WriteLine($"  web login      user '{station.Sessions.Username}', {station.LoginFile.Path}");

                if (station.GeneratedPassword is not null)
                {
                    Console.WriteLine();
                    Console.WriteLine("  ┌─ First start: there was no web login, so one was made up for you ─────────");
                    Console.WriteLine($"  │  user      {station.Sessions.Username}");
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
            Console.WriteLine("                          [--web-login <file>] [--verbose | --quiet] [--no-trace]");
            Console.WriteLine();
            Console.WriteLine("Web interface:");
            Console.WriteLine($"  --port <number>   TCP port to listen on (default: {ChargingStation.DefaultHTTPPort})");
            Console.WriteLine("  --any             listen on all addresses instead of 127.0.0.1");
            Console.WriteLine("  --frontend <dir>  serve the web interface from a directory on disk instead of the");
            Console.WriteLine("                    bundle embedded in the assembly - use it together with");
            Console.WriteLine("                    'npm run watch' in ChargingStation/Frontend");
            Console.WriteLine();
            Console.WriteLine("Web login:");
            Console.WriteLine($"  --web-login <file>  where the web login lives (default: {WebLoginFile.DefaultFileName} below the");
            Console.WriteLine("                      repository root). Without it a password is made up at the");
            Console.WriteLine($"                      first start for the user '{WebLoginSettings.DefaultUsername}' and shown once.");
            Console.WriteLine();
            Console.WriteLine("Log:");
            Console.WriteLine("  -v, --verbose     write every entry to the console, down to the debug ones");
            Console.WriteLine("  -q, --quiet       write only warnings and worse");
            Console.WriteLine("      --no-trace    do not pick up what the libraries below write with DebugX");
            Console.WriteLine();
            Console.WriteLine("Whatever the console shows, the web interface shows the whole log under 'Logs'.");
        }

        #endregion

    }

}
