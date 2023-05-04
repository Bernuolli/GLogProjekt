using System.Configuration.Install;
using System.Data.SqlClient;
using System.Reflection;
using System.ServiceProcess;
using GriffSoft.GLog.WorkerService.Extensions;
using NLog.Web;
using Microsoft.AspNetCore.Builder;

if (args.Contains("/start") && args.Length == 2)
{
    StartService(args[1]);
    return;
}

if (args.Contains("/register") && args.Length == 2)
{
    Register(args[1]);
    return;
}

if (args.Contains("/unregister") && args.Length == 2)
{
    Unregister(args[1]);
    return;
}

if( args.Contains( "/installnlogdb" ) )
{
    InstallNLogDb();
    return;
}

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureGLogServices()
    .UseDefaultServiceProvider((context, options) =>
    {
        options.ValidateScopes = true;
        options.ValidateOnBuild = true;
    })
    .UseWindowsService()
    .ConfigureLogging(logging => logging.ClearProviders())
    .UseNLog()
    .Build();    
host.ConfigureGLog();

await host.RunAsync();

//TODO ServiceCollection -> regisztrálás
void Register(string serviceName)
{
    var exeLocation = Assembly.GetEntryAssembly()!.Location.Replace(".dll", ".exe");
    string appPath = $"/assemblypath={exeLocation}";

    ServiceProcessInstaller serviceProcessInstaller = new();
    serviceProcessInstaller.Account = ServiceAccount.LocalSystem;

    ServiceInstaller serviceInstaller = new();

    string[] commandLine = {appPath};
    System.Collections.Specialized.ListDictionary stateSaver = new();
    var installContext = new InstallContext("", commandLine);
    serviceInstaller.Context = installContext;
    serviceInstaller.ServiceName = serviceName;
    serviceInstaller.DisplayName = serviceName;
    serviceInstaller.Description = $"GriffSoft GLog szolgáltatás. ({serviceName})";
    if (OperatingSystem.IsWindows())
    {
        serviceInstaller.StartType = ServiceStartMode.Automatic;
    }

    serviceInstaller.Parent = serviceProcessInstaller;
    serviceInstaller.Install(stateSaver);
    StartService(serviceName);
}

//TODO ServiceCollection -> regisztrálás törlése
void Unregister(string serviceName)
{
    using (var serviceInstallerObj = new ServiceInstaller())
    {
        var exeLocation = Assembly.GetEntryAssembly()!.Location.Replace(".dll", ".exe");
        string appPath = $"/assemblypath={exeLocation}";
        string[] commandLine = {appPath};
        var context = new InstallContext(null, commandLine);

        serviceInstallerObj.Context = context;
        serviceInstallerObj.ServiceName = serviceName;

        try
        {
            serviceInstallerObj.Uninstall(null);
        }
        catch (Exception ex)
        {
            throw new Exception("Failed to uninstall the service.", ex);
        }
    }
}

void StartService(string serviceName)
{
    using ServiceController serviceController = new(serviceName);
    if (serviceController.Status != ServiceControllerStatus.Running)
    {
        serviceController.Start();
        try
        {
            serviceController.WaitForStatus(ServiceControllerStatus.Running, new TimeSpan(30000));
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            Console.WriteLine("A szolgáltatást nem sikerült elindítani.");
        }
    }
}


void InstallNLogDb()
{
    var builder = WebApplication.CreateBuilder( args );
    var constring = builder.Configuration.GetValue<string>( "ConnectionStrings:GLogInternalLogDb" );
    string uniqueId = builder.Configuration.GetValue<string>( "GLogConfig:UniqueId" );
    SqlConnectionStringBuilder sqlConnectionStringBuilder = new SqlConnectionStringBuilder( constring );
    
    using SqlConnection sqlConnection = new SqlConnection(constring);
    sqlConnection.Open();
    
    string installScript = File.ReadAllText( @".\nlogDbSetup.txt" );
    const string COMMAND_SEPARATOR = "NEXTCOMMAND";
    string[] commands = installScript.Split( COMMAND_SEPARATOR );
    
    using SqlTransaction transaction = sqlConnection.BeginTransaction();
    try
    {
        foreach(string command in commands )
        {
            string currentCommand;
            currentCommand = command.Replace( "<<SERVICE_UNIQUE_ID>>", uniqueId );
            currentCommand = currentCommand.Replace( "<<SERVICE_NLOG_DB>>", sqlConnectionStringBuilder.InitialCatalog );
            using SqlCommand sqlCommand = new SqlCommand( currentCommand, sqlConnection, transaction );
            sqlCommand.ExecuteNonQuery();
        }
        transaction.Commit();
        Console.WriteLine( "Internal logging has been succesfully installed." );
    }
    catch (Exception ex)
    {
        if(transaction is not null )
        {
            try
            {
                transaction.Rollback();
            }
            catch
            {
                Console.WriteLine("An error occured during installation and rollback did not complete succesfully.");
                throw;
            }
        }
        Console.WriteLine(ex.Message);
    }

    return;
}