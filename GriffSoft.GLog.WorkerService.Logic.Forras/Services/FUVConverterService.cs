using System.Reflection;
using System.Text;
using System.Transactions;
using GriffSoft.Forras.DataAccess.FUV.Entities;
using GriffSoft.GLog.WorkerService.Logic.Forras.Services.Contracts;
using GriffSoft.GLog.WorkerService.Logic.Forras.Types;
using GriffSoft.GLog.WorkerService.Shared;
using GriffSoft.GLog.WorkerService.Shared.Exceptions;

namespace GriffSoft.GLog.WorkerService.Logic.Forras.Services;

public class FUVConverterService : IFuvConverterService
{
    private readonly ILogger<FUVConverterService> _logger;
    private readonly IFUVGLogHelper _fuvHelper;
    private readonly string _subSystemVersion;

    public FUVConverterService(ILogger<FUVConverterService> logger, IFUVGLogHelper fuvHelper)
    {
        _logger = logger;
        _fuvHelper = fuvHelper;
        _subSystemVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "N/A";
    }

    public bool CheckConnection()
    {
        using (var fuvContext = _fuvHelper.GetFuvDbContextAsync().Result)
        {
            var canConnect = fuvContext.Database.CanConnect();
            _logger.LogInformation($"FUVContext availability:{fuvContext}");
            return canConnect;
        }
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="ids"></param>
    /// <returns></returns>
    /// <exception cref="MissingLogEntryException"></exception>
    public async Task<List<LogEntryDto>> MakeLogEntryDtos( List<int> ids )
    {
        FUV_Log? fuvLog;
        var transactionOption = new TransactionOptions
        {
            IsolationLevel = IsolationLevel.ReadUncommitted
        };

        List<LogEntryDto> returnList = new();

        using( var fuvlContext = await _fuvHelper.GetFuvDbContextAsync() )
        {
            foreach( int id in ids )
            {
                fuvLog = await fuvlContext.FUV_Log
                        .AsNoTracking()
                        .Where( w => w.Id == id )
                        .Include( i => i.FUV_TaskStatus )
                        .ThenInclude( fTaskStatus => fTaskStatus.FUV_TaskStatusParameters ).DefaultIfEmpty()
                        .Include( fLog => fLog.FUV_TaskStatus.FUV_Task )
                        .ThenInclude( fTask => fTask.FUV_TaskType )
                        .ThenInclude( fTaskType => fTaskType.FUV_TaskTypeParameters ).DefaultIfEmpty()

                        .SingleOrDefaultAsync();

                if( fuvLog == null )
                {
                    throw new MissingLogEntryException( id, typeof( FUV_Log ) );
                }
                StringBuilder additionalDataBuilder = new( "<Data>" );
                additionalDataBuilder.AppendLine( $"<TaskStatusId>{fuvLog.TaskStatusId}</TaskStatusId>" );
                additionalDataBuilder.AppendLine( $"<Status>{fuvLog.Status}</Status>" );
                additionalDataBuilder.AppendLine( $"<Error>{fuvLog.Error}</Error>" );
                additionalDataBuilder.AppendLine( $"</Data>" );
                string additionalData = additionalDataBuilder.ToString();
                string inputValuesInString = "N/A";
                if( fuvLog.FUV_TaskStatus?.FUV_Task?.FUV_TaskType?.FUV_TaskTypeParameters != null && fuvLog.FUV_TaskStatus?.FUV_TaskStatusParameters != null )
                {
                    var taskTypeParams = fuvLog.FUV_TaskStatus.FUV_Task.FUV_TaskType.FUV_TaskTypeParameters.ToDictionary( s => s.Id, s => s.Name );
                    var inputValues = fuvLog.FUV_TaskStatus.FUV_TaskStatusParameters.ToDictionary( tdKey => tdKey.Id, tdValue => tdValue.Value );

                    inputValuesInString = String.Join( ";", inputValues.Select( s =>
                        $"{( taskTypeParams.ContainsKey( s.Key ) ? $"{taskTypeParams[s.Key]}" : $"{s.Key}" )} : {{s.Value}}" ) );
                }

                returnList.Add( new LogEntryDto()
                    {
                        UniqueId = Guid.NewGuid(),
                        System = "FÜV",
                        SystemVersion = "N/A",
                        Module = fuvLog.FUV_TaskStatus?.FUV_Task?.FUV_TaskType?.ModuleId,
                        Function = fuvLog.FUV_TaskStatus?.FUV_Task?.FUV_TaskType?.FuncId,
                        InputValues = inputValuesInString,
                        CorrelationId = null,
                        LogDate = fuvLog.LogDate ?? DateTime.Now,
                        SubSystem = "FÜVLog",
                        SubSystemVersion = _subSystemVersion,
                        Message = fuvLog.Message,
                        AdditionalData = additionalData,
                        ExternalId = fuvLog.Id.ToString(),
                        BrokerDataSource = (int)BrokerDataSource.FUVLog
                    });
            }
        }
        return returnList;
    }

    public async Task RemoveLogEntriesByIdList(List<int> idList)
    {
        await using var fuvContext = await _fuvHelper.GetFuvDbContextAsync();
        var markedAsRemove = fuvContext.FUV_Log.Where(w => idList.Contains(w.Id));
        fuvContext.FUV_Log.RemoveRange(markedAsRemove);
        await fuvContext.SaveChangesAsync();
    }
}