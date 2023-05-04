using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Transactions;
using GriffSoft.Forras.DataAccess.Install;
using GriffSoft.Forras.DataAccess.Install.Entities.Architect;
using GriffSoft.Forras.DataAccess.Install.Entities.History;
using GriffSoft.Forras.DataAccess.Install.Entities.Log;
using GriffSoft.GLog.WorkerService.Logic.Forras.Services.Contracts;
using GriffSoft.GLog.WorkerService.Logic.Forras.Types;
using GriffSoft.GLog.WorkerService.Shared;
using GriffSoft.GLog.WorkerService.Shared.Exceptions;

namespace GriffSoft.GLog.WorkerService.Logic.Forras.Services;

public class ForrasAppCallsLogService : ForrasInstallBaseService,
    IForrasLogConverterGenericService<ForrasAppCallsLogService>
{
    private const string AppCallsLogLogType = "AppCallsLog";
    private readonly string _subSystemVersion;

    public ForrasAppCallsLogService(ILogger<ForrasAppCallsLogService> logger,
        IDbContextFactory<InstallDbContext> forrasInstallFactory) : base(logger,forrasInstallFactory)
    {
        _subSystemVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "N/A";
    }

    public async Task<LogEntryDto> MakeLogEntryDto(BrokerData brokerData, Guid relationalId, string subSystem)
    {
        if (brokerData == null)
        {
            throw new MissingBrokerDataException(typeof(ForrasAppCallsLog));
        }
        ForrasAppCallsLog? forrasAppCallsLog;
        ForrasConnections? forrasConnections;
        List<ForrasConnections_SpecHistory>? forrasConnectionsSpecHistories = null;
        string clientApp = "N/A";
        var transactionOption = new TransactionOptions
        {
            IsolationLevel = IsolationLevel.ReadUncommitted
        };
        using (var scope = new TransactionScope(TransactionScopeOption.Required, transactionOption,
            TransactionScopeAsyncFlowOption.Enabled))
        {
            await using var forrasInstallContext = await ForrasInstallFactory.CreateDbContextAsync();
            
            forrasAppCallsLog = await forrasInstallContext.ForrasAppCallsLog
                .AsNoTracking()
                .Include(conn => conn.ForrasConnections).DefaultIfEmpty()
                .Where(w => w.ForrasID == brokerData.Id)
                .SingleOrDefaultAsync();

            if (forrasAppCallsLog == null)
            {
                throw new MissingLogEntryException(brokerData.Id, typeof(ForrasAppCallsLog));
            }

            forrasConnections = forrasAppCallsLog.ForrasConnections;
            if (forrasAppCallsLog.ConnectionID > 0)
            {
                forrasConnectionsSpecHistories = await forrasInstallContext.ForrasConnections_SpecHistory
                    .AsNoTracking()
                    .Where(w => w.ConnectionID == forrasAppCallsLog.ConnectionID)
                    .ToListAsync();
                clientApp = forrasConnectionsSpecHistories.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.ClientApp))
                    ?.ClientApp ?? "N/A";
            }

            scope.Complete();
        }

        string logSystem = clientApp.Contains(" ") ? clientApp.Split(" ")[0] : clientApp;
        string logSystemVersion = clientApp.Contains(" ") ? clientApp.Split(" ")[1] : "N/A";

        string? userInfo = forrasConnections?.userid ?? forrasConnectionsSpecHistories
            ?.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.userid))?.userid;

        StringBuilder additionalDataBuilder = new("<Data>");
        additionalDataBuilder.AppendLine($"<ReturnType>{forrasAppCallsLog.ReturnType}</ReturnType>");
        additionalDataBuilder.AppendLine($"<LoadTime_ms>{forrasAppCallsLog.LoadTime_ms}</LoadTime_ms>");
        additionalDataBuilder.AppendLine($"<RunTime_ms>{forrasAppCallsLog.RunTime_ms}</RunTime_ms>");
        additionalDataBuilder.AppendLine($"<StartTime>{forrasAppCallsLog.StartTime}</StartTime>");
        additionalDataBuilder.AppendLine($"<EndTime>{forrasAppCallsLog.EndTime}</EndTime>");
        additionalDataBuilder.AppendLine($"{forrasAppCallsLog.AssemblyInfo}");
        additionalDataBuilder.AppendLine($"</Data>");
        string additionalData = additionalDataBuilder.ToString();

        Guid correlation = Guid.TryParse(forrasAppCallsLog.CorrelationId, out Guid guid) ? guid : relationalId;

        string moduleVersion;
        if( forrasAppCallsLog.AssemblyInfo != null )
        {
            Regex version = new("Version=[\"]([0-9.]*)[\"]");
            var versionGroups = version.Matches(forrasAppCallsLog.AssemblyInfo).FirstOrDefault()?.Groups.Values.ToArray();
            moduleVersion = versionGroups switch
            {
                {Length: 2} => versionGroups[1].Value,
                {Length: 1} or {Length: > 2} => versionGroups[0].Value,
                _ => "N/A"
            };
        }
        else
        {
            moduleVersion = "N/A";
        }

        string externalId = forrasAppCallsLog.RelatedId != null
            ? $"{forrasAppCallsLog.ForrasID}({forrasAppCallsLog.RelatedId})"
            : $"{forrasAppCallsLog.ForrasID}";

        return new LogEntryDto()
        {
            UniqueId = Guid.NewGuid(),
            CorrelationId = correlation,
            System = logSystem,
            SystemVersion = logSystemVersion,
            Session = forrasAppCallsLog.SessionId,
            Host = forrasAppCallsLog.Host,
            LogDate = forrasAppCallsLog.DiaryTime,
            Module = forrasAppCallsLog.AssemblyName,
            ModuleVersion = moduleVersion,
            AdditionalData = additionalData,
            Function = forrasAppCallsLog.FuncId,
            LogType = AppCallsLogLogType,
            IsAudit = false,
            HostConfig = null,
            LogLevel = forrasAppCallsLog.LogLevel,
            UserData = userInfo,
            SubSystem = subSystem,
            SubSystemVersion = _subSystemVersion,
            ExternalId = externalId,
            InputValues = forrasAppCallsLog.Parameters,
            OutputValues = forrasAppCallsLog.ReturnValue,
            Message = forrasAppCallsLog.LogInfo,
            BrokerDataSource = (int)BrokerDataSource.ForrasAppCallsLog
        };
    }

    public async Task RemoveLogEntriesByIdList(List<int> idList)
    {
        await using var forrasInstallContext = await ForrasInstallFactory.CreateDbContextAsync();

        var markedAsRemove = forrasInstallContext.ForrasAppCallsLog.Where(w => idList.Contains(w.ForrasID));
        forrasInstallContext.ForrasAppCallsLog.RemoveRange(markedAsRemove);
        await forrasInstallContext.SaveChangesAsync();
    }
}