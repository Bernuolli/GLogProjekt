using System.Collections.Concurrent;
using System.Text.Json;
using GriffSoft.Forras.DataAccess.Install;
using GriffSoft.Forras.DataAccess.Install.Entities.Log;
using GriffSoft.GLog.WorkerService.Logic.Contracts;
using GriffSoft.GLog.WorkerService.Logic.Forras.Services;
using GriffSoft.GLog.WorkerService.Logic.Forras.Services.Contracts;
using GriffSoft.GLog.WorkerService.Logic.Forras.Types;
using GriffSoft.GLog.WorkerService.Shared;
using GriffSoft.GLog.WorkerService.Shared.Exceptions;
using GriffSoft.GLog.WorkerService.Shared.Extensions;
using GriffSoft.GLog.WorkerService.Shared.Models;
using GriffSoft.GLog.WorkerService.Shared.Queue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GriffSoft.GLog.WorkerService.Logic;

public class ForrasLogCollector : IForrasLogCollector
{
    private readonly GLogConfig _logConfig;
    private readonly ILogger<ForrasLogCollector> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IBackGroundQueue<List<LogEntryDto>> _gLogWriterQueue;
    private readonly IDbContextFactory<InstallDbContext> _forrasInstallFactory;

    public ForrasLogCollector(IOptions<GLogConfig> logConfig, ILogger<ForrasLogCollector> logger,
        IServiceScopeFactory serviceScopeFactory, IBackGroundQueue<List<LogEntryDto>> gLogWriterQueue,
        IDbContextFactory<InstallDbContext> forrasInstallFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _gLogWriterQueue = gLogWriterQueue;
        _logConfig = logConfig.Value;
        _forrasInstallFactory = forrasInstallFactory;
    }

    /// <summary>
    /// Reset the state of the logs from previous runs. 
    /// </summary>
    public async Task ResetLogsWithStuckState()
    {
        _logger.LogInformation($"Attempting to reset stuck log entries: {_logConfig.UniqueId}");

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var brokerDataService = scope.ServiceProvider.GetRequiredService<IBrokerDataService>();
        await brokerDataService.ResetLogsWithStuckState(_logConfig.UniqueId);
        if( _logConfig.FuvDbExists )
        {            
            var fuvGLogHelper = scope.ServiceProvider.GetRequiredService<IFUVGLogHelper>();
            await fuvGLogHelper.ResetLogsStuckInWriterQueueAsnyc(_logConfig.UniqueId);
        }
    }

    /// <summary>
    /// Checkout log data of install and fuv databases.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<(List<GLogBrokerData> gLogBrokerDataList, FUVLogCheckoutData? fuvLogCheckoutData)> CheckoutLogDataAsync(CancellationToken cancellationToken )
    {
        List<GLogBrokerData> gLogBrokerDataList = await CheckOutInstallLogData();
        FUVLogCheckoutData? fuvLogCheckoutData = null;
        if( _logConfig.FuvDbExists )
        {
            fuvLogCheckoutData = await CheckoutFuvLogDataAsync();
        }
        return ( gLogBrokerDataList, fuvLogCheckoutData );
    }

    /// <summary>
    /// Get the next batch of ids from install db and put them in <see cref="_checkoutQueue"/>
    /// </summary>
    /// <returns></returns>
    private async Task<List<GLogBrokerData>> CheckOutInstallLogData()
    {
        List<GLogBrokerData> brokerDataList;
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var brokerDataService = scope.ServiceProvider.GetRequiredService<IBrokerDataService>();
        brokerDataList = await brokerDataService.CheckoutLogData(_logConfig.MaxLogDataCaptureInCycles, _logConfig.UniqueId);
       
        if (brokerDataList.Count == 0)
        {
            _logger.LogDebugWithLevelCheck ($"{nameof(CheckOutInstallLogData)} - no new available data");
            return new List<GLogBrokerData>();
        }

        _logger.LogDebugWithLevelCheck(
            $"{nameof(CheckOutInstallLogData)} is finished, CheckoutQue has {brokerDataList.Count} more element");
        return brokerDataList;
    }

    /// <summary>
    /// Get the next batch of ids from fuv db and put them in <see cref="_fuvCheckoutQueue"/>
    /// </summary>
    /// <returns></returns>
    private async Task<FUVLogCheckoutData?> CheckoutFuvLogDataAsync()
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var fuvGLogHelper = scope.ServiceProvider.GetRequiredService<IFUVGLogHelper>();
        var logData = await fuvGLogHelper.CheckOutFuvLogData( _logConfig.MaxLogDataCaptureInCycles, _logConfig.UniqueId );
        return logData;
    }

    /// <summary>
    /// Go through <see cref="_checkoutQueue"/> and <see cref="_fuvCheckoutQueue"/>, convert the data to <see cref="LogEntryDto"/> and 
    /// store it in <see cref="_gLogWriterQueue"/>.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task ProcessLogDataAsync(CancellationToken cancellationToken, List<GLogBrokerData> gLogBrokerDataList, FUVLogCheckoutData? fuvLogCheckoutData)
    {
        bool noFuvCheckoutData = fuvLogCheckoutData is null || fuvLogCheckoutData.AllIds is null;
        if( !gLogBrokerDataList.Any() && noFuvCheckoutData )
        {
            _logger.LogDebugWithLevelCheck( $"{nameof( ProcessLogDataAsync )} - CheckoutQueue and FuvCheckoutQueue is empty" );
            return;
        }


        (List<LogEntryDto> logEntryDtoList, List<int> successList, List<int> failedList, List<int> invalidList) = await GetConvertedInstallGLogData( gLogBrokerDataList );

        if( !noFuvCheckoutData )
        {
            List<LogEntryDto> fuvLogDtoList = await GetConvertedFuvLogData( fuvLogCheckoutData! );
            logEntryDtoList.AddRange( fuvLogDtoList.OrderBy( log => int.Parse( log.ExternalId! ) ) );
        }

        if( logEntryDtoList.Any() )
        {
            _gLogWriterQueue.Enqueue( logEntryDtoList );
            _logger.LogDebugWithLevelCheck( $"{nameof( ProcessLogDataAsync )} is finished, GLogWriterQueue has {logEntryDtoList.Count} more element" );
        }

        if( successList.Any() )
        {
            SetStatusToProcessed( successList );
            _logger.LogDebug( "Succes status for {number} element(s) : {list}", successList.Count,
                String.Join( ',', successList ) );
        }

        if( failedList.Any() )
        {
            RemoveInvalidOrFailedBrokerData(failedList);
            _logger.LogDebugWithLevelCheck( "Failed status for {number} element(s) : {list}", failedList.Count,
                String.Join( ',', failedList ) );
        }

        if( invalidList.Any() )
        {
            RemoveInvalidOrFailedBrokerData(invalidList);
            _logger.LogDebugWithLevelCheck( "Invalid status for {number} element(s) : {list}", invalidList.Count,
                String.Join( ',', invalidList ) );
        }        

        _logger.LogDebug( "BrokerData transformation is done" );
    }

    private async Task<(List<LogEntryDto> logEntryDtoList, List<int> successList, List<int> failedList, List<int> invalidList)> GetConvertedInstallGLogData( List<GLogBrokerData> glogBrokerDataList )
    {
        List<LogEntryDto> logEntryDtoBag = new();
        List<int> successList = new();
        List< int > failedList = new();
        List<int> invalidList = new();

        foreach(var gLogBrokerData in glogBrokerDataList)
        {
            try
            {
                var brokerDataObject = JsonSerializer.Deserialize<BrokerDataObject>( gLogBrokerData.LogData );
                ArgumentNullException.ThrowIfNull( brokerDataObject?.Data );
                if( !brokerDataObject.Data.Any() )
                {
                    //NOTE: üres insert esetén
                    continue;
                }

                await using var scope = _serviceScopeFactory.CreateAsyncScope();
                var logEntryDtoConverter = GetLogEntryDtoConverter( brokerDataObject, scope );
                Guid relationalId = Guid.NewGuid();
                foreach( var brokerData in brokerDataObject.Data )
                {
                    var logEntryDto = await logEntryDtoConverter.MakeLogEntryDto( brokerData, relationalId,
                        brokerDataObject.Source.ToString() );
                    logEntryDto.GlogBrokerDataId = gLogBrokerData.Id;
                    logEntryDtoBag.Add( logEntryDto );
                }
                successList.Add( gLogBrokerData.Id );
            }
            catch( JsonException jE )
            {
                failedList.Add( gLogBrokerData.Id );
                _logger.LogError( jE, $"Invalid JSON:{gLogBrokerData.LogData}" );
            }
            catch( ArgumentNullException anE )
            {
                failedList.Add( gLogBrokerData.Id );
                _logger.LogError( anE, $"JSON parameter cannot be null" );
            }
            catch( NotSupportedException nsE )
            {
                failedList.Add( gLogBrokerData.Id );
                _logger.LogError( nsE,
                    $"There is no compatible JsonConverter for {nameof( BrokerDataObject )} or its serializable members." );
            }
            catch( MissingLogEntryException )
            {
                invalidList.Add( gLogBrokerData.Id );
            }
            catch( MissingBrokerDataException )
            {
                invalidList.Add( gLogBrokerData.Id );
            }
            catch( ArgumentOutOfRangeException aoR )
            {
                failedList.Add( gLogBrokerData.Id );
                _logger.LogError( aoR,
                    $"Not supported {nameof( BrokerDataSource )}" );
            }
            catch( Exception ex )
            {
                _logger.LogError( ex, "Unhandled exception" );
            }
        }

        return (logEntryDtoBag.ToList(),successList.ToList(), failedList.ToList(), invalidList.ToList());
    }

    /// <summary>
    /// Go through <see cref="_fuvCheckoutQueue"/> and convert the data to <see cref="LogEntryDto"/>.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns> List of fuv <see cref="LogEntryDto"/> </returns>
    private async Task<List<LogEntryDto>> GetConvertedFuvLogData( FUVLogCheckoutData fuvLogCheckoutData )
    {
        List<LogEntryDto> fuvLogDtoList = new();
        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var fuvConverterService = scope.ServiceProvider.GetRequiredService<IFuvConverterService>();
            fuvLogDtoList = await fuvConverterService.MakeLogEntryDtos( fuvLogCheckoutData.AllIds! );            
            var fuvGLogHelper = scope.ServiceProvider.GetRequiredService<IFUVGLogHelper>();
            await fuvGLogHelper.SetLogStatusToInWriterQeueueAsync( _logConfig.UniqueId );
        }
        catch( Exception ex )
        {
            _logger.LogError( ex, "Unhandled exception" );
        }
        return fuvLogDtoList;
    }

    private async void SetStatusToProcessed( List<int> successList )
    {
        int index = 0;
        const int batchSize = 500;
        int batchCount = (successList.Count / batchSize)+1;
        for (index = 0; index < batchCount; index++)
        {
            List<int> processData = successList.Skip(index * batchSize).Take(batchSize).ToList();
            if (processData.Count > 0)
            {
                string sql = @$"UPDATE GLogBrokerData SET Status = {(int) GLogBrokerDataStatus.Processed}
WHERE BackGroundServiceUniqueId = '{_logConfig.UniqueId}' AND Id IN ({string.Join(",", processData)})";
                using (var forrasInstallContext = _forrasInstallFactory.CreateDbContext())
                {
                    _ = await forrasInstallContext.Database.ExecuteSqlRawAsync(sql);
                }
            }
        }

    }
    private async void RemoveInvalidOrFailedBrokerData(List<int> removeList)
    {
        int index = 0;
        const int batchSize = 500;
        int batchCount = (removeList.Count / batchSize) + 1;
        for (index = 0; index < batchCount; index++)
        {
            List<int> processData = removeList.Skip(index * batchSize).Take(batchSize).ToList();
            string sql = @$"DELETE FROM GLogBrokerData 
WHERE BackGroundServiceUniqueId = '{_logConfig.UniqueId}' AND Id IN ({string.Join(",", processData)})";
            using (var forrasInstallContext = _forrasInstallFactory.CreateDbContext())
            {
                _ = await forrasInstallContext.Database.ExecuteSqlRawAsync(sql);
            }
        }

    }
    IForrasLogConverterBaseService GetLogEntryDtoConverter(BrokerDataObject brokerDataObject, IServiceScope scope)
    {
        switch (brokerDataObject.Source)
        {
            case BrokerDataSource.ForrasEventLog:
                var forrasEventLogService = scope.ServiceProvider
                    .GetRequiredService<IForrasLogConverterGenericService<ForrasEventLogService>>();
                return forrasEventLogService;
            case BrokerDataSource.ForrasAppCallsLog:
                var forrasAppCallsLogService = scope.ServiceProvider
                    .GetRequiredService<IForrasLogConverterGenericService<ForrasAppCallsLogService>>();
                return forrasAppCallsLogService;
            case BrokerDataSource.ForrasFSTManageLog:
                var forrasFSTManageLogService = scope.ServiceProvider
                    .GetRequiredService<IForrasLogConverterGenericService<ForrasFSTManageLogService>>();
                return forrasFSTManageLogService;
            case BrokerDataSource.ForrasConnections_SpecHistory:
                var forrasConnectionsSpecHistoryService = scope.ServiceProvider
                    .GetRequiredService<IForrasLogConverterGenericService<ForrasConnectionsSpecHistoryService>>();
                return forrasConnectionsSpecHistoryService;
            case BrokerDataSource.ForrasModuleExecutions_SpecHistory:
                var forrasModuleExecutionsSpecHistoryService = scope.ServiceProvider
                    .GetRequiredService<IForrasLogConverterGenericService<ForrasModuleExecutionsSpecHistoryService>>();
                return forrasModuleExecutionsSpecHistoryService;
            case BrokerDataSource.FUVLog:
                var fuvLogService = scope.ServiceProvider.GetRequiredService<IForrasLogConverterGenericService<FUVConverterService>>();
                return fuvLogService;
            case BrokerDataSource.Indefinite:
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}