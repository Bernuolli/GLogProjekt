using GriffSoft.GLog.DataAccess;
using GriffSoft.GLog.DataAccess.LogDb;
using GriffSoft.GLog.WorkerService.Logic.Forras.Services.Contracts;
using GriffSoft.GLog.WorkerService.Logic.Services.Contracts;
using GriffSoft.GLog.WorkerService.Shared;
using GriffSoft.Shared.Forras.Contracts.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GriffSoft.GLog.WorkerService.Logic.Services;

public class GLogWriterService : IGLogWriterService
{
    private readonly ILogger<GLogWriterService> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public GLogWriterService(ILogger<GLogWriterService> logger, IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;        
    }

    public async Task<bool> CheckGLogDbConnection()
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var _gLogContextFactory = scope.ServiceProvider.GetRequiredService<IGriffDbContextFactory<GLogContext>>();
        using var context = _gLogContextFactory.CreateDbContext();
        return await context.Database.CanConnectAsync();
    }

    public async Task CreateLogFromLogEntryListAsync(List<LogEntryDto> logData)
    {
        var (HostList, MasterDataList, LogList) = await GetLogDataFromLogEntryDtoList(logData);

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var _gLogContextFactory = scope.ServiceProvider.GetRequiredService<IGriffDbContextFactory<GLogContext>>();
        await using var context = await _gLogContextFactory.CreateDbContextAsync();
        await context.GLogHost.AddRangeAsync(HostList);
        await context.GLogMasterData.AddRangeAsync(MasterDataList);
        await context.SaveChangesAsync();

        Dictionary<Guid, int> hostDict = HostList.ToDictionary(keySelector: m => m.TmpId, elementSelector: m => m.Id);
        Dictionary<Guid, int> masterDataDict = MasterDataList.ToDictionary(keySelector: m => m.TmpId, elementSelector: m => m.Id);

        PrepareGLogEntitiesForSaving(ref LogList, hostDict, masterDataDict);

        await context.GLog.AddRangeAsync(LogList);
        await context.SaveChangesAsync();
    }

    private void PrepareGLogEntitiesForSaving(ref List<GriffSoft.GLog.DataAccess.LogDb.GLog> logData, Dictionary<Guid, int> hostDict, Dictionary<Guid, int> masterDataDict)
    {
        foreach (var log in logData)
        {
            if (hostDict.ContainsKey(log.HostTmpId))
            {
                log.HostId = hostDict[log.HostTmpId];
            }

            if (masterDataDict.ContainsKey(log.MasterDataTmpId))
            {
                log.MasterDataId = masterDataDict[log.MasterDataTmpId];
            }
        }
    }

    public async Task<(List<GLogHost> HostList, List<GLogMasterData> MasterDataList, List<GriffSoft.GLog.DataAccess.LogDb.GLog> LogList)> GetLogDataFromLogEntryDtoList(List<LogEntryDto> dto)
    {
        List<GLogHost> gLogHostList = new();
        List<GLogMasterData> gLogMasterDataList = new();
        List<GriffSoft.GLog.DataAccess.LogDb.GLog> gLogList = new();

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var gLogContextFactory = scope.ServiceProvider.GetRequiredService<IGriffDbContextFactory<GLogContext>>();
        await using var context = await gLogContextFactory.CreateDbContextAsync();

        foreach (var logentry in dto)
        {
            GLogHost? host = await context.GLogHost.FirstOrDefaultAsync(f =>
                f.Host == logentry.Host &&
                f.HostConfig == logentry.HostConfig
            );

            if (host == null)
            {
                host = new GLogHost()
                {
                    Host = logentry.Host,
                    HostConfig = logentry.HostConfig
                };
                gLogHostList.Add(host);
            }

            GLogMasterData? gLogMasterData = await context.GLogMasterData.FirstOrDefaultAsync(f =>
                f.System == (logentry.System ?? "N/A") &&
                f.SystemVersion == logentry.SystemVersion &&
                f.SubSystem == (logentry.SubSystem ?? "N/A") &&
                f.SubSystemVersion == logentry.SubSystemVersion &&
                f.Function == logentry.Function &&
                f.Module == (logentry.Module ?? "N/A") &&
                f.ModuleVersion == logentry.ModuleVersion
            );

            if (gLogMasterData == null)
            {
                gLogMasterData = new GLogMasterData()
                {
                    System = logentry.System ?? "N/A",
                    SystemVersion = logentry.SystemVersion,
                    SubSystem = logentry.SubSystem ?? "N/A",
                    SubSystemVersion = logentry.SubSystemVersion,
                    Function = logentry.Function,
                    Module = logentry.Module ?? "N/A",
                    ModuleVersion = logentry.ModuleVersion
                };
                gLogMasterDataList.Add(gLogMasterData);
            }

            var gLog = new GriffSoft.GLog.DataAccess.LogDb.GLog()
            {
                HostId = host.Id,
                HostTmpId = host.TmpId,
                MasterDataId = gLogMasterData.Id,
                MasterDataTmpId= gLogMasterData.TmpId,
                ExternalId = logentry.ExternalId,
                UniqueId = logentry.UniqueId,
                CorrelationId = logentry.CorrelationId,
                AdditionalData = logentry.AdditionalData,
                IsAudit = logentry.IsAudit,
                InputValues = logentry.InputValues,
                OutputValues = logentry.OutputValues,
                LogLevel = logentry.LogLevel,
                LogType = logentry.LogType,
                LogDate = logentry.LogDate,
                Session = logentry.Session,
                Message = logentry.Message,
                UserData = logentry.UserData
            };
            gLogList.Add(gLog);

            context.Entry(host).State = EntityState.Detached;
            context.Entry(gLogMasterData).State = EntityState.Detached;
            context.Entry(gLog).State = EntityState.Detached;
        }
        return (gLogHostList, gLogMasterDataList, gLogList);
    }
}