using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Timers;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;

class Program
{
    private static IConfiguration Configuration;
    private static string FtpHost;
    private static string FtpUsername;
    private static string FtpPassword;
    private static readonly string RemoteDirectory = "/screenshots"; // Path relative to user's home
    private static readonly string LocalSaveDirectory = Path.Combine(Path.GetTempPath(), "Screenshots");
    private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "ScreenshotUploader.log");

    private static System.Timers.Timer aTimer;

    static void Main()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

        Configuration = builder.Build();

        FtpHost = Configuration["FtpSettings:FtpHost"];
        FtpUsername = Configuration["FtpSettings:FtpUsername"];
        FtpPassword = Configuration["FtpSettings:FtpPassword"];

        Console.WriteLine("Screenshot Uploader Started. Press any key to exit.");
        Directory.CreateDirectory(LocalSaveDirectory);

        // NEW STEP: Ensure the target directory exists on the FTP server once at startup
        EnsureDirectoryExistsFtp(FtpHost, RemoteDirectory, FtpUsername, FtpPassword);

        aTimer = new System.Timers.Timer(60000); // 60 seconds
        aTimer.Elapsed += OnTimedEvent;
        aTimer.AutoReset = true;
        aTimer.Enabled = true;

        Console.ReadKey();
    }

    private static void OnTimedEvent(Object source, ElapsedEventArgs e)
    {
        try
        {
            string filePath = CaptureScreenAndSave(LocalSaveDirectory);
            Console.WriteLine($"Screenshot saved to {filePath}");

            // Correct URI construction: FtpHost + RemoteDirectory + FileName
            string remoteUri = $"{FtpHost}{RemoteDirectory}/{Path.GetFileName(filePath)}";

            // Log the URI for debugging
            Console.WriteLine($"Attempting to upload to: {remoteUri}");

            UploadFileFtp(filePath, remoteUri, FtpUsername, FtpPassword);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
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

        string fileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
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
                Console.WriteLine($"Upload File Complete, status {response.StatusDescription}");
            }

            File.Delete(localFilePath);
            Console.WriteLine($"Local file {localFilePath} deleted.");
        }
        catch (Exception ex)
        {
            // Specifically check for 553, though the fix should resolve it
            if (ex.Message.Contains("(553)"))
            {
                Console.WriteLine("CRITICAL: 553 Error persists. Check file name for invalid characters or server-side write permissions.");
            }
            Console.WriteLine($"FTP Upload Failed: {ex.Message}");
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
                Console.WriteLine($"FTP Directory '{directory}' created successfully. Status: {response.StatusDescription}");
            }
        }
        catch (WebException ex)
        {
            FtpWebResponse response = ex.Response as FtpWebResponse;
            // 550 usually means the directory already exists or permission denied
            if (response != null && response.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
            {
                Console.WriteLine($"FTP Directory '{directory}' likely already exists or permissions denied (550). Proceeding.");
            }
            else
            {
                Console.WriteLine($"Error creating directory {directory}: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error in directory check: {ex.Message}");
        }
    }
}
