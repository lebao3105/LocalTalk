using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shared.Platform;

namespace Shared.Protocol
{
    /// <summary>
    /// Connection lifecycle management with heartbeat mechanisms and graceful termination
    /// </summary>
    public class ConnectionManager
    {
        private static ConnectionManager _instance;
        private readonly ConcurrentDictionary<string, ConnectionContext> _activeConnections;
        private readonly Timer _heartbeatTimer;
        private readonly Timer _cleanupTimer;
        private readonly ConnectionConfiguration _config;
        private readonly object _lock = new object();

        public static ConnectionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ConnectionManager();
                }
                return _instance;
            }
        }

        public event EventHandler<ConnectionEventArgs> ConnectionEstablished;
        public event EventHandler<ConnectionEventArgs> ConnectionLost;
        public event EventHandler<HeartbeatEventArgs> HeartbeatReceived;
        public event EventHandler<ConnectionEventArgs> ConnectionTerminated;

        private ConnectionManager()
        {
            _activeConnections = new ConcurrentDictionary<string, ConnectionContext>();
            _config = new ConnectionConfiguration();
            
            // Initialize heartbeat timer
            _heartbeatTimer = new Timer(SendHeartbeats, null, 
                _config.HeartbeatInterval, _config.HeartbeatInterval);
            
            // Initialize cleanup timer
            _cleanupTimer = new Timer(CleanupStaleConnections, null,
                _config.CleanupInterval, _config.CleanupInterval);
        }

        /// <summary>
        /// Establishes a new connection
        /// </summary>
        public async Task<ConnectionResult> EstablishConnectionAsync(ConnectionRequest request)
        {
            var result = new ConnectionResult
            {
                ConnectionId = Guid.NewGuid().ToString(),
                RemoteEndpoint = request.RemoteEndpoint,
                RequestedAt = DateTime.Now
            };

            try
            {
                // Create connection context
                var context = new ConnectionContext
                {
                    ConnectionId = result.ConnectionId,
                    RemoteEndpoint = request.RemoteEndpoint,
                    LocalEndpoint = request.LocalEndpoint,
                    ConnectionType = request.ConnectionType,
                    State = ConnectionState.Connecting,
                    EstablishedAt = DateTime.Now,
                    LastHeartbeat = DateTime.Now,
                    LastActivity = DateTime.Now,
                    Metadata = request.Metadata ?? new Dictionary<string, string>()
                };

                // Perform connection handshake
                var handshakeResult = await PerformHandshakeAsync(context, request);
                if (!handshakeResult.Success)
                {
                    result.Success = false;
                    result.ErrorMessage = handshakeResult.ErrorMessage;
                    return result;
                }

                // Add to active connections
                _activeConnections[result.ConnectionId] = context;
                context.State = ConnectionState.Connected;

                result.Success = true;
                result.LocalEndpoint = context.LocalEndpoint;

                OnConnectionEstablished(new ConnectionEventArgs
                {
                    ConnectionId = result.ConnectionId,
                    RemoteEndpoint = context.RemoteEndpoint,
                    LocalEndpoint = context.LocalEndpoint,
                    ConnectionType = context.ConnectionType
                });

                System.Diagnostics.Debug.WriteLine($"Connection established: {result.ConnectionId} to {request.RemoteEndpoint}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Connection failed: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Connection establishment error: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Terminates a connection gracefully
        /// </summary>
        public async Task<bool> TerminateConnectionAsync(string connectionId, string reason = "User requested")
        {
            if (!_activeConnections.TryGetValue(connectionId, out var context))
                return false;

            try
            {
                context.State = ConnectionState.Terminating;
                
                // Send termination notice
                await SendTerminationNoticeAsync(context, reason);
                
                // Cleanup resources
                await CleanupConnectionAsync(context);
                
                // Remove from active connections
                _activeConnections.TryRemove(connectionId, out _);
                
                OnConnectionTerminated(new ConnectionEventArgs
                {
                    ConnectionId = connectionId,
                    RemoteEndpoint = context.RemoteEndpoint,
                    LocalEndpoint = context.LocalEndpoint,
                    ConnectionType = context.ConnectionType,
                    Reason = reason
                });

                System.Diagnostics.Debug.WriteLine($"Connection terminated: {connectionId} - {reason}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error terminating connection {connectionId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Processes incoming heartbeat
        /// </summary>
        public async Task<bool> ProcessHeartbeatAsync(string connectionId, HeartbeatMessage heartbeat)
        {
            if (!_activeConnections.TryGetValue(connectionId, out var context))
                return false;

            try
            {
                context.LastHeartbeat = DateTime.Now;
                context.LastActivity = DateTime.Now;
                context.HeartbeatCount++;

                // Update connection statistics
                if (heartbeat.Timestamp.HasValue)
                {
                    var latency = DateTime.Now - heartbeat.Timestamp.Value;
                    context.UpdateLatency(latency);
                }

                OnHeartbeatReceived(new HeartbeatEventArgs
                {
                    ConnectionId = connectionId,
                    Heartbeat = heartbeat,
                    Latency = context.AverageLatency
                });

                // Send heartbeat response if required
                if (heartbeat.RequiresResponse)
                {
                    await SendHeartbeatResponseAsync(context, heartbeat);
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing heartbeat for {connectionId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets connection information
        /// </summary>
        public ConnectionInfo GetConnectionInfo(string connectionId)
        {
            if (!_activeConnections.TryGetValue(connectionId, out var context))
                return null;

            return new ConnectionInfo
            {
                ConnectionId = context.ConnectionId,
                RemoteEndpoint = context.RemoteEndpoint,
                LocalEndpoint = context.LocalEndpoint,
                ConnectionType = context.ConnectionType,
                State = context.State,
                EstablishedAt = context.EstablishedAt,
                LastHeartbeat = context.LastHeartbeat,
                LastActivity = context.LastActivity,
                HeartbeatCount = context.HeartbeatCount,
                AverageLatency = context.AverageLatency,
                IsHealthy = IsConnectionHealthy(context),
                Metadata = new Dictionary<string, string>(context.Metadata)
            };
        }

        /// <summary>
        /// Gets all active connections
        /// </summary>
        public List<ConnectionInfo> GetActiveConnections()
        {
            return _activeConnections.Values
                .Select(context => GetConnectionInfo(context.ConnectionId))
                .Where(info => info != null)
                .ToList();
        }

        /// <summary>
        /// Updates connection activity timestamp
        /// </summary>
        public void UpdateConnectionActivity(string connectionId)
        {
            if (_activeConnections.TryGetValue(connectionId, out var context))
            {
                context.LastActivity = DateTime.Now;
            }
        }

        /// <summary>
        /// Checks if a connection is healthy
        /// </summary>
        public bool IsConnectionHealthy(string connectionId)
        {
            if (!_activeConnections.TryGetValue(connectionId, out var context))
                return false;

            return IsConnectionHealthy(context);
        }

        /// <summary>
        /// Performs connection handshake
        /// </summary>
        private async Task<HandshakeResult> PerformHandshakeAsync(ConnectionContext context, ConnectionRequest request)
        {
            var result = new HandshakeResult();

            try
            {
                // This is a simplified handshake implementation
                // In a real implementation, this would involve:
                // 1. Protocol version negotiation
                // 2. Authentication/authorization
                // 3. Capability exchange
                // 4. Security parameter negotiation

                await Task.Delay(100); // Simulate handshake time

                result.Success = true;
                result.NegotiatedVersion = "1.0";
                result.SecurityLevel = "Standard";

                System.Diagnostics.Debug.WriteLine($"Handshake completed for {context.ConnectionId}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Sends heartbeats to all active connections
        /// </summary>
        private async void SendHeartbeats(object state)
        {
            var connections = _activeConnections.Values.ToList();
            
            foreach (var context in connections)
            {
                if (context.State == ConnectionState.Connected)
                {
                    try
                    {
                        await SendHeartbeatAsync(context);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error sending heartbeat to {context.ConnectionId}: {ex.Message}");
                        
                        // Mark connection as potentially lost
                        context.State = ConnectionState.Disconnected;
                        OnConnectionLost(new ConnectionEventArgs
                        {
                            ConnectionId = context.ConnectionId,
                            RemoteEndpoint = context.RemoteEndpoint,
                            LocalEndpoint = context.LocalEndpoint,
                            ConnectionType = context.ConnectionType,
                            Reason = "Heartbeat failed"
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Sends a heartbeat to a specific connection
        /// </summary>
        private async Task SendHeartbeatAsync(ConnectionContext context)
        {
            var heartbeat = new HeartbeatMessage
            {
                Timestamp = DateTime.Now,
                SequenceNumber = context.HeartbeatCount + 1,
                RequiresResponse = true,
                ConnectionId = context.ConnectionId
            };

            // This is a placeholder - would use actual transport layer
            await Task.Delay(10); // Simulate network operation
            
            context.LastHeartbeatSent = DateTime.Now;
        }

        /// <summary>
        /// Sends heartbeat response
        /// </summary>
        private async Task SendHeartbeatResponseAsync(ConnectionContext context, HeartbeatMessage originalHeartbeat)
        {
            var response = new HeartbeatMessage
            {
                Timestamp = DateTime.Now,
                SequenceNumber = originalHeartbeat.SequenceNumber,
                RequiresResponse = false,
                ConnectionId = context.ConnectionId,
                IsResponse = true
            };

            // This is a placeholder - would use actual transport layer
            await Task.Delay(5); // Simulate network operation
        }

        /// <summary>
        /// Sends termination notice
        /// </summary>
        private async Task SendTerminationNoticeAsync(ConnectionContext context, string reason)
        {
            var notice = new TerminationNotice
            {
                ConnectionId = context.ConnectionId,
                Reason = reason,
                Timestamp = DateTime.Now
            };

            // This is a placeholder - would use actual transport layer
            await Task.Delay(10); // Simulate network operation
        }

        /// <summary>
        /// Cleans up stale connections
        /// </summary>
        private async void CleanupStaleConnections(object state)
        {
            var now = DateTime.Now;
            var staleConnections = new List<string>();

            foreach (var kvp in _activeConnections)
            {
                var context = kvp.Value;
                
                // Check if connection is stale
                if (now - context.LastHeartbeat > _config.ConnectionTimeout ||
                    now - context.LastActivity > _config.InactivityTimeout)
                {
                    staleConnections.Add(kvp.Key);
                }
            }

            // Remove stale connections
            foreach (var connectionId in staleConnections)
            {
                await TerminateConnectionAsync(connectionId, "Connection timeout");
            }

            if (staleConnections.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Cleaned up {staleConnections.Count} stale connections");
            }
        }

        /// <summary>
        /// Cleans up connection resources
        /// </summary>
        private async Task CleanupConnectionAsync(ConnectionContext context)
        {
            await Task.Run(() =>
            {
                // Cleanup any connection-specific resources
                context.Dispose();
            });
        }

        /// <summary>
        /// Checks if a connection context is healthy
        /// </summary>
        private bool IsConnectionHealthy(ConnectionContext context)
        {
            var now = DateTime.Now;
            
            return context.State == ConnectionState.Connected &&
                   now - context.LastHeartbeat <= _config.HeartbeatTimeout &&
                   now - context.LastActivity <= _config.InactivityTimeout;
        }

        /// <summary>
        /// Raises the ConnectionEstablished event
        /// </summary>
        private void OnConnectionEstablished(ConnectionEventArgs args)
        {
            ConnectionEstablished?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the ConnectionLost event
        /// </summary>
        private void OnConnectionLost(ConnectionEventArgs args)
        {
            ConnectionLost?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the HeartbeatReceived event
        /// </summary>
        private void OnHeartbeatReceived(HeartbeatEventArgs args)
        {
            HeartbeatReceived?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the ConnectionTerminated event
        /// </summary>
        private void OnConnectionTerminated(ConnectionEventArgs args)
        {
            ConnectionTerminated?.Invoke(this, args);
        }

        /// <summary>
        /// Disposes the connection manager
        /// </summary>
        public void Dispose()
        {
            _heartbeatTimer?.Dispose();
            _cleanupTimer?.Dispose();

            // Terminate all active connections
            var connections = _activeConnections.Keys.ToList();
            foreach (var connectionId in connections)
            {
                TerminateConnectionAsync(connectionId, "System shutdown").Wait();
            }
        }
    }

    /// <summary>
    /// Connection request information
    /// </summary>
    public class ConnectionRequest
    {
        public string RemoteEndpoint { get; set; }
        public string LocalEndpoint { get; set; }
        public ConnectionType ConnectionType { get; set; }
        public Dictionary<string, string> Metadata { get; set; }
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Connection result
    /// </summary>
    public class ConnectionResult
    {
        public bool Success { get; set; }
        public string ConnectionId { get; set; }
        public string RemoteEndpoint { get; set; }
        public string LocalEndpoint { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime RequestedAt { get; set; }
        public DateTime EstablishedAt { get; set; }
    }

    /// <summary>
    /// Connection context for internal management
    /// </summary>
    internal class ConnectionContext : IDisposable
    {
        public string ConnectionId { get; set; }
        public string RemoteEndpoint { get; set; }
        public string LocalEndpoint { get; set; }
        public ConnectionType ConnectionType { get; set; }
        public ConnectionState State { get; set; }
        public DateTime EstablishedAt { get; set; }
        public DateTime LastHeartbeat { get; set; }
        public DateTime LastHeartbeatSent { get; set; }
        public DateTime LastActivity { get; set; }
        public int HeartbeatCount { get; set; }
        public Dictionary<string, string> Metadata { get; set; }

        private readonly List<TimeSpan> _latencyHistory = new List<TimeSpan>();
        private readonly object _latencyLock = new object();

        public TimeSpan AverageLatency
        {
            get
            {
                lock (_latencyLock)
                {
                    if (_latencyHistory.Count == 0)
                        return TimeSpan.Zero;

                    var totalTicks = _latencyHistory.Sum(l => l.Ticks);
                    return new TimeSpan(totalTicks / _latencyHistory.Count);
                }
            }
        }

        public void UpdateLatency(TimeSpan latency)
        {
            lock (_latencyLock)
            {
                _latencyHistory.Add(latency);

                // Keep only last 10 measurements
                if (_latencyHistory.Count > 10)
                {
                    _latencyHistory.RemoveAt(0);
                }
            }
        }

        public void Dispose()
        {
            // Cleanup any connection-specific resources
            Metadata?.Clear();
            _latencyHistory.Clear();
        }
    }

    /// <summary>
    /// Connection information for external consumption
    /// </summary>
    public class ConnectionInfo
    {
        public string ConnectionId { get; set; }
        public string RemoteEndpoint { get; set; }
        public string LocalEndpoint { get; set; }
        public ConnectionType ConnectionType { get; set; }
        public ConnectionState State { get; set; }
        public DateTime EstablishedAt { get; set; }
        public DateTime LastHeartbeat { get; set; }
        public DateTime LastActivity { get; set; }
        public int HeartbeatCount { get; set; }
        public TimeSpan AverageLatency { get; set; }
        public bool IsHealthy { get; set; }
        public Dictionary<string, string> Metadata { get; set; }
        public TimeSpan Duration => DateTime.Now - EstablishedAt;
    }

    /// <summary>
    /// Connection configuration
    /// </summary>
    public class ConnectionConfiguration
    {
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(90);
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromMinutes(10);
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);
        public int MaxConcurrentConnections { get; set; } = 100;
        public bool EnableHeartbeat { get; set; } = true;
        public bool EnableAutoReconnect { get; set; } = true;
    }

    /// <summary>
    /// Handshake result
    /// </summary>
    internal class HandshakeResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string NegotiatedVersion { get; set; }
        public string SecurityLevel { get; set; }
    }

    /// <summary>
    /// Heartbeat message
    /// </summary>
    public class HeartbeatMessage
    {
        public DateTime? Timestamp { get; set; }
        public int SequenceNumber { get; set; }
        public bool RequiresResponse { get; set; }
        public string ConnectionId { get; set; }
        public bool IsResponse { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Termination notice
    /// </summary>
    internal class TerminationNotice
    {
        public string ConnectionId { get; set; }
        public string Reason { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Connection types
    /// </summary>
    public enum ConnectionType
    {
        FileTransfer,
        Discovery,
        Control,
        Data
    }

    /// <summary>
    /// Connection states
    /// </summary>
    public enum ConnectionState
    {
        Connecting,
        Connected,
        Disconnected,
        Terminating,
        Failed
    }

    /// <summary>
    /// Connection event arguments
    /// </summary>
    public class ConnectionEventArgs : EventArgs
    {
        public string ConnectionId { get; set; }
        public string RemoteEndpoint { get; set; }
        public string LocalEndpoint { get; set; }
        public ConnectionType ConnectionType { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Heartbeat event arguments
    /// </summary>
    public class HeartbeatEventArgs : EventArgs
    {
        public string ConnectionId { get; set; }
        public HeartbeatMessage Heartbeat { get; set; }
        public TimeSpan Latency { get; set; }
    }
}
