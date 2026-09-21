using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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

        public static bool IsBoundToAllInterfaces { get; private set; } = false;
        public static List<string> BoundPrefixes { get; } = new();
        
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

        public static async Task RestartAsync(int port)
        {
            Stop();
            _port = port;
            await StartAsync();
        }

        public static async Task StartAsync()
        {
            await Task.Yield();
            if (_isRunning)
            {
                LogService.WriteSystemLog("[FileDownloadService] Server already running", "Warning", "SYSTEM");
                return;
            }

            if (string.IsNullOrEmpty(_username))
            {
                _username = AuthService.CurrentUser?.Username ?? "admin";
            }
            if (string.IsNullOrEmpty(_backupDirectory))
            {
                _backupDirectory = ConfigService.Current.Paths.FtpLocalFolder;
            }
            if (_port <= 0)
            {
                _port = ConfigService.Current.HttpServer.Port > 0 ? ConfigService.Current.HttpServer.Port : 8080;
            }

            try
            {
                _listener = new HttpListener();
                BoundPrefixes.Clear();
                
                // Try to bind to all interfaces first (requires admin or URL reservation)
                try
                {
                    var wildcardPrefix = $"http://+:{_port}/";
                    _listener.Prefixes.Add(wildcardPrefix);
                    _listener.Start();
                    IsBoundToAllInterfaces = true;
                    BoundPrefixes.Add(wildcardPrefix);
                    LogService.WriteSystemLog($"[FileDownloadService] HTTP server started on all interfaces (+), port {_port}", "Information", "SYSTEM");
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 5) // Access denied
                {
                    LogService.WriteSystemLog("[FileDownloadService] Wildcard (+) requires URL reservation. Binding to local network adapters...", "Information", "SYSTEM");
                    _listener = new HttpListener();
                    IsBoundToAllInterfaces = false;

                    var prefixesToAdd = new List<string>
                    {
                        $"http://localhost:{_port}/",
                        $"http://127.0.0.1:{_port}/"
                    };

                    // Discover all active physical local IPv4 addresses (Wi-Fi, Ethernet, etc.)
                    var localIps = GetAllLocalIPv4Addresses();
                    foreach (var ip in localIps)
                    {
                        var p = $"http://{ip}:{_port}/";
                        if (!prefixesToAdd.Contains(p)) prefixesToAdd.Add(p);
                    }

                    foreach (var prefix in prefixesToAdd)
                    {
                        try
                        {
                            _listener.Prefixes.Add(prefix);
                            BoundPrefixes.Add(prefix);
                        }
                        catch (Exception pEx)
                        {
                            LogService.WriteSystemLog($"[FileDownloadService] Prefix {prefix} failed to register: {pEx.Message}", "Warning", "SYSTEM");
                        }
                    }

                    try
                    {
                        _listener.Start();
                        LogService.WriteSystemLog($"[FileDownloadService] HTTP server successfully started on {BoundPrefixes.Count} prefixes on port {_port}", "Information", "SYSTEM");
                    }
                    catch (Exception startEx)
                    {
                        LogService.WriteSystemLog($"[FileDownloadService] Multi-IP listener start failed ({startEx.Message}), falling back to localhost only", "Warning", "SYSTEM");
                        _listener = new HttpListener();
                        BoundPrefixes.Clear();
                        _listener.Prefixes.Add($"http://localhost:{_port}/");
                        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                        BoundPrefixes.Add($"http://localhost:{_port}/");
                        BoundPrefixes.Add($"http://127.0.0.1:{_port}/");
                        _listener.Start();
                    }
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
                    await WebDashboardService.HandleWebRequestAsync(context);
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FileDownloadService] Request handling error: {ex.Message}", "Error", "SYSTEM");
                await SendErrorResponseAsync(response, 500, "Internal Server Error");
            }
        }

        private static async Task HandleFileDownload(HttpListenerContext context, string filename)
        {
            var response = context.Response;
            
            try
            {
                // Security: Sanitize and validate filename to prevent path traversal
                filename = Path.GetFileName(filename);
                if (string.IsNullOrWhiteSpace(filename) || filename.Contains("..") || filename.Contains('/') || filename.Contains('\\'))
                {
                    LogService.WriteSystemLog($"[FileDownloadService] Invalid filename requested: {filename}", "Warning", "SYSTEM");
                    await SendErrorResponseAsync(response, 400, "Invalid filename");
                    return;
                }

                // Search in all backup directories
                var filePath = FindBackupFile(filename);
                
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    LogService.WriteSystemLog($"[FileDownloadService] File not found: {filename}", "Warning", "SYSTEM");
                    await SendErrorResponseAsync(response, 404, "File not found");
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
                await SendErrorResponseAsync(response, 500, "Download failed");
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

        private static async Task SendErrorResponseAsync(HttpListenerResponse response, int statusCode, string message)
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
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
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

        public static async Task RestartAsync()
        {
            Stop();
            await Task.Delay(500);
            await StartAsync();
        }

        public static List<string> GetAllLocalIPv4Addresses()
        {
            var results = new List<string>();
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .OrderByDescending(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                                             ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet);

                foreach (var ni in interfaces)
                {
                    var desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    if (desc.Contains("virtual") || desc.Contains("hyper-v") || desc.Contains("vethernet") ||
                        desc.Contains("wsl") || desc.Contains("vmware") || desc.Contains("virtualbox") ||
                        desc.Contains("pseudo"))
                    {
                        continue;
                    }

                    var ipProps = ni.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr.Address))
                        {
                            var ipStr = addr.Address.ToString();
                            if (!results.Contains(ipStr)) results.Add(ipStr);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[FileDownloadService] Error enumerating network interfaces: {ex.Message}", "Warning", "SYSTEM");
            }

            if (results.Count == 0)
            {
                try
                {
                    var host = Dns.GetHostEntry(Dns.GetHostName());
                    foreach (var ip in host.AddressList)
                    {
                        if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        {
                            var ipStr = ip.ToString();
                            if (!results.Contains(ipStr)) results.Add(ipStr);
                        }
                    }
                }
                catch { }
            }

            return results;
        }

        public static string GetLocalIpAddress()
        {
            var ips = GetAllLocalIPv4Addresses();
            return ips.Count > 0 ? ips[0] : "127.0.0.1";
        }

        /// <summary>
        /// Gets the command to add URL reservation for the HTTP server (requires admin)
        /// </summary>
        public static string GetUrlReservationCommand(int port)
        {
            return $"netsh http add urlacl url=http://+:{port}/ user=Everyone";
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
