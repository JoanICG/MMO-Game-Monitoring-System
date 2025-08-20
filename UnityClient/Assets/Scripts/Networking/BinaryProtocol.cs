using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class BinaryProtocol
{
    // Message types
    public const byte MSG_STATE_UPDATE = 1;
    public const byte MSG_PLAYER_JOIN = 2;
    public const byte MSG_PLAYER_LEAVE = 3;
    public const byte MSG_HEARTBEAT = 4;

    // Pack state update into binary format
    public static byte[] PackStateUpdate(Dictionary<Guid, UdpNetworkClient.RemotePlayer> players)
    {
        var buffer = new List<byte>();
        
        // Message type
        buffer.Add(MSG_STATE_UPDATE);
        
        // Player count (4 bytes)
        var count = players.Count;
        buffer.AddRange(BitConverter.GetBytes(count));
        
        foreach (var player in players.Values)
        {
            // Player ID (16 bytes)
            buffer.AddRange(player.id.ToByteArray());
            
            // Position (12 bytes: 3 floats)
            buffer.AddRange(BitConverter.GetBytes(player.targetPos.x));
            buffer.AddRange(BitConverter.GetBytes(player.targetPos.y));
            buffer.AddRange(BitConverter.GetBytes(player.targetPos.z));
            
            // Is NPC flag (1 byte)
            buffer.Add((byte)(player.name.StartsWith("Bot_") ? 1 : 0));
            
            // Name length + name (variable)
            var nameBytes = Encoding.UTF8.GetBytes(player.name);
            buffer.Add((byte)nameBytes.Length);
            buffer.AddRange(nameBytes);
        }
        
        return buffer.ToArray();
    }

    // Unpack state update from binary format
    public static List<PlayerData> UnpackStateUpdate(byte[] data)
    {
        var players = new List<PlayerData>();
        int offset = 1; // Skip message type
        
        // Read player count
        var count = BitConverter.ToInt32(data, offset);
        offset += 4;
        
        for (int i = 0; i < count; i++)
        {
            var player = new PlayerData();
            
            // Read player ID
            var guidBytes = new byte[16];
            Array.Copy(data, offset, guidBytes, 0, 16);
            player.id = new Guid(guidBytes);
            offset += 16;
            
            // Read position
            player.x = BitConverter.ToSingle(data, offset);
            offset += 4;
            player.y = BitConverter.ToSingle(data, offset);
            offset += 4;
            player.z = BitConverter.ToSingle(data, offset);
            offset += 4;
            
            // Read NPC flag
            player.isNPC = data[offset] == 1;
            offset += 1;
            
            // Read name
            var nameLength = data[offset];
            offset += 1;
            player.name = Encoding.UTF8.GetString(data, offset, nameLength);
            offset += nameLength;
            
            players.Add(player);
        }
        
        return players;
    }

    public struct PlayerData
    {
        public Guid id;
        public string name;
        public float x, y, z;
        public bool isNPC;
    }
}
