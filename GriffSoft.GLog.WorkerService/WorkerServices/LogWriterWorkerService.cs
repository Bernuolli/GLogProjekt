using GriffSoft.GLog.WorkerService.Helpers;
using GriffSoft.GLog.WorkerService.Logic.Forras.Helpers;
using GriffSoft.GLog.WorkerService.Logic.Forras.Services.Contracts;
using GriffSoft.GLog.WorkerService.Logic.Forras.Types;
using GriffSoft.GLog.WorkerService.Logic.Services.Contracts;
using GriffSoft.GLog.WorkerService.Shared.Models;
using GriffSoft.GLog.WorkerService.Shared;
using GriffSoft.GLog.WorkerService.Shared.Extensions;
using GriffSoft.GLog.WorkerService.Shared.Queue;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using GriffSoft.GLog.WorkerService.Logic;

namespace GriffSoft.GLog.WorkerService.WorkerServices;

public class LogWriterWorkerService : BackgroundService
{
    private readonly ILogger<LogWriterWorkerService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackGroundQueue<List<LogEntryDto>> _writerQueue;
    private readonly IBackGroundQueue<FeedbackData> _feedbackQueue;
    private readonly IOptions<GLogConfig> _config;
    private readonly int _millisecondsDelay = 5000;
    private readonly IGLogWorkerErrorHelper _errorHelper;    
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IFUVLogMemory<FUVLogMemory> _fuvLogStatusRangeMemory;

    public LogWriterWorkerService( ILogger<LogWriterWorkerService> logger, IServiceScopeFactory scopeFactory,
        IBackGroundQueue<List<LogEntryDto>> writerQueue, IGLogWorkerErrorHelper errorHelper, IBackGroundQueue<FeedbackData> feedbackQueue,
        IOptions<GLogConfig> config, IServiceScopeFactory serviceScopeFactory,
        IFUVLogMemory<FUVLogMemory> fuvLogStatusRangeMemory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _writerQueue = writerQueue;
        _errorHelper = errorHelper;
        _feedbackQueue = feedbackQueue;
        _config = config;        
        _serviceScopeFactory = serviceScopeFactory;
        _fuvLogStatusRangeMemory = fuvLogStatusRangeMemory;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        while( !stoppingToken.IsCancellationRequested )
        {
            try
            {
                await ProcessWriting();
                await Task.Delay( _millisecondsDelay, stoppingToken );
            }
            catch( Exception ex )
            {
                _errorHelper.AddError( out TimeSpan nexTryTimeSpan, out string errorPrefix );
                _logger.LogError( ex, errorPrefix + ex.Message );
                await Task.Delay( nexTryTimeSpan, stoppingToken );
                continue;
            }
        }
    }

    /// <summary>
    /// Go through <see cref="_writerQueue"/> and write the data to GLog.
    /// </summary>
    /// <returns></returns>
    private async Task ProcessWriting()
    {
        _logger.LogDebugWithLevelCheck($"{nameof(ProcessWriting)} is started");
        int itemCount = _writerQueue.GetQueuedItemCount();

        _logger.LogDebugWithLevelCheck($"{itemCount} logEntry lists are queued.");
        
        while (!_writerQueue.IsEmpty())
        {
            var logEntryDtoList = _writerQueue.Dequeue();
            if (logEntryDtoList == null)
            {
                continue;
            }

            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var gLogWriterService = scope.ServiceProvider.GetRequiredService<IGLogWriterService>();

            await gLogWriterService.CreateLogFromLogEntryListAsync(logEntryDtoList);

            foreach (var entryDto in logEntryDtoList)
            {
                FeedbackData feedbackData = new FeedbackData()
                {
                    BrokerDataSource = (BrokerDataSource) entryDto.BrokerDataSource,
                    ExternalId = entryDto.ExternalId,
                    GlogBrokerDataId = entryDto.GlogBrokerDataId
                };
                _feedbackQueue.Enqueue( feedbackData );
                
                if( entryDto.BrokerDataSource == (int)BrokerDataSource.FUVLog )
                {
                    await UpdateFuvLogCheckoutDataIfNeeded( entryDto );
                }
            }
            _logger.LogDebugWithLevelCheck($"{nameof(ProcessWriting)} is finished");
        }
    }

    /// <summary>
    /// Check whether <paramref name="entryDto"/> is the last checked out data and update the state if needed.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="entryDto"></param>
    /// <returns></returns>
    private async Task UpdateFuvLogCheckoutDataIfNeeded( LogEntryDto entryDto )
    {
        if( entryDto.ExternalId == _fuvLogStatusRangeMemory.GetRangeTo().ToString() )
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var fuvGLogHelper = scope.ServiceProvider.GetRequiredService<IFUVGLogHelper>();
            await fuvGLogHelper.SetFuvLogCheckoutToProcessedAsync( _config.Value.UniqueId );
        }
    }
}