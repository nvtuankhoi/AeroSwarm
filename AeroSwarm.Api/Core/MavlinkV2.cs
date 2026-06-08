using System.Buffers.Binary;

namespace AeroSwarm.Api.Core;

/// <summary>
/// MAVLink v2 frame encode/decode with proper X.25 CRC including CRC_EXTRA seed byte.
/// Common dialect only. No signature (incompat_flags = 0).
/// </summary>
public static class MavlinkV2
{
    public const byte MagicV2 = 0xFD;
    public const byte HeaderLen = 10;
    public const byte GcsSysId = 255;
    public const byte GcsCompId = 190;
    public const byte AutopilotCompId = 1;

    // Message IDs
    public const uint MSG_HEARTBEAT = 0;
    public const uint MSG_SYS_STATUS = 1;
    public const uint MSG_SET_MODE = 11;
    public const uint MSG_GPS_RAW_INT = 24;
    public const uint MSG_GLOBAL_POSITION_INT = 33;
    public const uint MSG_MISSION_COUNT = 44;
    public const uint MSG_MISSION_CLEAR_ALL = 45;
    public const uint MSG_MISSION_ACK = 47;
    public const uint MSG_MISSION_REQUEST_INT = 51;
    public const uint MSG_MISSION_ITEM_INT = 73;
    public const uint MSG_SET_POSITION_TARGET_GLOBAL_INT = 86;
    public const uint MSG_COMMAND_INT = 75;
    public const uint MSG_COMMAND_LONG = 76;
    public const uint MSG_COMMAND_ACK = 77;
    public const uint MSG_BATTERY_STATUS = 147;
    public const uint MSG_REQUEST_DATA_STREAM = 66;
    public const uint MSG_MESSAGE_INTERVAL = 244;
    public const uint MSG_SET_MESSAGE_INTERVAL = 511;
    public const uint MSG_STATUSTEXT = 253;

    // MAV_CMD constants
    public const ushort CMD_NAV_WAYPOINT = 16;
    public const ushort CMD_NAV_RETURN_TO_LAUNCH = 20;
    public const ushort CMD_NAV_LAND = 21;
    public const ushort CMD_NAV_TAKEOFF = 22;
    public const ushort CMD_DO_SET_HOME = 179;
    public const ushort CMD_COMPONENT_ARM_DISARM = 400;

    // CRC_EXTRA seed bytes per common dialect message id.
    // Source: mavlink/c_library_v2 dialect headers.
    private static readonly Dictionary<uint, byte> CrcExtra = new()
    {
        { MSG_HEARTBEAT, 50 },
        { MSG_SYS_STATUS, 124 },
        { MSG_SET_MODE, 89 },
        { MSG_GPS_RAW_INT, 24 },
        { MSG_GLOBAL_POSITION_INT, 104 },
        { MSG_MISSION_COUNT, 221 },
        { MSG_MISSION_CLEAR_ALL, 232 },
        { MSG_MISSION_ACK, 153 },
        { MSG_MISSION_REQUEST_INT, 38 },
        { MSG_MISSION_ITEM_INT, 38 },
        { MSG_SET_POSITION_TARGET_GLOBAL_INT, 5 },
        { MSG_COMMAND_INT, 158 },
        { MSG_COMMAND_LONG, 152 },
        { MSG_COMMAND_ACK, 143 },
        { MSG_BATTERY_STATUS, 154 },
        { MSG_REQUEST_DATA_STREAM, 148 },
        { MSG_MESSAGE_INTERVAL, 67 },
        { MSG_SET_MESSAGE_INTERVAL, 19 },
        { MSG_STATUSTEXT, 83 },
    };

    public static byte GetCrcExtra(uint msgId) =>
        CrcExtra.TryGetValue(msgId, out var v) ? v : (byte)0;

    private static byte _seq;

    public sealed record MavMessage(byte SysId, byte CompId, uint MsgId, byte[] Payload);

    /// <summary>Build a complete MAVLink v2 frame (header + payload + CRC).</summary>
    public static byte[] Encode(uint msgId, byte sysId, byte compId, byte[] payload)
    {
        int totalLen = HeaderLen + payload.Length + 2;
        var frame = new byte[totalLen];

        frame[0] = MagicV2;
        frame[1] = (byte)payload.Length;
        frame[2] = 0;                  // incompat_flags
        frame[3] = 0;                  // compat_flags
        frame[4] = unchecked(_seq++);
        frame[5] = sysId;
        frame[6] = compId;
        frame[7] = (byte)(msgId & 0xFF);
        frame[8] = (byte)((msgId >> 8) & 0xFF);
        frame[9] = (byte)((msgId >> 16) & 0xFF);
        Buffer.BlockCopy(payload, 0, frame, HeaderLen, payload.Length);

        ushort crc = 0xFFFF;
        for (int i = 1; i < HeaderLen + payload.Length; i++)
            crc = CrcAccum(crc, frame[i]);
        crc = CrcAccum(crc, GetCrcExtra(msgId));

        frame[totalLen - 2] = (byte)(crc & 0xFF);
        frame[totalLen - 1] = (byte)((crc >> 8) & 0xFF);
        return frame;
    }

    /// <summary>Decode a MAVLink v2 frame. Returns null on magic/length/CRC failure.</summary>
    public static MavMessage? Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderLen + 2) return null;
        if (data[0] != MagicV2) return null;

        int payloadLen = data[1];
        int expectedLen = HeaderLen + payloadLen + 2;
        if (data.Length < expectedLen) return null;

        byte sysId = data[5];
        byte compId = data[6];
        uint msgId = (uint)(data[7] | (data[8] << 8) | (data[9] << 16));

        // Verify CRC if we know the message
        if (CrcExtra.ContainsKey(msgId))
        {
            ushort crc = 0xFFFF;
            for (int i = 1; i < HeaderLen + payloadLen; i++)
                crc = CrcAccum(crc, data[i]);
            crc = CrcAccum(crc, GetCrcExtra(msgId));

            ushort frameCrc = (ushort)(data[HeaderLen + payloadLen] |
                                       (data[HeaderLen + payloadLen + 1] << 8));
            if (crc != frameCrc) return null;
        }

        var payload = new byte[payloadLen];
        data.Slice(HeaderLen, payloadLen).CopyTo(payload);
        return new MavMessage(sysId, compId, msgId, payload);
    }

    private static ushort CrcAccum(ushort crc, byte b)
    {
        byte tmp = (byte)(b ^ (crc & 0xFF));
        tmp ^= (byte)(tmp << 4);
        return (ushort)((crc >> 8) ^ (tmp << 8) ^ (tmp << 3) ^ (tmp >> 4));
    }

    // ── Helpers ────────────────────────────────────────────────────────

    public static byte[] EncodeHeartbeat(byte sysId = GcsSysId, byte compId = GcsCompId,
        byte type = 6 /* MAV_TYPE_GCS */, byte autopilot = 8 /* MAV_AUTOPILOT_INVALID */,
        byte baseMode = 0, uint customMode = 0, byte systemStatus = 4 /* MAV_STATE_ACTIVE */,
        byte mavlinkVersion = 3)
    {
        var p = new byte[9];
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), customMode);
        p[4] = type;
        p[5] = autopilot;
        p[6] = baseMode;
        p[7] = systemStatus;
        p[8] = mavlinkVersion;
        return Encode(MSG_HEARTBEAT, sysId, compId, p);
    }

    public static byte[] EncodeCommandLong(byte targetSysId, byte targetCompId, ushort command,
        float p1 = 0, float p2 = 0, float p3 = 0, float p4 = 0, float p5 = 0, float p6 = 0, float p7 = 0,
        byte confirmation = 0)
    {
        var p = new byte[33];
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(0, 4), p1);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(4, 4), p2);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(8, 4), p3);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(12, 4), p4);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(16, 4), p5);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(20, 4), p6);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(24, 4), p7);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(28, 2), command);
        p[30] = targetSysId;
        p[31] = targetCompId;
        p[32] = confirmation;
        return Encode(MSG_COMMAND_LONG, GcsSysId, GcsCompId, p);
    }

    public static byte[] EncodeSetMode(byte targetSysId, byte baseMode, uint customMode)
    {
        var p = new byte[6];
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), customMode);
        p[4] = targetSysId;
        p[5] = baseMode;
        return Encode(MSG_SET_MODE, GcsSysId, GcsCompId, p);
    }

    public static byte[] EncodeMissionCount(byte targetSysId, byte targetCompId, ushort count,
        byte missionType = 0 /* MAV_MISSION_TYPE_MISSION */)
    {
        var p = new byte[5];
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0, 2), count);
        p[2] = targetSysId;
        p[3] = targetCompId;
        p[4] = missionType;
        return Encode(MSG_MISSION_COUNT, GcsSysId, GcsCompId, p);
    }

    public static byte[] EncodeRequestDataStream(byte targetSysId, byte targetCompId,
        byte reqStreamId, ushort reqMessageRate, byte startStop)
    {
        var p = new byte[6];
        p[0] = targetSysId;
        p[1] = targetCompId;
        p[2] = reqStreamId;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(3, 2), reqMessageRate);
        p[5] = startStop;
        return Encode(MSG_REQUEST_DATA_STREAM, GcsSysId, GcsCompId, p);
    }

    public static byte[] EncodeSetMessageInterval(byte targetSysId, byte targetCompId,
        uint messageId, int intervalUs, byte responseTarget = 0)
    {
        var p = new byte[11];
        p[0] = targetSysId;
        p[1] = targetCompId;
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(2, 4), messageId);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(6, 4), intervalUs);
        p[10] = responseTarget;
        return Encode(MSG_SET_MESSAGE_INTERVAL, GcsSysId, GcsCompId, p);
    }

    public static byte[] EncodeMissionItemInt(byte targetSysId, byte targetCompId, ushort seq,
        byte frame, ushort command, byte current, byte autocontinue,
        float p1, float p2, float p3, float p4, int latE7, int lonE7, float alt,
        byte missionType = 0)
    {
        var p = new byte[38];
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(0, 4), p1);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(4, 4), p2);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(8, 4), p3);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(12, 4), p4);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(16, 4), latE7);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(20, 4), lonE7);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(24, 4), alt);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(28, 2), seq);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(30, 2), command);
        p[32] = targetSysId;
        p[33] = targetCompId;
        p[34] = frame;
        p[35] = current;
        p[36] = autocontinue;
        p[37] = missionType;
        return Encode(MSG_MISSION_ITEM_INT, GcsSysId, GcsCompId, p);
    }

    public static byte[] EncodeSetPositionTargetGlobalInt(byte targetSysId, byte targetCompId,
        ushort typeMask, byte coordinateFrame, int latE7, int lonE7, float alt,
        float vx = 0, float vy = 0, float vz = 0,
        float afx = 0, float afy = 0, float afz = 0,
        float yaw = 0, float yawRate = 0)
    {
        var p = new byte[53];
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), 0);           // time_boot_ms
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(4, 4), latE7);        // lat_int
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(8, 4), lonE7);        // lon_int
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(12, 4), alt);        // alt
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(16, 4), vx);         // vx
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(20, 4), vy);         // vy
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(24, 4), vz);         // vz
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(28, 4), afx);        // afx
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(32, 4), afy);        // afy
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(36, 4), afz);        // afz
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(40, 4), yaw);        // yaw
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(44, 4), yawRate);    // yaw_rate
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(48, 2), typeMask);   // type_mask
        p[50] = targetSysId;
        p[51] = targetCompId;
        p[52] = coordinateFrame;
        return Encode(MSG_SET_POSITION_TARGET_GLOBAL_INT, GcsSysId, GcsCompId, p);
    }
}
