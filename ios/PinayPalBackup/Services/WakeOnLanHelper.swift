import Foundation
import Network

public enum WakeOnLanHelper {
    /// Sends a Wake-on-LAN magic packet to wake up a remote PC over local network
    public static func sendMagicPacket(macAddress: String, broadcastIp: String = "255.255.255.255", port: UInt16 = 9) async -> (success: Bool, error: String?) {
        // Clean MAC address (remove colons, dashes, spaces)
        let cleanMac = macAddress.replacingOccurrences(of: ":", with: "")
            .replacingOccurrences(of: "-", with: "")
            .replacingOccurrences(of: " ", with: "")
            .trimmingCharacters(in: .whitespacesAndNewlines)

        guard cleanMac.count == 12 else {
            return (false, "Invalid MAC address format (must be 12 hex digits)")
        }

        var macBytes = [UInt8]()
        for i in stride(from: 0, to: 12, by: 2) {
            let start = cleanMac.index(cleanMac.startIndex, offsetBy: i)
            let end = cleanMac.index(start, offsetBy: 2)
            let byteStr = String(cleanMac[start..<end])
            guard let byte = UInt8(byteStr, radix: 16) else {
                return (false, "Invalid hex digit in MAC address")
            }
            macBytes.append(byte)
        }

        // Construct 102-byte magic packet: 6 bytes 0xFF followed by 16 repetitions of MAC
        var packetData = Data(repeating: 0xFF, count: 6)
        for _ in 0..<16 {
            packetData.append(contentsOf: macBytes)
        }

        // Send via UDP using NWConnection
        guard let nwPort = NWEndpoint.Port(rawValue: port) else {
            return (false, "Invalid UDP port")
        }

        let host = NWEndpoint.Host(broadcastIp)
        let connection = NWConnection(host: host, port: nwPort, using: .udp)

        return await withCheckedContinuation { continuation in
            connection.stateUpdateHandler = { state in
                switch state {
                case .ready:
                    connection.send(content: packetData, completion: .contentProcessed { sendError in
                        connection.cancel()
                        if let err = sendError {
                            continuation.resume(returning: (false, err.localizedDescription))
                        } else {
                            continuation.resume(returning: (true, nil))
                        }
                    })
                case .failed(let error):
                    connection.cancel()
                    continuation.resume(returning: (false, error.localizedDescription))
                case .cancelled:
                    break
                default:
                    break
                }
            }

            connection.start(queue: .global())
        }
    }
}
