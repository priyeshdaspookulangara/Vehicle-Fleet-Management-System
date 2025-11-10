using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Threading;
using System.Timers;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;

class Program
{
    private static IConfiguration Configuration;
    private static string FtpHost;
    private static string FtpUsername;
    private static string FtpPassword;
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

        Directory.CreateDirectory(LocalSaveDirectory);

        aTimer = new System.Timers.Timer(60000); // 60 seconds
        aTimer.Elapsed += OnTimedEvent;
        aTimer.AutoReset = true;
        aTimer.Enabled = true;

        // Keep the main thread alive for the background process
        Thread.Sleep(Timeout.Infinite);
    }

    private static void OnTimedEvent(Object source, ElapsedEventArgs e)
    {
        try
        {
            string filePath = CaptureScreenAndSave(LocalSaveDirectory);
            string remoteUri = $"{FtpHost}/screenshots/{Path.GetFileName(filePath)}";
            UploadFileFtp(filePath, remoteUri, FtpUsername, FtpPassword);
        }
        catch (Exception ex)
        {
            Log($"An error occurred: {ex.ToString()}");
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
                // Success, do nothing.
            }

            File.Delete(localFilePath);
        }
        catch (Exception ex)
        {
            Log($"FTP Upload Failed: {ex.ToString()}");
            // Rethrow the exception to be caught by the main event handler's catch block.
            throw;
        }
    }

    private static void Log(string message)
    {
        try
        {
            string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
            File.AppendAllText(LogFile, logMessage + Environment.NewLine);
        }
        catch
        {
            // If logging fails, there's not much else to do in a background app.
        }
    }
}
