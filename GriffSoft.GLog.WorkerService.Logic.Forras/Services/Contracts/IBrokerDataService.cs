using GriffSoft.Forras.DataAccess.Install.Entities.Log;

namespace GriffSoft.GLog.WorkerService.Logic.Forras.Services.Contracts;

public interface IBrokerDataService
{
    /// <summary>
    /// Mark GLogBrokerData entries for later processing.
    /// </summary>
    /// <param name="maxLogDataCaptureInCycles">Max number of logs</param>
    /// <param name="backGroundServiceUniqueId">Service unique id</param>
    /// <returns>GLogBrokerData which were marked for later processing</returns>
    Task<List<GLogBrokerData>> CheckoutLogData(int maxLogDataCaptureInCycles, string backGroundServiceUniqueId);
    /// <summary>
    /// Reset the statuses of GLogBrokerData entries
    /// </summary>
    /// <param name="backGroundServiceUniqueId">Service unique id</param>
    Task ResetLogsWithStuckState(string backGroundServiceUniqueId);
    
}