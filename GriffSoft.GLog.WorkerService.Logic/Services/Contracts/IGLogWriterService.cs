using GriffSoft.GLog.DataAccess.LogDb;
using GriffSoft.GLog.WorkerService.Shared;

namespace GriffSoft.GLog.WorkerService.Logic.Services.Contracts;

public interface IGLogWriterService
{
    Task CreateLogFromLogEntryListAsync(List<LogEntryDto> logData);
    Task<(List<GLogHost> HostList, List<GLogMasterData> MasterDataList, List<GriffSoft.GLog.DataAccess.LogDb.GLog> LogList)> GetLogDataFromLogEntryDtoList(List<LogEntryDto> dto);
    Task<bool> CheckGLogDbConnection();
}