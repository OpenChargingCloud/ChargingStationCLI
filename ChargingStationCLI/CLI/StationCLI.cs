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

using System.Reflection;

using org.GraphDefined.Vanaheimr.CLI;

#endregion

namespace cloud.charging.open.ChargingStation.CommandLine
{

    /// <summary>
    /// The command line of a running charging station.
    /// </summary>
    /// <remarks>
    /// Everything a command needs is reachable from here, which is why every
    /// command takes one of these: the station itself, and through it its
    /// configuration, its log and everything the JSON API can do. A command is
    /// a second way of asking for the same thing as the web interface - never
    /// an implementation of its own.
    ///
    /// Commands are not listed anywhere. The constructor asks Styx to walk this
    /// assembly for anything that implements ICLICommand and can be built from
    /// a StationCLI, so a new command is a new file and nothing else.
    /// </remarks>
    public class StationCLI : CLI
    {

        #region Data

        /// <summary>
        /// What stands in front of the command being typed.
        /// </summary>
        public const String Prompt = "ChargingStation> ";

        #endregion

        #region Properties

        /// <summary>
        /// The charging station these commands are about.
        /// </summary>
        public ChargingStation Station { get; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create the command line of the given charging station.
        /// </summary>
        /// <param name="Station">The running charging station.</param>
        /// <param name="AssembliesWithCLICommands">Further assemblies to search for commands. This one is searched either way.</param>
        public StationCLI(ChargingStation    Station,
                          params Assembly[]  AssembliesWithCLICommands)

            : base(AssembliesWithCLICommands)

        {

            this.Station = Station;

            RegisterCLIType(typeof(StationCLI));

        }

        #endregion


        #region (protected override) GetPrompt()

        /// <summary>
        /// What this program is, because a charging station usually runs on a
        /// bench next to a vehicle, and the two consoles should not have to be
        /// told apart by what scrolls past on them.
        /// </summary>
        /// <remarks>
        /// Not a name of its own: unlike the vehicle, a station has none to
        /// give. Its two OCPP identities are fixed test values and would say
        /// less than this does.
        /// </remarks>
        protected override String GetPrompt()

            => Prompt;

        #endregion

    }

}
