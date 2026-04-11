using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public class FileDownloadService
    {
        private static HttpListener? _listener;
        private static CancellationTokenSource? _cancellationTokenSource;
        private static Task? _serverTask;
        private static readonly HttpClient _httpClient = new HttpClient();
        
        private static string _username = string.Empty;
        private static string _backupDirectory = string.Empty;
        private static int _port = 8080;
        private static bool _isRunning = false;
        private static bool _hasNotifiedOnline = false;
        
        private const string FirebaseUrl = "https://pinaypal-backup-manager-default-rtdb.firebaseio.com/";

        public static event Action? OnServerStarted;
        public static event Action? OnServerStopped;
        public static event Action<string>? OnError;

        public static bool IsRunning => _isRunning;
        public static bool RequiresAdmin => true;

        public static void Initialize(string username, string backupDirectory, int port = 8080)
        {
            _username = username;
            _backupDirectory = backupDirectory;
            _port = port;
            
            LogService.WriteSystemLog($"[FileDownloadService] Initialized with username: {username}, port: {port}", "Information", "SYSTEM");
        }

        public static async Task StartAsync()
        {
            if (_isRunning)
            {
                LogService.WriteSystemLog("[FileDownloadService] Server already running", "Warning", "SYSTEM");
                return;
            }

            if (string.IsNullOrEmpty(_username))
            {
                LogService.WriteSystemLog("[FileDownloadService] Not initialized - call Initialize first", "Error", "SYSTEM");
                OnError?.Invoke("Service not initialized");
                return;
            }

            try
            {
                _listener = new HttpListener();
                
                // Try to bind to all interfaces first (requires admin or URL reservation)
                try
                {
                    _listener.Prefixes.Add($"http://+:{_port}/");
                    _listener.Start();
                    LogService.WriteSystemLog($"[FileDownloadService] HTTP server started on all interfaces, port {_port}", "Information", "SYSTEM");
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 5) // Access denied
                {
                    // Fallback to localhost only
                    LogService.WriteSystemLog("[FileDownloadService] Cannot bind to all interfaces (requires admin or URL reservation), falling back to localhost", "Warning", "SYSTEM");
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://localhost:{_port}/");
                    _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                    _listener.Start();
                    LogService.WriteSystemLog($"[FileDownloadService] HTTP server started on localhost only, port {_port}", "Information", "SYSTEM");
                    NotificationService.ShowBackupToast(
                        "HTTP Server Warning",
                        "Server running on localhost only. Mobile devices on same network cannot connect. Run as administrator or add URL reservation.",
                        "Warning"
                    );
                }
                
                _cancellationTokenSource = new CancellationTokenSource();
                _isRunning = true;
                _hasNotifiedOnline = false;
                
                _serverTask = Task.Run(() => ListenForConnections(_cancellationTokenSource.Token));
                
                // Start updating connection status
                _ = Task.Run(() => UpdateConnectionStatusLoop(_cancellationTokenSource.Token));
                
                OnServerStarted?.Invoke();
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FileDownloadService] Failed to start server: {ex.Message}", "Error", "SYSTEM");
                OnError?.Invoke(ex.Message);
                Stop();
            }
        }

        public static void Stop()
        {
            if (!_isRunning) return;

            try
            {
                _cancellationTokenSource?.Cancel();
                _listener?.Stop();
                _listener?.Close();
                _isRunning = false;
                
                LogService.WriteSystemLog("[FileDownloadService] HTTP server stopped", "Information", "SYSTEM");
                OnServerStopped?.Invoke();
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FileDownloadService] Error stopping server: {ex.Message}", "Error", "SYSTEM");
            }
        }

        private static async Task ListenForConnections(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context), cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    LogService.WriteSystemLog($"[FileDownloadService] Connection error: {ex.Message}", "Error", "SYSTEM");
                }
            }
        }

        private static async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                LogService.WriteSystemLog($"[FileDownloadService] Request: {request.HttpMethod} {request.Url?.PathAndQuery}", "Information", "SYSTEM");

                if (request.HttpMethod == "GET" && request.Url?.AbsolutePath.StartsWith("/download/") == true)
                {
                    var filename = request.Url.AbsolutePath.Substring("/download/".Length);
                    await HandleFileDownload(context, filename);
                }
                else
                {
                    SendErrorResponse(response, 404, "Not Found");
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FileDownloadService] Request handling error: {ex.Message}", "Error", "SYSTEM");
                SendErrorResponse(response, 500, "Internal Server Error");
            }
        }

        private static async Task HandleFileDownload(HttpListenerContext context, string filename)
        {
            var response = context.Response;
            
            try
            {
                // Security: Validate filename to prevent path traversal
                if (filename.Contains("..") || filename.Contains("/") || filename.Contains("\\"))
                {
                    LogService.WriteSystemLog($"[FileDownloadService] Invalid filename requested: {filename}", "Warning", "SYSTEM");
                    SendErrorResponse(response, 400, "Invalid filename");
                    return;
                }

                // Search in all backup directories
                var filePath = FindBackupFile(filename);
                
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    LogService.WriteSystemLog($"[FileDownloadService] File not found: {filename}", "Warning", "SYSTEM");
                    SendErrorResponse(response, 404, "File not found");
                    return;
                }

                // Determine content type
                var contentType = GetContentType(filename);
                response.ContentType = contentType;
                response.ContentLength64 = new FileInfo(filePath).Length;
                
                // Add headers for download
                response.AddHeader("Content-Disposition", $"attachment; filename=\"{filename}\"");
                
                // Stream the file
                using var fileStream = File.OpenRead(filePath);
                await fileStream.CopyToAsync(response.OutputStream);
                
                LogService.WriteSystemLog($"[FileDownloadService] File downloaded: {filename} ({response.ContentLength64} bytes)", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FileDownloadService] File download error: {ex.Message}", "Error", "SYSTEM");
                SendErrorResponse(response, 500, "Download failed");
            }
            finally
            {
                response.Close();
            }
        }

        private static string? FindBackupFile(string filename)
        {
            // Search in configured backup directories
            var directories = new[]
            {
                ConfigService.Current.Paths.FtpLocalFolder,
                ConfigService.Current.Paths.SqlLocalFolder,
                ConfigService.Current.Paths.MailchimpFolder,
                _backupDirectory
            };

            foreach (var dir in directories)
            {
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    var filePath = Path.Combine(dir, filename);
                    if (File.Exists(filePath))
                    {
                        return filePath;
                    }
                    
                    // Also search subdirectories (one level deep)
                    try
                    {
                        foreach (var subDir in Directory.GetDirectories(dir))
                        {
                            var subPath = Path.Combine(subDir, filename);
                            if (File.Exists(subPath))
                            {
                                return subPath;
                            }
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        private static string GetContentType(string filename)
        {
            var extension = Path.GetExtension(filename).ToLowerInvariant();
            return extension switch
            {
                ".zip" => "application/zip",
                ".sql" => "application/sql",
                ".csv" => "text/csv",
                ".json" => "application/json",
                ".txt" => "text/plain",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
        }

        private static void SendErrorResponse(HttpListenerResponse response, int statusCode, string message)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            
            var errorResponse = new
            {
                error = message,
                statusCode = statusCode
            };
            
            var json = JsonSerializer.Serialize(errorResponse);
            var buffer = Encoding.UTF8.GetBytes(json);
            
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        private static async Task UpdateConnectionStatusLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await UpdateConnectionStatusAsync();
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[FileDownloadService] Connection status update error: {ex.Message}", "Error", "SYSTEM");
                }
                
                // Update every 15 seconds
                await Task.Delay(15000, cancellationToken);
            }
            
            // Set status to offline when stopping
            try
            {
                await SetConnectionStatusAsync("offline");
            }
            catch { }
        }

        private static async Task UpdateConnectionStatusAsync()
        {
            var ipAddress = GetLocalIpAddress();
            
            var connectionData = new
            {
                status = "online",
                lastSeen = DateTime.UtcNow.ToString("o"),
                ipAddress = ipAddress,
                port = _port.ToString()
            };
            
            await SetConnectionStatusAsync("online", connectionData);
            
            // Show notification only once when coming online
            if (!_hasNotifiedOnline)
            {
                _hasNotifiedOnline = true;
                NotificationService.ShowBackupToast(
                    "PC Online", 
                    $"File download server started on http://{ipAddress}:{_port}", 
                    "Success"
                );
            }
        }

        private static async Task SetConnectionStatusAsync(string status, object? data = null)
        {
            try
            {
                if (string.IsNullOrEmpty(_username))
                {
                    LogService.WriteSystemLog("[FileDownloadService] Cannot update connection status: username is empty", "Error", "SYSTEM");
                    return;
                }

                var url = $"{FirebaseUrl}users/{_username}/connection.json";
                LogService.WriteSystemLog($"[FileDownloadService] Updating connection status to Firebase: {url}", "Information", "SYSTEM");
                
                if (data != null)
                {
                    var json = JsonSerializer.Serialize(data);
                    LogService.WriteSystemLog($"[FileDownloadService] Sending data: {json}", "Information", "SYSTEM");
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PutAsync(url, content);
                    
                    LogService.WriteSystemLog($"[FileDownloadService] Firebase response status: {response.StatusCode}", "Information", "SYSTEM");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        LogService.WriteSystemLog($"[FileDownloadService] Connection status updated: {status}", "Information", "SYSTEM");
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        LogService.WriteSystemLog($"[FileDownloadService] Firebase update failed: {response.StatusCode} - {errorContent}", "Error", "SYSTEM");
                    }
                }
                else
                {
                    // Just update status field
                    var updateData = new { status };
                    var json = JsonSerializer.Serialize(updateData);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    
                    var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
                    {
                        Content = content
                    };
                    
                    var response = await _httpClient.SendAsync(request);
                    LogService.WriteSystemLog($"[FileDownloadService] Firebase PATCH response: {response.StatusCode}", "Information", "SYSTEM");
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FileDownloadService] Failed to update connection status: {ex.Message}", "Error", "SYSTEM");
                LogService.WriteSystemLog($"[FileDownloadService] Stack trace: {ex.StackTrace}", "Error", "SYSTEM");
            }
        }

        private static string GetLocalIpAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    // Return first IPv4 address that's not loopback
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && 
                        !IPAddress.IsLoopback(ip))
                    {
                        return ip.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FileDownloadService] Error getting local IP: {ex.Message}", "Warning", "SYSTEM");
            }
            
            return "127.0.0.1"; // Fallback
        }

        /// <summary>
        /// Gets the command to add URL reservation for the HTTP server (requires admin)
        /// </summary>
        public static string GetUrlReservationCommand(int port)
        {
            return $"netsh http add urlacl url=http://+:{port}/ user={Environment.UserDomainName}\\{Environment.UserName}";
        }

        /// <summary>
        /// Gets the command to remove URL reservation for the HTTP server (requires admin)
        /// </summary>
        public static string GetUrlRemovalCommand(int port)
        {
            return $"netsh http delete urlacl url=http://+:{port}/";
        }
    }
}
