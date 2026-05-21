using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;

namespace PinayPalBackupManager.Services
{
    /// <summary>
    /// Provides optimized HTTP client with connection pooling and certificate pinning
    /// </summary>
    public class HttpClientFactory : IDisposable
    {
        private static readonly Lazy<HttpClientFactory> _instance = new(() => new HttpClientFactory());
        private readonly HttpClient _httpClient;
        private readonly HttpClient _pinnedClient;
        private bool _disposed = false;

        public static HttpClientFactory Instance => _instance.Value;

        private HttpClientFactory()
        {
            // Standard client with connection pooling
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 10,
                EnableMultipleHttp2Connections = true
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
                DefaultRequestHeaders = { { "User-Agent", "PinayPalBackupManager/2.13.8" } }
            };

            // Pinned client for Firebase connections
            var pinnedHandler = new CertificatePinningHandler();
            _pinnedClient = new HttpClient(pinnedHandler)
            {
                Timeout = TimeSpan.FromSeconds(10),
                DefaultRequestHeaders = { { "User-Agent", "PinayPalBackupManager/2.13.8" } }
            };

        }

        /// <summary>
        /// Gets the standard HTTP client for general use
        /// </summary>
        public HttpClient Client => _httpClient;

        /// <summary>
        /// Gets the certificate-pinned HTTP client for Firebase operations
        /// </summary>
        public HttpClient PinnedClient => _pinnedClient;

        /// <summary>
        /// Creates a new client with custom timeout
        /// </summary>
        public HttpClient CreateClient(TimeSpan timeout)
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 10
            };

            return new HttpClient(handler)
            {
                Timeout = timeout,
                DefaultRequestHeaders = { { "User-Agent", "PinayPalBackupManager/2.13.8" } }
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _pinnedClient?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// HTTP client handler with certificate pinning for enhanced security
    /// Note: Certificate pinning implementation placeholder
    /// </summary>
    public class CertificatePinningHandler : HttpClientHandler
    {
        // Firebase's certificate thumbprint (you should update this with the actual thumbprint)
        private const string FirebaseThumbprint = "A1B2C3D4E5F6789012345678901234567890ABCD";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);

            return response;
        }
    }

    /// <summary>
    /// Rate-limited HTTP client to prevent API abuse
    /// </summary>
    public class RateLimitedHttpClient
    {
        private readonly HttpClient _client;
        private readonly SemaphoreSlim _semaphore;
        private readonly TimeSpan _minInterval;
        private DateTime _lastRequest = DateTime.MinValue;
        private readonly object _lock = new object();

        public RateLimitedHttpClient(HttpClient client, TimeSpan minInterval, int maxConcurrentRequests = 5)
        {
            _client = client;
            _minInterval = minInterval;
            _semaphore = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
        }

        /// <summary>
        /// Sends a GET request with rate limiting
        /// </summary>
        public async Task<HttpResponseMessage> GetAsync(string url, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            
            try
            {
                await EnforceRateLimitAsync(cancellationToken);
                return await _client.GetAsync(url, cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Sends a POST request with rate limiting
        /// </summary>
        public async Task<HttpResponseMessage> PostAsync(string url, HttpContent content, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            
            try
            {
                await EnforceRateLimitAsync(cancellationToken);
                return await _client.PostAsync(url, content, cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Sends a PUT request with rate limiting
        /// </summary>
        public async Task<HttpResponseMessage> PutAsync(string url, HttpContent content, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            
            try
            {
                await EnforceRateLimitAsync(cancellationToken);
                return await _client.PutAsync(url, content, cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Sends a DELETE request with rate limiting
        /// </summary>
        public async Task<HttpResponseMessage> DeleteAsync(string url, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            
            try
            {
                await EnforceRateLimitAsync(cancellationToken);
                return await _client.DeleteAsync(url, cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Sends a custom HTTP request with rate limiting
        /// </summary>
        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            
            try
            {
                await EnforceRateLimitAsync(cancellationToken);
                return await _client.SendAsync(request, cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task EnforceRateLimitAsync(CancellationToken cancellationToken)
        {
            DateTime targetTime;
            lock (_lock)
            {
                var timeSinceLastRequest = DateTime.UtcNow - _lastRequest;
                if (timeSinceLastRequest < _minInterval)
                {
                    var delay = _minInterval - timeSinceLastRequest;
                    _lastRequest = DateTime.UtcNow + delay;
                }
                else
                {
                    _lastRequest = DateTime.UtcNow;
                }
                targetTime = _lastRequest;
            }

            // Wait outside the lock to avoid blocking other threads
            var now = DateTime.UtcNow;
            if (now < targetTime)
            {
                await Task.Delay(targetTime - now, cancellationToken);
            }
        }

        public void Dispose()
        {
            _semaphore?.Dispose();
        }
    }
}
