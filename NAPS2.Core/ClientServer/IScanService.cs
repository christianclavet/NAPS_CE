using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;
using NAPS2.Scan;

namespace NAPS2.ClientServer
{
    [ServiceContract(CallbackContract = typeof(IScanCallback))]
    public interface IScanService
    {
        [OperationContract]
        void Hello(string clientName, string clientVersion);

        [OperationContract]
        List<string> GetSupportedDriverNames();

        [OperationContract]
        List<ScanDevice> GetDeviceList();

        [OperationContract]
        ScanProfile GetServerProfile();

        [OperationContract]
        Task<int> Scan(ScanParams scanParams);

        [OperationContract(IsOneWay = true)]
        void CancelScan();
    }
}
