
using org.GraphDefined.Vanaheimr.Illias;
using cloud.charging.open.protocols.WWCP.NetworkingNode;
using OCPPv1_6 = cloud.charging.open.protocols.OCPPv1_6;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using cloud.charging.open.protocols.OCPPv1_6.CP;
using org.GraphDefined.Vanaheimr.Hermod.DNS;
using org.GraphDefined.Vanaheimr.Hermod;

namespace OCPP_ChargingStation
{
    public class Program
    {
        public static async Task Main(String[] Arguments)
        {

            var dnsClient  = new DNSClient();

            var cs01       = new OCPPv1_6.TestChargePointNode(
                                 ChargeBoxId:               NetworkingNode_Id.Parse("test01"),
                                 Connectors:                [
                                                                new OCPPv1_6.CP.ConnectorSpec(
                                                                    Availability:        OCPPv1_6.Availabilities.Operative,
                                                                    PhysicalReference:   "A",
                                                                    MaxPower:            Watt.    FromKW  (22),
                                                                    MaxEnergy:           WattHour.FromKWh(100),
                                                                    EnergyMeter:         null
                                                                )
                                                            ],
                                 Description:               null,
                                 ChargePointVendor:         null,
                                 ChargePointModel:          null,
                                 ChargePointSerialNumber:   null,
                                 ChargeBoxSerialNumber:     null,
                                 FirmwareVersion:           null,
                                 Iccid:                     null,
                                 IMSI:                      null,
                                 UplinkEnergyMeter:         null
                             );


            var ws01          = await cs01.ConnectOCPPWebSocketClient(
                                          RemoteURL:                   URL.Parse("wss://c.electriqua.com/abesp7/test01"),//ws://ocpp-public-demo.topazev.app:8081/AchimFriedland44"),
                                          RemoteCertificateValidator:  (sender, certificate, chain, client, policyErrors) => {
                                                                           return TLSValidationResult.Success();
                                                                       },
                                          DNSClient:                   dnsClient
                                      );
            var ws01response  = ws01.HTTPStatusCode;


            var sendBootNotification = true;

            while (true)
            {

                await Task.Delay(1000);

                if (sendBootNotification)
                {

                    var request01  = await cs01.SendBootNotification(
                                         "vendor1"
                                     );

                    DebugX.Log("BootNotification response: " + request01.Status);

                    sendBootNotification = false;

                }

            }


        }

    }

}
