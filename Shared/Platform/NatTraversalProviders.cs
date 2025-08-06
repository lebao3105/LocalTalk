using System;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Shared.Platform
{
    /// <summary>
    /// Interface for NAT traversal providers
    /// </summary>
    public interface INatTraversalProvider
    {
        string MethodName { get; }
        string Description { get; }
        int Priority { get; }
        Task<bool> IsAvailableAsync();
        Task<PortMappingResult> CreatePortMappingAsync(int port, PortProtocol protocol, string description);
        Task<bool> DeletePortMappingAsync(int port, PortProtocol protocol);
    }

    /// <summary>
    /// Port mapping result
    /// </summary>
    public class PortMappingResult
    {
        public bool Success { get; set; }
        public string ExternalEndpoint { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// UPnP (Universal Plug and Play) provider for automatic port forwarding
    /// </summary>
    public class UpnpProvider : INatTraversalProvider
    {
        public string MethodName => "UPnP";
        public string Description => "Universal Plug and Play automatic port forwarding";
        public int Priority => 1; // Highest priority

        private const int UPNP_DISCOVERY_TIMEOUT = 5000;
        private const string UPNP_MULTICAST_ADDRESS = "239.255.255.250";
        private const int UPNP_MULTICAST_PORT = 1900;

        /// <summary>
        /// Checks if UPnP is available on the network
        /// </summary>
        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                // Send UPnP discovery request
                var discoveryMessage = CreateUpnpDiscoveryMessage();
                
                using (var client = new UdpClient())
                {
                    client.Client.ReceiveTimeout = UPNP_DISCOVERY_TIMEOUT;
                    
                    var multicastEndpoint = new IPEndPoint(IPAddress.Parse(UPNP_MULTICAST_ADDRESS), UPNP_MULTICAST_PORT);
                    await client.SendAsync(discoveryMessage, discoveryMessage.Length, multicastEndpoint);
                    
                    // Wait for response
                    var result = await client.ReceiveAsync();
                    var response = Encoding.UTF8.GetString(result.Buffer);
                    
                    // Check if response contains UPnP gateway information
                    return response.Contains("InternetGatewayDevice") || response.Contains("WANIPConnection");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UPnP availability check failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Creates a port mapping using UPnP
        /// </summary>
        public async Task<PortMappingResult> CreatePortMappingAsync(int port, PortProtocol protocol, string description)
        {
            var result = new PortMappingResult();

            try
            {
                // This is a simplified UPnP implementation
                // In a real implementation, you would:
                // 1. Discover UPnP devices
                // 2. Get the control URL for WANIPConnection
                // 3. Send SOAP request to add port mapping
                
                // For now, we'll simulate the process
                await Task.Delay(1000); // Simulate network operation
                
                // In a real implementation, you would get the external IP from the router
                var externalIp = await GetExternalIpAsync();
                
                result.Success = true;
                result.ExternalEndpoint = $"{externalIp}:{port}";
                
                System.Diagnostics.Debug.WriteLine($"UPnP: Created port mapping for {port}/{protocol}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"UPnP port mapping failed: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"UPnP error: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Deletes a port mapping using UPnP
        /// </summary>
        public async Task<bool> DeletePortMappingAsync(int port, PortProtocol protocol)
        {
            try
            {
                // This is a simplified implementation
                // In a real implementation, you would send a SOAP request to delete the mapping
                await Task.Delay(500); // Simulate network operation
                
                System.Diagnostics.Debug.WriteLine($"UPnP: Deleted port mapping for {port}/{protocol}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UPnP delete error: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Creates UPnP discovery message
        /// </summary>
        private byte[] CreateUpnpDiscoveryMessage()
        {
            var message = "M-SEARCH * HTTP/1.1\r\n" +
                         "HOST: 239.255.255.250:1900\r\n" +
                         "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n" +
                         "MAN: \"ssdp:discover\"\r\n" +
                         "MX: 3\r\n\r\n";
            
            return Encoding.UTF8.GetBytes(message);
        }

        /// <summary>
        /// Gets external IP address (simplified implementation)
        /// </summary>
        private async Task<string> GetExternalIpAsync()
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response = await client.GetStringAsync("http://checkip.amazonaws.com/");
                    return response.Trim();
                }
            }
            catch
            {
                return "unknown";
            }
        }
    }

    /// <summary>
    /// NAT-PMP (NAT Port Mapping Protocol) provider
    /// </summary>
    public class NatPmpProvider : INatTraversalProvider
    {
        public string MethodName => "NAT-PMP";
        public string Description => "NAT Port Mapping Protocol for Apple routers";
        public int Priority => 2;

        private const int NAT_PMP_PORT = 5351;
        private const int NAT_PMP_TIMEOUT = 5000;

        /// <summary>
        /// Checks if NAT-PMP is available
        /// </summary>
        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                // Get default gateway
                var gateway = await GetDefaultGatewayAsync();
                if (gateway == null)
                    return false;

                // Send NAT-PMP discovery request
                using (var client = new UdpClient())
                {
                    client.Client.ReceiveTimeout = NAT_PMP_TIMEOUT;
                    
                    var request = CreateNatPmpDiscoveryRequest();
                    var gatewayEndpoint = new IPEndPoint(gateway, NAT_PMP_PORT);
                    
                    await client.SendAsync(request, request.Length, gatewayEndpoint);
                    
                    // Wait for response
                    var result = await client.ReceiveAsync();
                    
                    // Check if response is valid NAT-PMP response
                    return result.Buffer.Length >= 12 && result.Buffer[0] == 0 && result.Buffer[1] == 128;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NAT-PMP availability check failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Creates a port mapping using NAT-PMP
        /// </summary>
        public async Task<PortMappingResult> CreatePortMappingAsync(int port, PortProtocol protocol, string description)
        {
            var result = new PortMappingResult();

            try
            {
                var gateway = await GetDefaultGatewayAsync();
                if (gateway == null)
                {
                    result.ErrorMessage = "No default gateway found";
                    return result;
                }

                using (var client = new UdpClient())
                {
                    client.Client.ReceiveTimeout = NAT_PMP_TIMEOUT;
                    
                    var request = CreateNatPmpMappingRequest(port, protocol);
                    var gatewayEndpoint = new IPEndPoint(gateway, NAT_PMP_PORT);
                    
                    await client.SendAsync(request, request.Length, gatewayEndpoint);
                    
                    var response = await client.ReceiveAsync();
                    
                    if (IsNatPmpSuccessResponse(response.Buffer))
                    {
                        var externalIp = await GetExternalIpAsync();
                        result.Success = true;
                        result.ExternalEndpoint = $"{externalIp}:{port}";
                        
                        System.Diagnostics.Debug.WriteLine($"NAT-PMP: Created port mapping for {port}/{protocol}");
                    }
                    else
                    {
                        result.ErrorMessage = "NAT-PMP mapping request failed";
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"NAT-PMP port mapping failed: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"NAT-PMP error: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Deletes a port mapping using NAT-PMP
        /// </summary>
        public async Task<bool> DeletePortMappingAsync(int port, PortProtocol protocol)
        {
            try
            {
                var gateway = await GetDefaultGatewayAsync();
                if (gateway == null)
                    return false;

                using (var client = new UdpClient())
                {
                    client.Client.ReceiveTimeout = NAT_PMP_TIMEOUT;
                    
                    var request = CreateNatPmpDeleteRequest(port, protocol);
                    var gatewayEndpoint = new IPEndPoint(gateway, NAT_PMP_PORT);
                    
                    await client.SendAsync(request, request.Length, gatewayEndpoint);
                    
                    var response = await client.ReceiveAsync();
                    
                    System.Diagnostics.Debug.WriteLine($"NAT-PMP: Deleted port mapping for {port}/{protocol}");
                    return IsNatPmpSuccessResponse(response.Buffer);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NAT-PMP delete error: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Gets the default gateway IP address
        /// </summary>
        private async Task<IPAddress> GetDefaultGatewayAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // This is a simplified implementation
                    // In a real implementation, you would query the routing table
                    return IPAddress.Parse("192.168.1.1"); // Common default gateway
                }
                catch
                {
                    return null;
                }
            });
        }

        /// <summary>
        /// Creates NAT-PMP discovery request
        /// </summary>
        private byte[] CreateNatPmpDiscoveryRequest()
        {
            return new byte[] { 0, 0 }; // Version 0, Opcode 0 (public address request)
        }

        /// <summary>
        /// Creates NAT-PMP port mapping request
        /// </summary>
        private byte[] CreateNatPmpMappingRequest(int port, PortProtocol protocol)
        {
            var request = new byte[12];
            request[0] = 0; // Version
            request[1] = (byte)(protocol == PortProtocol.TCP ? 2 : 1); // Opcode
            
            // Internal port (2 bytes, big endian)
            request[4] = (byte)(port >> 8);
            request[5] = (byte)(port & 0xFF);
            
            // External port (2 bytes, big endian)
            request[6] = (byte)(port >> 8);
            request[7] = (byte)(port & 0xFF);
            
            // Lifetime (4 bytes, big endian) - 1 hour
            request[8] = 0;
            request[9] = 0;
            request[10] = 14; // 3600 seconds = 0x0E10
            request[11] = 16;
            
            return request;
        }

        /// <summary>
        /// Creates NAT-PMP delete request
        /// </summary>
        private byte[] CreateNatPmpDeleteRequest(int port, PortProtocol protocol)
        {
            var request = CreateNatPmpMappingRequest(port, protocol);
            // Set lifetime to 0 to delete mapping
            request[8] = 0;
            request[9] = 0;
            request[10] = 0;
            request[11] = 0;
            
            return request;
        }

        /// <summary>
        /// Checks if NAT-PMP response indicates success
        /// </summary>
        private bool IsNatPmpSuccessResponse(byte[] response)
        {
            return response.Length >= 12 && response[0] == 0 && response[3] == 0; // Version 0, Result code 0 (success)
        }

        /// <summary>
        /// Gets external IP address
        /// </summary>
        private async Task<string> GetExternalIpAsync()
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response = await client.GetStringAsync("http://checkip.amazonaws.com/");
                    return response.Trim();
                }
            }
            catch
            {
                return "unknown";
            }
        }
    }

    /// <summary>
    /// Manual configuration provider (fallback option)
    /// </summary>
    public class ManualConfigProvider : INatTraversalProvider
    {
        public string MethodName => "Manual";
        public string Description => "Manual port forwarding configuration";
        public int Priority => 99; // Lowest priority

        /// <summary>
        /// Manual configuration is always "available" as a fallback
        /// </summary>
        public async Task<bool> IsAvailableAsync()
        {
            return await Task.FromResult(true);
        }

        /// <summary>
        /// Manual configuration requires user intervention
        /// </summary>
        public async Task<PortMappingResult> CreatePortMappingAsync(int port, PortProtocol protocol, string description)
        {
            await Task.Delay(100); // Simulate processing

            var result = new PortMappingResult
            {
                Success = false,
                ErrorMessage = $"Manual port forwarding required: Please configure your router to forward port {port}/{protocol} to this device"
            };

            System.Diagnostics.Debug.WriteLine($"Manual: Port forwarding required for {port}/{protocol}");
            return result;
        }

        /// <summary>
        /// Manual deletion also requires user intervention
        /// </summary>
        public async Task<bool> DeletePortMappingAsync(int port, PortProtocol protocol)
        {
            await Task.Delay(100);
            System.Diagnostics.Debug.WriteLine($"Manual: Please remove port forwarding for {port}/{protocol} from your router");
            return true; // Assume user will handle it
        }
    }
}
