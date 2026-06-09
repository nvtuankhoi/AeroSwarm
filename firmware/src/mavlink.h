// Minimal MAVLink v2 encode/decode helpers — common dialect subset.
// Hand-written to avoid pulling in the full c_library_v2 submodule
// (which is ~1000 headers). CRC X.25 with CRC_EXTRA seed byte per spec.
#pragma once
#include <Arduino.h>
#include <string.h>

namespace mavlink {

constexpr uint8_t MAGIC_V2     = 0xFD;
constexpr uint8_t HEADER_LEN   = 10;

// Message IDs
constexpr uint32_t MSG_HEARTBEAT           = 0;
constexpr uint32_t MSG_SYS_STATUS          = 1;
constexpr uint32_t MSG_SET_MODE            = 11;
constexpr uint32_t MSG_GPS_RAW_INT         = 24;
constexpr uint32_t MSG_GLOBAL_POSITION_INT = 33;
constexpr uint32_t MSG_MISSION_COUNT       = 44;
constexpr uint32_t MSG_MISSION_CLEAR_ALL   = 45;
constexpr uint32_t MSG_MISSION_ACK         = 47;
constexpr uint32_t MSG_MISSION_REQUEST_INT = 51;
constexpr uint32_t MSG_MISSION_ITEM_INT    = 73;
constexpr uint32_t MSG_SET_POSITION_TARGET_GLOBAL_INT = 86;
constexpr uint32_t MSG_COMMAND_LONG        = 76;
constexpr uint32_t MSG_COMMAND_ACK         = 77;
constexpr uint32_t MSG_BATTERY_STATUS      = 147;
constexpr uint32_t MSG_NAMED_VALUE_FLOAT   = 251;
constexpr uint32_t MSG_STATUSTEXT          = 253;

// MAV_CMD constants
constexpr uint16_t CMD_NAV_WAYPOINT          = 16;
constexpr uint16_t CMD_NAV_RETURN_TO_LAUNCH  = 20;
constexpr uint16_t CMD_NAV_LAND              = 21;
constexpr uint16_t CMD_NAV_TAKEOFF           = 22;
constexpr uint16_t CMD_DO_SET_HOME           = 179;
constexpr uint16_t CMD_COMPONENT_ARM_DISARM  = 400;

// CRC_EXTRA seed byte per common dialect (subset we use).
inline uint8_t crcExtra(uint32_t msgId) {
    switch (msgId) {
        case MSG_HEARTBEAT:           return 50;
        case MSG_SYS_STATUS:          return 124;
        case MSG_SET_MODE:            return 89;
        case MSG_GPS_RAW_INT:         return 24;
        case MSG_GLOBAL_POSITION_INT: return 104;
        case MSG_MISSION_COUNT:       return 221;
        case MSG_MISSION_CLEAR_ALL:   return 232;
        case MSG_MISSION_ACK:         return 153;
        case MSG_MISSION_REQUEST_INT: return 38;
        case MSG_MISSION_ITEM_INT:    return 38;
        case MSG_SET_POSITION_TARGET_GLOBAL_INT: return 5;
        case MSG_COMMAND_LONG:        return 152;
        case MSG_COMMAND_ACK:         return 143;
        case MSG_BATTERY_STATUS:      return 154;
        case MSG_NAMED_VALUE_FLOAT:   return 145;
        case MSG_STATUSTEXT:          return 83;
        default:                      return 0;
    }
}

inline uint16_t crcAccum(uint16_t crc, uint8_t b) {
    uint8_t tmp = b ^ (uint8_t)(crc & 0xFF);
    tmp ^= (tmp << 4);
    return (crc >> 8) ^ ((uint16_t)tmp << 8) ^ ((uint16_t)tmp << 3) ^ ((uint16_t)tmp >> 4);
}

// Build a MAVLink v2 frame. Returns total length.
inline int encode(uint8_t* out, uint32_t msgId, uint8_t sysId, uint8_t compId,
                  const uint8_t* payload, uint8_t payloadLen) {
    static uint8_t seq = 0;
    out[0] = MAGIC_V2;
    out[1] = payloadLen;
    out[2] = 0; out[3] = 0;          // incompat/compat flags
    out[4] = seq++;
    out[5] = sysId;
    out[6] = compId;
    out[7] = (uint8_t)(msgId & 0xFF);
    out[8] = (uint8_t)((msgId >> 8) & 0xFF);
    out[9] = (uint8_t)((msgId >> 16) & 0xFF);
    memcpy(out + HEADER_LEN, payload, payloadLen);

    uint16_t crc = 0xFFFF;
    for (int i = 1; i < HEADER_LEN + payloadLen; i++) crc = crcAccum(crc, out[i]);
    crc = crcAccum(crc, crcExtra(msgId));
    out[HEADER_LEN + payloadLen]     = (uint8_t)(crc & 0xFF);
    out[HEADER_LEN + payloadLen + 1] = (uint8_t)((crc >> 8) & 0xFF);
    return HEADER_LEN + payloadLen + 2;
}

// Parse a frame. Returns true if valid (writes msgId/sysId/payload).
struct Decoded {
    uint8_t sysId;
    uint8_t compId;
    uint32_t msgId;
    uint8_t payload[256];
    uint8_t payloadLen;
};

inline bool decode(const uint8_t* data, int len, Decoded& out) {
    if (len < HEADER_LEN + 2) return false;
    if (data[0] != MAGIC_V2) return false;
    uint8_t payloadLen = data[1];
    if (len < HEADER_LEN + payloadLen + 2) return false;

    out.sysId = data[5];
    out.compId = data[6];
    out.msgId = (uint32_t)data[7] | ((uint32_t)data[8] << 8) | ((uint32_t)data[9] << 16);
    out.payloadLen = payloadLen;
    memcpy(out.payload, data + HEADER_LEN, payloadLen);

    // Validate CRC if we know the message
    uint8_t extra = crcExtra(out.msgId);
    if (extra != 0) {
        uint16_t crc = 0xFFFF;
        for (int i = 1; i < HEADER_LEN + payloadLen; i++) crc = crcAccum(crc, data[i]);
        crc = crcAccum(crc, extra);
        uint16_t frameCrc = (uint16_t)data[HEADER_LEN + payloadLen] |
                            ((uint16_t)data[HEADER_LEN + payloadLen + 1] << 8);
        if (crc != frameCrc) return false;
    }
    return true;
}

// ── High-level encoders ───────────────────────────────────────────────

inline int encHeartbeat(uint8_t* out, uint8_t sysId, uint8_t type, uint8_t autopilot,
                         uint8_t baseMode, uint32_t customMode, uint8_t systemStatus) {
    uint8_t p[9] = {};
    memcpy(p, &customMode, 4);
    p[4] = type;
    p[5] = autopilot;
    p[6] = baseMode;
    p[7] = systemStatus;
    p[8] = 3; // MAVLink version
    return encode(out, MSG_HEARTBEAT, sysId, 1, p, sizeof(p));
}

inline int encGlobalPositionInt(uint8_t* out, uint8_t sysId,
                                 double lat, double lon, float alt,
                                 float vx_mps, float vy_mps, float vz_mps,
                                 float hdgDeg) {
    uint8_t p[28] = {};
    uint32_t tBoot = millis();
    int32_t latE7 = (int32_t)(lat * 1e7);
    int32_t lonE7 = (int32_t)(lon * 1e7);
    int32_t altMm = (int32_t)(alt * 1000.0f);
    int32_t relAltMm = altMm;
    int16_t vx = (int16_t)(vx_mps * 100.0f);
    int16_t vy = (int16_t)(vy_mps * 100.0f);
    int16_t vz = (int16_t)(vz_mps * 100.0f);
    uint16_t hdg = (uint16_t)(hdgDeg * 100.0f);
    memcpy(p + 0,  &tBoot,    4);
    memcpy(p + 4,  &latE7,    4);
    memcpy(p + 8,  &lonE7,    4);
    memcpy(p + 12, &altMm,    4);
    memcpy(p + 16, &relAltMm, 4);
    memcpy(p + 20, &vx,       2);
    memcpy(p + 22, &vy,       2);
    memcpy(p + 24, &vz,       2);
    memcpy(p + 26, &hdg,      2);
    return encode(out, MSG_GLOBAL_POSITION_INT, sysId, 1, p, sizeof(p));
}

inline int encBatteryStatus(uint8_t* out, uint8_t sysId, float voltage, int8_t percent) {
    uint8_t p[36] = {};
    uint16_t voltMv = (uint16_t)(voltage * 1000.0f);
    memcpy(p + 10, &voltMv, 2);
    p[33] = (uint8_t)percent;
    return encode(out, MSG_BATTERY_STATUS, sysId, 1, p, sizeof(p));
}

inline int encCommandAck(uint8_t* out, uint8_t sysId, uint16_t cmd, uint8_t result) {
    uint8_t p[3] = {};
    memcpy(p, &cmd, 2);
    p[2] = result;
    return encode(out, MSG_COMMAND_ACK, sysId, 1, p, sizeof(p));
}

inline int encMissionAck(uint8_t* out, uint8_t sysId, uint8_t targetSys, uint8_t result) {
    uint8_t p[3] = {};
    p[0] = targetSys;
    p[1] = 1;       // target_component
    p[2] = result;
    return encode(out, MSG_MISSION_ACK, sysId, 1, p, sizeof(p));
}

inline int encMissionRequestInt(uint8_t* out, uint8_t sysId, uint8_t targetSys, uint16_t seq) {
    uint8_t p[4] = {};
    memcpy(p, &seq, 2);
    p[2] = targetSys;
    p[3] = 1;
    return encode(out, MSG_MISSION_REQUEST_INT, sysId, 1, p, sizeof(p));
}

inline int encStatusText(uint8_t* out, uint8_t sysId, uint8_t severity, const char* text) {
    uint8_t p[51] = {};
    p[0] = severity;
    strncpy((char*)(p + 1), text, 50);
    return encode(out, MSG_STATUSTEXT, sysId, 1, p, sizeof(p));
}

// ── Payload accessors ────────────────────────────────────────────────

struct CommandLongParams {
    float p[7];
    uint16_t command;
    uint8_t targetSys;
    uint8_t targetComp;
    uint8_t confirmation;
};

inline bool parseCommandLong(const uint8_t* payload, uint8_t len, CommandLongParams& out) {
    if (len < 33) return false;
    memcpy(out.p, payload, 28);
    memcpy(&out.command, payload + 28, 2);
    out.targetSys = payload[30];
    out.targetComp = payload[31];
    out.confirmation = payload[32];
    return true;
}

struct SetModePayload {
    uint32_t customMode;
    uint8_t targetSys;
    uint8_t baseMode;
};

inline bool parseSetMode(const uint8_t* payload, uint8_t len, SetModePayload& out) {
    if (len < 6) return false;
    memcpy(&out.customMode, payload, 4);
    out.targetSys = payload[4];
    out.baseMode = payload[5];
    return true;
}

struct MissionItemInt {
    float p1, p2, p3, p4;
    int32_t latE7;
    int32_t lonE7;
    float alt;
    uint16_t seq;
    uint16_t command;
    uint8_t targetSys;
    uint8_t targetComp;
    uint8_t frame;
    uint8_t current;
    uint8_t autocontinue;
    uint8_t missionType;
};

inline bool parseMissionItemInt(const uint8_t* payload, uint8_t len, MissionItemInt& out) {
    if (len < 38) return false;
    memcpy(&out.p1, payload + 0, 4);
    memcpy(&out.p2, payload + 4, 4);
    memcpy(&out.p3, payload + 8, 4);
    memcpy(&out.p4, payload + 12, 4);
    memcpy(&out.latE7, payload + 16, 4);
    memcpy(&out.lonE7, payload + 20, 4);
    memcpy(&out.alt,   payload + 24, 4);
    memcpy(&out.seq,   payload + 28, 2);
    memcpy(&out.command, payload + 30, 2);
    out.targetSys = payload[32];
    out.targetComp = payload[33];
    out.frame = payload[34];
    out.current = payload[35];
    out.autocontinue = payload[36];
    out.missionType = payload[37];
    return true;
}

struct MissionCountPayload {
    uint16_t count;
    uint8_t targetSys;
    uint8_t targetComp;
    uint8_t missionType;
};

inline bool parseMissionCount(const uint8_t* payload, uint8_t len, MissionCountPayload& out) {
    if (len < 5) return false;
    memcpy(&out.count, payload, 2);
    out.targetSys = payload[2];
    out.targetComp = payload[3];
    out.missionType = payload[4];
    return true;
}

struct SetPositionTargetGlobalIntPayload {
    int32_t latE7;
    int32_t lonE7;
    float alt;
    uint16_t typeMask;
    uint8_t targetSys;
    uint8_t targetComp;
    uint8_t coordinateFrame;
};

inline bool parseSetPositionTargetGlobalInt(const uint8_t* payload, uint8_t len, SetPositionTargetGlobalIntPayload& out) {
    if (len < 53) return false;
    memcpy(&out.latE7, payload + 4, 4);
    memcpy(&out.lonE7, payload + 8, 4);
    memcpy(&out.alt, payload + 12, 4);
    memcpy(&out.typeMask, payload + 48, 2);
    out.targetSys = payload[50];
    out.targetComp = payload[51];
    out.coordinateFrame = payload[52];
    return true;
}

struct NamedValueFloatPayload {
    uint32_t timeBootMs;
    char name[10];
    float value;
};

inline bool parseNamedValueFloat(const uint8_t* payload, uint8_t len, NamedValueFloatPayload& out) {
    if (len < 18) return false;
    memcpy(&out.timeBootMs, payload, 4);
    memcpy(out.name, payload + 4, 10);
    out.name[9] = '\0';
    memcpy(&out.value, payload + 14, 4);
    return true;
}

} // namespace mavlink
