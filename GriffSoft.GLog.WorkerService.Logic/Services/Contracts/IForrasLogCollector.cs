using GriffSoft.Forras.DataAccess.Install.Entities.Log;
using GriffSoft.GLog.WorkerService.Logic.Forras.Types;

namespace GriffSoft.GLog.WorkerService.Logic.Contracts;

public interface IForrasLogCollector
{
    Task ResetLogsWithStuckState();
    Task<(List<GLogBrokerData> gLogBrokerDataList, FUVLogCheckoutData? fuvLogCheckoutData)> CheckoutLogDataAsync(CancellationToken cancellationToken);
    Task ProcessLogDataAsync(CancellationToken cancellationToken, List<GLogBrokerData> gLogBrokerDataList, FUVLogCheckoutData? fuvLogCheckoutData);
}