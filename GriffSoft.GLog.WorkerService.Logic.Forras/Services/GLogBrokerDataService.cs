using GriffSoft.Forras.DataAccess.Install;
using GriffSoft.Forras.DataAccess.Install.Entities.Log;
using GriffSoft.GLog.WorkerService.Logic.Forras.Services.Contracts;
using GriffSoft.GLog.WorkerService.Logic.Forras.Types;
using GriffSoft.GLog.WorkerService.Shared;
using GriffSoft.GLog.WorkerService.Shared.Extensions;

namespace GriffSoft.GLog.WorkerService.Logic.Forras.Services;

public class GLogBrokerDataService : ForrasInstallBaseService, IBrokerDataService
{
    public GLogBrokerDataService(ILogger<GLogBrokerDataService> logger,
        IDbContextFactory<InstallDbContext> forrasInstallFactory) : base(logger, forrasInstallFactory)
    {
    }

    public async Task<List<GLogBrokerData>> CheckoutLogData(int maxLogDataCaptureInCycles,
        string backGroundServiceUniqueId)
    {
        await using var forrasInstallContext = await ForrasInstallFactory.CreateDbContextAsync();
        int noRowsAffected = await forrasInstallContext.Database.ExecuteSqlRawAsync(
            $"UPDATE TOP({maxLogDataCaptureInCycles}) " +
            "GLogBrokerData SET ModifyDate = GETDATE(), BackGroundServiceUniqueId = {0}, Status = {1} WHERE Status = {2};",
            backGroundServiceUniqueId, GLogBrokerDataStatus.UnderProcess, GLogBrokerDataStatus.Available);

        Logger.LogDebugWithLevelCheck($"{nameof(CheckoutLogData)}-> number of rows affected:{noRowsAffected}");
       
        List<GLogBrokerData> brokerDataList = await forrasInstallContext.GLogBrokerData
            .FromSqlRaw(@"SELECT g.Id, g.BackGroundServiceUniqueId, g.LogData, g.ModifyDate, g.Status " +
                        "FROM GLogBrokerData AS g WITH (NOLOCK) " +
                        "WHERE g.Status = {0} AND g.BackGroundServiceUniqueId = {1}", 
                GLogBrokerDataStatus.UnderProcess,
                backGroundServiceUniqueId)
            .ToListAsync();
        return brokerDataList;
    }

    public async Task ResetLogsWithStuckState(string backGroundServiceUniqueId)
    {
        await using var forrasInstallContext = ForrasInstallFactory.CreateDbContext();
        int noRowsAffected = await forrasInstallContext.Database.ExecuteSqlRawAsync(
            "UPDATE GLogBrokerData SET ModifyDate = GETDATE(), Status = 0, BackGroundServiceUniqueId = null WHERE BackGroundServiceUniqueId = {0}",
            backGroundServiceUniqueId);
        Logger.LogInformation("{count} log(s) state has been reset for: {uniqueid}", noRowsAffected,
            backGroundServiceUniqueId);
    }

    public LogEntryDto MakeLogEntryDto(BrokerData brdata, Guid relationalId, string subSystem)
    {
        throw new NotImplementedException();
    }

    public void RemoveLogEntriesByIdList(List<int> idList)
    {
        throw new NotImplementedException();
    }

    public async Task SetMultpleBrokerDataStatusAtOnce(List<GLogBrokerData>? brokerDataList,
        GLogBrokerDataStatus status)
    {
        if (brokerDataList != null && brokerDataList.Count > 0)
        {
            using (var forrasInstallContext = await ForrasInstallFactory.CreateDbContextAsync())
            {
                forrasInstallContext.GLogBrokerData.AttachRange(brokerDataList);

                foreach (var brokerData in brokerDataList)
                {
                    brokerData.Status = status;
                    brokerData.ModifyDate = DateTime.Now;
                }

                await forrasInstallContext.SaveChangesAsync();
                foreach (var brokerData in brokerDataList)
                {
                    forrasInstallContext.Entry(brokerData).State = EntityState.Detached;
                }
            }
        }
    }

    public async Task SetBrokerDataStatus(int brokerDataId, GLogBrokerDataStatus status)
    {
        using (var forrasInstallContext = ForrasInstallFactory.CreateDbContext())
        {
            var brokerData = await forrasInstallContext.GLogBrokerData
                .AsTracking()
                .FirstOrDefaultAsync(f => f.Id == brokerDataId);
            if (brokerData != null)
            {
                brokerData.Status = status;
                brokerData.ModifyDate = DateTime.Now;
                await forrasInstallContext.SaveChangesAsync();
                forrasInstallContext.Entry(brokerData).State = EntityState.Detached;
            }
        }
    }
}