
using org.GraphDefined.Vanaheimr.Illias;
using cloud.charging.open.protocols.WWCP.NetworkingNode;
using OCPPv1_6 = cloud.charging.open.protocols.OCPPv1_6;
using OCPPv2_1 = cloud.charging.open.protocols.OCPPv2_1;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using cloud.charging.open.protocols.OCPPv1_6.CP;
using org.GraphDefined.Vanaheimr.Hermod.DNS;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Norn.NTS;

namespace OCPP_ChargingStation
{
    public class Program
    {
        public static async Task Main(String[] Arguments)
        {

            var dnsClient    = new DNSClient();

            var ntsClient    = new NTSClient(
                                   DomainName.Parse("ptbtime1.ptb.de"),
                                   Timeout:    TimeSpan.FromSeconds(10),
                                   DNSClient:  dnsClient
                               );

            var ntsKEResult  = await ntsClient.GetNTSKERecords();

            //var cs01         = new OCPPv1_6.TestChargePointNode(
            //                       ChargeBoxId:               NetworkingNode_Id.Parse("test01"),
            //                       Connectors:                [
            //                                                      new OCPPv1_6.CP.ConnectorSpec(
            //                                                          Availability:        OCPPv1_6.Availabilities.Operative,
            //                                                          PhysicalReference:   "A",
            //                                                          MaxPower:            Watt.    FromKW  (22),
            //                                                          MaxEnergy:           WattHour.FromKWh(100),
            //                                                          EnergyMeter:         null
            //                                                      )
            //                                                  ],
            //                       Description:               null,
            //                       ChargePointVendor:         null,
            //                       ChargePointModel:          null,
            //                       ChargePointSerialNumber:   null,
            //                       ChargeBoxSerialNumber:     null,
            //                       FirmwareVersion:           null,
            //                       Iccid:                     null,
            //                       IMSI:                      null,
            //                       UplinkEnergyMeter:         null
            //                   );

            //var ws01          = await cs01.ConnectOCPPWebSocketClient(
            //                              RemoteURL:                   URL.Parse("wss://c.electriqua.com/abesp7/test01"),//ws://ocpp-public-demo.topazev.app:8081/AchimFriedland44"),
            //                              RemoteCertificateValidator:  (sender, certificate, chain, client, policyErrors) => {
            //                                                               return TLSValidationResult.Success();
            //                                                           },
            //                              DNSClient:                   dnsClient
            //                          );
            //var ws01response  = ws01.HTTPStatusCode;


            var cs02         = new OCPPv2_1.CS.TestChargingStationNode(
                                   Id:                             NetworkingNode_Id.Parse("test02"),
                                   VendorName:                     "gef",
                                   Model:                          "cs1",
                                   Description:                    I18NString.Empty,
                                   SerialNumber:                   null,
                                   FirmwareVersion:                null,
                                   Modem:                          null,

                                   EVSEs:                          [
                                                                       new OCPPv2_1.CS.EVSESpec(
                                                                           AdminStatus:         OCPPv2_1.OperationalStatus.Operative,
                                                                           ConnectorTypes:      [ OCPPv2_1.ConnectorType.sType2 ],
                                                                           MeterType:           "",
                                                                           MeterSerialNumber:   "",
                                                                           MeterPublicKey:      ""
                                                                       )
                                                                   ],
                                   UplinkEnergyMeter:              null,

                                   DefaultRequestTimeout:          null,

                                   SignaturePolicy:                null,
                                   ForwardingSignaturePolicy:      null,

                                   HTTPAPI_Disabled:               true,
                                   HTTPAPI_Port:                   null,
                                   HTTPAPI_ServerName:             null,
                                   HTTPAPI_ServiceName:            null,
                                   HTTPAPI_RobotEMailAddress:      null,
                                   HTTPAPI_RobotGPGPassphrase:     null,
                                   HTTPAPI_EventLoggingDisabled:   true,

                                   WebAPI:                         null,
                                   WebAPI_Disabled:                true,
                                   WebAPI_Path:                    null,

                                   ControlWebSocketServer:         null,

                                   DisableSendHeartbeats:          true,
                                   SendHeartbeatsEvery:            null,

                                   DisableMaintenanceTasks:        true,
                                   MaintenanceEvery:               null,

                                   CustomData:                     null,
                                   DNSClient:                      dnsClient

                               );





            var sendBootNotification = true;

            while (true)
            {

                await Task.Delay(1000);

                if (sendBootNotification)
                {

                    //var request01  = await cs01.SendBootNotification(
                    //                     "vendor1"
                    //                 );

                    //DebugX.Log("BootNotification response: " + request01.Status);

                    sendBootNotification = false;

                }

            }


        }

    }

}
